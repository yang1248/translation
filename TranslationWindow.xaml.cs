using System.ComponentModel;
using System.Windows;
using RealtimeTranslator.Models;
using RealtimeTranslator.Pipeline;

namespace RealtimeTranslator;

public partial class TranslationWindow : Window
{
    private TranslationCoordinator? _coordinator;
    private AppSettings _settings = new();

    public TranslationWindow()
    {
        InitializeComponent();
        Left = SystemParameters.WorkArea.Right - Width - 20;
        Top = SystemParameters.WorkArea.Bottom - Height - 20;
    }

    public bool ExitConfirmed { get; set; }

    public void Initialize(TranslationCoordinator coordinator, AppSettings settings)
    {
        _coordinator = coordinator;
        _settings = settings;
        _coordinator.CurrentTranslationChanged += OnCurrentTranslationChanged;
        ApplySettings(settings);
    }

    public void ApplySettings(AppSettings settings)
    {
        _settings = settings;
        Topmost = settings.TranslationTopmost;
        Opacity = settings.TranslationOpacity;
    }

    public void ShowAndActivate()
    {
        if (!IsVisible)
        {
            Show();
        }
        if (WindowState == WindowState.Minimized)
        {
            WindowState = WindowState.Normal;
        }
        Activate();
    }

    public void ShowCurrentText(CurrentTranslation current)
    {
        if (current.Error is not null)
        {
            TranslationText.Text = "翻译失败";
            ErrorText.Text = current.Error;
            ErrorText.Visibility = Visibility.Visible;
        }
        else if (current.IsWorking)
        {
            TranslationText.Text = "翻译中...";
            ErrorText.Visibility = Visibility.Collapsed;
        }
        else
        {
            TranslationText.Text = current.TranslatedText;
            ErrorText.Visibility = Visibility.Collapsed;
        }

        SourceText.Text = current.SourceText;
        WindowStateText.Text = current.IsWorking ? "正在翻译" : current.Error is null ? "已就绪" : "连接或服务异常";
    }

    private void OnCurrentTranslationChanged(CurrentTranslation current)
    {
        ShowCurrentText(current);
    }

    private void ShowMainButton_Click(object sender, RoutedEventArgs e)
    {
        App.Controller?.ShowMainWindow();
    }

    private void Window_Closing(object? sender, CancelEventArgs e)
    {
        if (ExitConfirmed || App.Controller?.IsExiting == true)
        {
            return;
        }

        e.Cancel = true;
        Hide();
    }
}
