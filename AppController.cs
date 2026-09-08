using System.Windows;
using System.Windows.Forms;
using RealtimeTranslator.Capture;
using RealtimeTranslator.Configuration;
using RealtimeTranslator.Models;
using RealtimeTranslator.Pipeline;
using RealtimeTranslator.Recognition;
using RealtimeTranslator.Services;
using RealtimeTranslator.UI;
using Application = System.Windows.Application;
using Drawing = System.Drawing;
using WinForms = System.Windows.Forms;

namespace RealtimeTranslator;

public sealed class AppController : IDisposable
{
    private readonly SettingsService _settingsService;
    private readonly StartupService _startupService = new();
    private readonly GlobalHotkeyService _hotkeys = new();
    private AppSettings _settings;
    private HistoryService _history = null!;
    private TranslationCoordinator _coordinator = null!;
    private MainWindow _main = null!;
    private TranslationWindow _translation = null!;
    private ScreenTextMonitor? _screenMonitor;
    private AudioLoopbackCapture? _audioCapture;
    private DeepgramSpeechClient? _speech;
    private RegionInfo? _region;
    private WinForms.NotifyIcon? _notifyIcon;
    private bool _running;
    private bool _paused;
    private bool _starting;
    private bool _isExiting;
    private bool _disposed;

    public AppController()
    {
        _settingsService = new SettingsService();
        _settings = _settingsService.Load();
    }

    public bool IsExiting => _isExiting;

    public void Initialize()
    {
        ApplyStartupPreferenceSilently();

        _history = new HistoryService(_settings.HistoryLimit);
        _coordinator = new TranslationCoordinator(_settings, _history);
        _main = new MainWindow();
        _translation = new TranslationWindow();

        _main.Initialize(this, _history, _coordinator, _settings);
        _translation.Initialize(_coordinator, _settings);
        _coordinator.CurrentTranslationChanged += _translation.ShowCurrentText;

        _hotkeys.RegistrationFailed += (_, message) => _main.Dispatcher.BeginInvoke(() =>
        {
            _main.ShowStatus(message);
        });

        _main.Show();
        _translation.ShowAndActivate();
        CreateTrayIcon();
        _hotkeys.Attach(_main);
        RegisterHotkeys();
    }

    public void SetMode(CaptureMode mode)
    {
        if (_settings.Mode == mode && !_running && !_paused)
        {
            _main.UpdateModeUi();
            return;
        }

        StopRuntime();
        _settings.Mode = mode;
        _main.RefreshFromSettings();
        _main.UpdateRuntimeState("就绪", running: false, paused: false);
        _main.ShowStatus(mode == CaptureMode.Audio ? "电脑声音模式需要在线语音密钥。" : "请框选屏幕区域后开始。");
    }

    public void SetTargetLanguage(LanguageOption language)
    {
        _settings.TargetLanguageCode = language.Code;
        _settings.TargetLanguageName = language.Name;
        _coordinator.UpdateSettings(_settings);
        _main.ShowStatus($"目标语言：{language.Name}");
    }

    public void PreviewTranslationOpacity(double opacity)
    {
        _settings.TranslationOpacity = opacity;
        _translation.ApplySettings(_settings);
    }

    public void ApplySettings(AppSettings settings)
    {
        var previousMode = _settings.Mode;
        _settings = settings;

        if (previousMode != settings.Mode)
        {
            StopRuntime();
        }

        _history.Limit = settings.HistoryLimit;
        _coordinator.UpdateSettings(settings);
        _screenMonitor?.UpdateSettings(settings);
        _translation.ApplySettings(settings);
        _main.RefreshFromSettings();
        ApplyStartupPreference(settings.StartWithWindows);
        RegisterHotkeys();
        SaveSettings();
        _main.UpdateRuntimeState("就绪", running: false, paused: false);
    }

    public void ToggleStartPause()
    {
        if (_starting)
        {
            return;
        }

        if (_running)
        {
            StopRuntime();
            _running = false;
            _paused = true;
            _main.UpdateRuntimeState(DisplayState.Paused, running: false, paused: true);
            return;
        }

        _ = StartRuntimeAsync();
    }

    public void SelectRegion()
    {
        if (_settings.Mode != CaptureMode.Screen)
        {
            _main.ShowStatus("当前模式为电脑声音，不需要框选屏幕区域。");
            return;
        }

        var wasActive = _running || _paused;
        StopRuntime();
        var region = PromptRegion();
        if (region is null)
        {
            _main.UpdateRuntimeState(DisplayState.Ready, running: false, paused: false);
            _main.ShowStatus("已取消框选。");
            return;
        }

        _region = region;
        _main.SetRegion(region);
        if (wasActive)
        {
            _ = StartRuntimeAsync();
        }
        else
        {
            _main.ShowStatus("识别区域已更新，点击“开始”即可运行。");
        }
    }

    public void ReselectRegion()
    {
        SelectRegion();
    }

    public void ShowTranslationWindow()
    {
        _translation.ShowAndActivate();
    }

    public void ShowMainWindow()
    {
        if (!_main.IsVisible)
        {
            _main.Show();
        }
        if (_main.WindowState == WindowState.Minimized)
        {
            _main.WindowState = WindowState.Normal;
        }
        _main.Activate();
    }

    public void RequestExit()
    {
        if (_isExiting)
        {
            return;
        }

        _isExiting = true;
        StopRuntime();
        SaveSettings();

        _main.ExitConfirmed = true;
        _translation.ExitConfirmed = true;
        if (_notifyIcon is not null)
        {
            _notifyIcon.Visible = false;
            _notifyIcon.Dispose();
            _notifyIcon = null;
        }
        Application.Current.Shutdown();
    }

    public void ReportFatal(Exception exception)
    {
        try
        {
            var message = exception.Message;
            if (_main is not null && _main.IsVisible)
            {
                _main.ShowStatus($"发生异常：{message}");
                return;
            }

            _notifyIcon?.ShowBalloonTip(
                5000,
                "实时翻译",
                $"发生异常：{message}",
                WinForms.ToolTipIcon.Warning);
        }
        catch
        {
            // Nothing safe left to display.
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }
        _disposed = true;

        StopRuntime();
        _hotkeys.Dispose();
        _notifyIcon?.Dispose();
        _notifyIcon = null;
    }

    private async Task StartRuntimeAsync()
    {
        if (_starting)
        {
            return;
        }

        _starting = true;
        _paused = false;
        try
        {
            if (_settings.Mode == CaptureMode.Screen)
            {
                StartScreenRuntime();
            }
            else
            {
                await StartAudioRuntimeAsync();
            }
        }
        catch (Exception ex)
        {
            StopRuntime();
            _main.ShowStatus(ex.Message);
            _main.UpdateRuntimeState(DisplayState.Error, running: false, paused: false);
        }
        finally
        {
            _starting = false;
        }
    }

    private void StartScreenRuntime()
    {
        if (_region is null)
        {
            var region = PromptRegion();
            if (region is null)
            {
                _main.ShowStatus("需要先框选字幕区域。");
                _main.UpdateRuntimeState(DisplayState.Ready, running: false, paused: false);
                return;
            }
            _region = region;
            _main.SetRegion(region);
        }

        _screenMonitor = new ScreenTextMonitor(_settingsService, _settings, _main.Dispatcher);
        _screenMonitor.LineRecognized += text => _coordinator.SubmitSourceLine(text, "Screen");
        _screenMonitor.Message += message => _main.Dispatcher.BeginInvoke(() => _main.ShowStatus(message));
        _screenMonitor.Start(_region);

        if (_screenMonitor.IsRunning)
        {
            _running = true;
            _main.UpdateRuntimeState(DisplayState.RunningScreen, running: true, paused: false);
        }
    }

    private async Task StartAudioRuntimeAsync()
    {
        if (string.IsNullOrWhiteSpace(_settings.DeepgramApiKey))
        {
            throw new InvalidOperationException("尚未配置 Deepgram API 密钥，请在设置页填写后重试。");
        }

        _main.UpdateRuntimeState(DisplayState.Starting, running: true, paused: false, connecting: true);
        _audioCapture = new AudioLoopbackCapture();
        _audioCapture.Prepare();

        _speech = new DeepgramSpeechClient(_settings);
        _speech.UtteranceRecognized += line => _main.Dispatcher.BeginInvoke(() =>
            _coordinator.SubmitSourceLine(line, "Audio"));
        _speech.Message += message => _main.Dispatcher.BeginInvoke(() => _main.ShowStatus(message));
        _audioCapture.Pcm16Data += pcm => _speech.EnqueueAudio(pcm);
        _audioCapture.Failed += message => _main.Dispatcher.BeginInvoke(() => _main.ShowStatus(message));

        await _speech.StartAsync(_audioCapture.SampleRate, CancellationToken.None);
        _audioCapture.Start();
        _running = true;
        _main.UpdateRuntimeState(DisplayState.RunningAudio, running: true, paused: false);
        _main.ShowStatus("正在采集电脑正在播放的声音，麦克风不会被使用。");
    }

    private void StopRuntime()
    {
        _screenMonitor?.Stop();
        _screenMonitor = null;

        try
        {
            _audioCapture?.Stop();
        }
        catch
        {
            // NAudio can throw if the endpoint disappeared mid-capture.
        }
        _audioCapture = null;

        if (_speech is not null)
        {
            try
            {
                _speech.StopAsync().GetAwaiter().GetResult();
            }
            catch
            {
                // Connection cleanup is best effort.
            }
            _speech = null;
        }

        _coordinator.CancelPending();
        _running = false;
        _paused = false;
        _main.UpdateRuntimeState(DisplayState.Ready, running: false, paused: false);
    }

    private RegionInfo? PromptRegion()
    {
        _main.Hide();
        try
        {
            var cursor = WinForms.Cursor.Position;
            var screen = WinForms.Screen.FromPoint(cursor);
            using var selector = new RegionSelectorForm(screen.Bounds);
            var result = selector.ShowDialog();
            if (result != WinForms.DialogResult.OK || !selector.SelectedRegion.IsValid())
            {
                return null;
            }

            return new RegionInfo(
                selector.SelectedRegion.X,
                selector.SelectedRegion.Y,
                selector.SelectedRegion.Width,
                selector.SelectedRegion.Height);
        }
        finally
        {
            if (!_isExiting)
            {
                ShowMainWindow();
            }
        }
    }

    private void CreateTrayIcon()
    {
        var menu = new WinForms.ContextMenuStrip();
        menu.Items.Add("显示主窗口", null, (_, _) => ShowMainWindow());
        menu.Items.Add("开始 / 暂停", null, (_, _) => ToggleStartPause());
        menu.Items.Add("退出", null, (_, _) => RequestExit());

        Drawing.Icon? icon = null;
        try
        {
            icon = Drawing.Icon.ExtractAssociatedIcon(Environment.ProcessPath ?? string.Empty);
        }
        catch
        {
            // Fall through to the system icon.
        }

        _notifyIcon = new WinForms.NotifyIcon
        {
            Text = "实时翻译",
            Icon = icon ?? Drawing.SystemIcons.Application,
            ContextMenuStrip = menu,
            Visible = true
        };
        _notifyIcon.DoubleClick += (_, _) => ShowMainWindow();
    }

    private void RegisterHotkeys()
    {
        _hotkeys.UnregisterAll();
        _hotkeys.Register(_settings.ToggleHotkey, ToggleStartPause);
        _hotkeys.Register(_settings.ReselectHotkey, ReselectRegion);
    }

    private void SaveSettings()
    {
        try
        {
            _settingsService.Save(_settings);
        }
        catch
        {
            // Configuration is a convenience; a failed save should not take down the app.
        }
    }

    private void ApplyStartupPreferenceSilently()
    {
        try
        {
            ApplyStartupPreference(_settings.StartWithWindows);
        }
        catch
        {
            // Registry access can fail on locked-down systems; the toggle will report on save.
        }
    }

    private void ApplyStartupPreference(bool enabled)
    {
        try
        {
            if (_startupService.IsEnabled != enabled)
            {
                _startupService.SetEnabled(enabled);
            }
        }
        catch (Exception ex)
        {
            _main?.Dispatcher.BeginInvoke(() => _main.ShowStatus($"开机自启设置失败：{ex.Message}"));
        }
    }
}

internal static class RegionInfoExtensions
{
    public static bool IsValid(this Drawing.Rectangle rectangle) =>
        rectangle.Width >= 20 && rectangle.Height >= 20;
}
