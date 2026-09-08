using System.Windows;
using WpfApplication = System.Windows.Application;

namespace RealtimeTranslator;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : WpfApplication
{
    public static AppController Controller { get; private set; } = null!;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        DispatcherUnhandledException += (_, args) =>
        {
            try
            {
                Controller?.ReportFatal(args.Exception);
            }
            catch
            {
                // Tray or UI may already be gone; do not let the exception escape twice.
            }
            args.Handled = true;
        };

        Controller = new AppController();
        Controller.Initialize();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        Controller?.Dispose();
        base.OnExit(e);
    }
}
