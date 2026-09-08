using System.Collections.Specialized;
using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.Win32;
using RealtimeTranslator.Capture;
using RealtimeTranslator.Configuration;
using RealtimeTranslator.Models;
using RealtimeTranslator.Pipeline;
using RealtimeTranslator.Services;
using RealtimeTranslator.Translation;
using WpfClipboard = System.Windows.Clipboard;
using WpfColor = System.Windows.Media.Color;
using WpfSaveFileDialog = Microsoft.Win32.SaveFileDialog;

namespace RealtimeTranslator;

public partial class MainWindow : Window
{
    private AppController? _controller;
    private HistoryService? _history;
    private TranslationCoordinator? _coordinator;
    private AppSettings _settings = new();
    private bool _initializing = true;
    private string _lastTranslated = string.Empty;

    public MainWindow()
    {
        InitializeComponent();
    }

    public bool ExitConfirmed { get; set; }

    public void Initialize(
        AppController controller,
        HistoryService history,
        TranslationCoordinator coordinator,
        AppSettings settings)
    {
        _controller = controller;
        _history = history;
        _coordinator = coordinator;
        _settings = settings;

        LanguageCombo.ItemsSource = LanguageCatalog.Targets;
        OcrLanguageCombo.ItemsSource = OcrLanguageCatalog.GetAvailable();
        TranslationProviderCombo.ItemsSource = TranslationProviders.All;
        SpeechProviderCombo.Items.Add("Deepgram");

        _coordinator.CurrentTranslationChanged += OnCurrentTranslationChanged;
        _coordinator.StatusMessage += message => Dispatcher.BeginInvoke(() =>
        {
            SourceStatusText.Text = message;
        });

        if (_history is not null)
        {
            HistoryList.ItemsSource = _history.Items;
            _history.Items.CollectionChanged += HistoryItems_CollectionChanged;
        }

        RefreshFromSettings();
        _initializing = false;
        Loaded += (_, _) => UpdateHistoryCount();
    }

    public AppSettings CollectSettingsFromUi()
    {
        _settings.Mode = AudioModeRadio.IsChecked == true ? CaptureMode.Audio : CaptureMode.Screen;
        _settings.TargetLanguageCode = (LanguageCombo.SelectedItem as LanguageOption)?.Code ?? "zh-CN";
        _settings.TargetLanguageName = (LanguageCombo.SelectedItem as LanguageOption)?.Name ?? "简体中文";
        _settings.OcrLanguage = (OcrLanguageCombo.SelectedItem as LanguageOption)?.Code ?? "auto";

        _settings.TranslationTopmost = TopmostCheckBox.IsChecked == true;
        _settings.TranslationOpacity = OpacitySlider.Value;
        _settings.StartWithWindows = StartWithWindowsCheckBox.IsChecked == true;
        _settings.HistoryLimit = int.TryParse(HistoryLimitTextBox.Text, out var historyLimit) ? historyLimit : 10;
        _settings.ScreenIntervalMs = int.TryParse(ScreenIntervalTextBox.Text, out var interval) ? interval : 500;
        _settings.ToggleHotkey = ToggleHotkeyTextBox.Text.Trim();
        _settings.ReselectHotkey = ReselectHotkeyTextBox.Text.Trim();

        _settings.TranslationProvider = TranslationProviderCombo.SelectedItem?.ToString() ?? TranslationProviders.OpenAiCompatible;
        _settings.OpenAiBaseUrl = OpenAiBaseUrlTextBox.Text.Trim();
        _settings.OpenAiModel = OpenAiModelTextBox.Text.Trim();
        _settings.OpenAiApiKey = string.IsNullOrWhiteSpace(OpenAiKeyBox.Password) ? _settings.OpenAiApiKey : OpenAiKeyBox.Password;
        _settings.AzureTranslationKey = string.IsNullOrWhiteSpace(AzureKeyBox.Password) ? _settings.AzureTranslationKey : AzureKeyBox.Password;
        _settings.AzureTranslationRegion = AzureRegionTextBox.Text.Trim();
        _settings.DeepLTranslationKey = string.IsNullOrWhiteSpace(DeepLKeyBox.Password) ? _settings.DeepLTranslationKey : DeepLKeyBox.Password;
        _settings.GoogleTranslationKey = string.IsNullOrWhiteSpace(GoogleKeyBox.Password) ? _settings.GoogleTranslationKey : GoogleKeyBox.Password;
        _settings.DeepgramBaseUrl = DeepgramBaseUrlTextBox.Text.Trim();
        _settings.DeepgramModel = DeepgramModelTextBox.Text.Trim();
        _settings.SpeechLanguage = SpeechLanguageTextBox.Text.Trim();
        _settings.DeepgramApiKey = string.IsNullOrWhiteSpace(DeepgramKeyBox.Password) ? _settings.DeepgramApiKey : DeepgramKeyBox.Password;

        return _settings;
    }

    public void RefreshFromSettings()
    {
        _initializing = true;
        var language = LanguageCatalog.Find(_settings.TargetLanguageCode);
        LanguageCombo.SelectedItem = language ?? LanguageCatalog.Targets[0];

        if (AudioModeRadio.IsChecked != (_settings.Mode == CaptureMode.Audio))
        {
            AudioModeRadio.IsChecked = _settings.Mode == CaptureMode.Audio;
        }
        if (ScreenModeRadio.IsChecked != (_settings.Mode == CaptureMode.Screen))
        {
            ScreenModeRadio.IsChecked = _settings.Mode == CaptureMode.Screen;
        }

        var ocr = OcrLanguageCombo.Items
            .OfType<LanguageOption>()
            .FirstOrDefault(x => x.Code == _settings.OcrLanguage);
        OcrLanguageCombo.SelectedItem = ocr ?? OcrLanguageCombo.Items.OfType<LanguageOption>().FirstOrDefault();

        ScreenIntervalTextBox.Text = _settings.ScreenIntervalMs.ToString();
        HistoryLimitTextBox.Text = _settings.HistoryLimit.ToString();
        TopmostCheckBox.IsChecked = _settings.TranslationTopmost;
        OpacitySlider.Value = _settings.TranslationOpacity;
        StartWithWindowsCheckBox.IsChecked = _settings.StartWithWindows;
        ToggleHotkeyTextBox.Text = _settings.ToggleHotkey;
        ReselectHotkeyTextBox.Text = _settings.ReselectHotkey;

        TranslationProviderCombo.SelectedItem = TranslationProviders.All.Contains(_settings.TranslationProvider)
            ? _settings.TranslationProvider
            : TranslationProviders.OpenAiCompatible;
        SpeechProviderCombo.SelectedIndex = 0;

        OpenAiBaseUrlTextBox.Text = _settings.OpenAiBaseUrl;
        OpenAiModelTextBox.Text = _settings.OpenAiModel;
        OpenAiKeyBox.Password = _settings.OpenAiApiKey ?? string.Empty;
        AzureKeyBox.Password = _settings.AzureTranslationKey ?? string.Empty;
        AzureRegionTextBox.Text = _settings.AzureTranslationRegion;
        DeepLKeyBox.Password = _settings.DeepLTranslationKey ?? string.Empty;
        GoogleKeyBox.Password = _settings.GoogleTranslationKey ?? string.Empty;
        DeepgramBaseUrlTextBox.Text = _settings.DeepgramBaseUrl;
        DeepgramModelTextBox.Text = _settings.DeepgramModel;
        SpeechLanguageTextBox.Text = _settings.SpeechLanguage;
        DeepgramKeyBox.Password = _settings.DeepgramApiKey ?? string.Empty;

        OpacityValueText.Text = $"{_settings.TranslationOpacity * 100:0}%";
        _initializing = false;
        UpdateModeUi();
    }

    public void UpdateModeUi()
    {
        var isScreen = _settings.Mode == CaptureMode.Screen;
        SelectRegionButton.IsEnabled = isScreen;
        ReselectButton.IsEnabled = isScreen && !string.IsNullOrWhiteSpace(RegionStatusText.Text) &&
                                   RegionStatusText.Text != "尚未选择识别区域";
        if (isScreen)
        {
            SourceStatusText.Text = _settings.RegionText;
        }
    }

    public void SetRegion(RegionInfo? region)
    {
        if (region is null)
        {
            RegionStatusText.Text = "尚未选择识别区域";
            return;
        }

        RegionStatusText.Text = $"识别区域：屏幕坐标 ({region.X}, {region.Y})，尺寸 {region.Width} x {region.Height}";
        _settings.RegionText = $"识别区域 {region.Width} x {region.Height}";
        ReselectButton.IsEnabled = true;
        UpdateModeUi();
    }

    public void UpdateRuntimeState(string text, bool running, bool paused, bool connecting = false)
    {
        StateText.Text = text;
        StateDot.Fill = connecting
            ? new SolidColorBrush(WpfColor.FromRgb(0xE8, 0xA4, 0x2B))
            : paused
                ? new SolidColorBrush(WpfColor.FromRgb(0xE8, 0xA4, 0x2B))
                : running
                    ? new SolidColorBrush(WpfColor.FromRgb(0x2E, 0x8B, 0x57))
                    : new SolidColorBrush(WpfColor.FromRgb(0x9A, 0xA5, 0xB1));
        StartPauseButton.Content = paused ? "继续" : running ? "暂停" : "开始";
    }

    public void ShowStatus(string message)
    {
        SourceStatusText.Text = message;
    }

    private void OnCurrentTranslationChanged(CurrentTranslation current)
    {
        CurrentSourceText.Text = current.SourceText;
        CurrentTimeText.Text = DateTime.Now.ToString("HH:mm:ss");

        if (current.Error is not null)
        {
            CurrentTranslationText.Text = "翻译失败";
            CurrentErrorText.Text = current.Error;
            CurrentErrorText.Visibility = Visibility.Visible;
        }
        else if (current.IsWorking)
        {
            CurrentTranslationText.Text = "翻译中...";
            CurrentErrorText.Visibility = Visibility.Collapsed;
            _lastTranslated = string.Empty;
        }
        else
        {
            CurrentTranslationText.Text = current.TranslatedText;
            CurrentErrorText.Visibility = Visibility.Collapsed;
            _lastTranslated = current.TranslatedText;
        }
    }

    private void HistoryItems_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        UpdateHistoryCount();
        if (e.Action == NotifyCollectionChangedAction.Add && HistoryList.Items.Count > 0)
        {
            HistoryList.ScrollIntoView(HistoryList.Items[^1]);
        }
    }

    private void UpdateHistoryCount()
    {
        if (_history is null)
        {
            return;
        }
        HistoryCountText.Text = $"{_history.Items.Count} 条";
    }

    private void ModeRadio_Checked(object sender, RoutedEventArgs e)
    {
        if (_controller is null || _initializing)
        {
            return;
        }

        var mode = AudioModeRadio.IsChecked == true ? CaptureMode.Audio : CaptureMode.Screen;
        _controller.SetMode(mode);
    }

    private void LanguageCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_controller is null || _initializing || LanguageCombo.SelectedItem is not LanguageOption language)
        {
            return;
        }
        _controller.SetTargetLanguage(language);
    }

    private void OpacitySlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        OpacityValueText.Text = $"{e.NewValue * 100:0}%";
        _controller?.PreviewTranslationOpacity(e.NewValue);
    }

    private void StartPauseButton_Click(object sender, RoutedEventArgs e) => _controller?.ToggleStartPause();

    private void SelectRegionButton_Click(object sender, RoutedEventArgs e) => _controller?.SelectRegion();

    private void ReselectRegionButton_Click(object sender, RoutedEventArgs e) => _controller?.ReselectRegion();

    private void ShowTranslationButton_Click(object sender, RoutedEventArgs e) => _controller?.ShowTranslationWindow();

    private void HideMainButton_Click(object sender, RoutedEventArgs e) => Hide();

    private void CopyCurrentButton_Click(object sender, RoutedEventArgs e)
    {
        if (!string.IsNullOrWhiteSpace(_lastTranslated))
        {
            WpfClipboard.SetText(_lastTranslated);
            SourceStatusText.Text = "当前译文已复制。";
        }
    }

    private void ClearHistoryButton_Click(object sender, RoutedEventArgs e)
    {
        _history?.Clear();
        UpdateHistoryCount();
        SourceStatusText.Text = "历史记录已清空。";
    }

    private void CopySelectedHistoryButton_Click(object sender, RoutedEventArgs e)
    {
        if (HistoryList.SelectedItem is HistoryEntry entry)
        {
            WpfClipboard.SetText(entry.TranslatedText);
            SourceStatusText.Text = "所选译文已复制。";
        }
    }

    private async void ExportHistoryButton_Click(object sender, RoutedEventArgs e)
    {
        if (_history is null || _history.Items.Count == 0)
        {
            SourceStatusText.Text = "暂无历史记录可导出。";
            return;
        }

        var dialog = new WpfSaveFileDialog
        {
            Title = "导出历史译文",
            Filter = "文本文件 (*.txt)|*.txt",
            DefaultExt = ".txt",
            FileName = $"实时翻译历史_{DateTime.Now:yyyyMMdd_HHmmss}.txt"
        };
        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        try
        {
            await _history.ExportAsync(dialog.FileName);
            SourceStatusText.Text = $"已导出到 {Path.GetFileName(dialog.FileName)}";
        }
        catch (Exception ex)
        {
            SourceStatusText.Text = $"导出失败：{ex.Message}";
        }
    }

    private void SaveSettingsButton_Click(object sender, RoutedEventArgs e)
    {
        _controller?.ApplySettings(CollectSettingsFromUi());
        SettingsStatusText.Text = "设置已保存";
    }

    private void Window_Closing(object? sender, CancelEventArgs e)
    {
        if (ExitConfirmed || _controller?.IsExiting == true)
        {
            return;
        }

        e.Cancel = true;
        Hide();
    }
}
