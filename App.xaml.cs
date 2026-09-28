using System.Windows;
using SevenDaysModLauncher.Services;
using Application = System.Windows.Application;

namespace SevenDaysModLauncher;

public partial class App : Application
{
    /// <summary>NXM-ссылка из командной строки (клик Mod Manager Download на сайте).</summary>
    public static string? PendingNxmLink { get; private set; }

    public static string? ConsumePendingNxmLink()
    {
        var link = PendingNxmLink;
        PendingNxmLink = null;
        return link;
    }

    public App()
    {
        DispatcherUnhandledException += (_, e) =>
        {
            AppLogger.Error("Unhandled UI exception", e.Exception);
            System.Windows.MessageBox.Show($"Unhandled error:\n{e.Exception.Message}\n\nDetails in:\n{AppLogger.LogPath}",
                "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            e.Handled = true;
        };

        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            AppLogger.Error("Unhandled domain exception", e.ExceptionObject as Exception);
        };

        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            AppLogger.Error("Unobserved task exception", e.Exception);
            e.SetObserved();
        };
    }

    protected override void OnStartup(StartupEventArgs e)
    {
        var link = NexusProtocolService.FindNxmArg(e.Args);

        // Второй экземпляр (клик nxm://) — переслать ссылку первому и выйти
        if (!SingleInstanceService.TryAcquireOrForward(link))
        {
            Shutdown();
            return;
        }

        // Кладем ДО base.OnStartup: окно и VM создаются там/после и забирают ссылку
        PendingNxmLink = link;
        if (link != null)
            AppLogger.Info($"Started with nxm link: {link}");

        base.OnStartup(e);
        AppLogger.Info(NexusProtocolService.EnsureRegistered());
        SingleInstanceService.StartListener(OnNxmLinkFromSecondInstance);
    }

    private void OnNxmLinkFromSecondInstance(string link)
    {
        Dispatcher.InvokeAsync(() =>
        {
            try
            {
                if (MainWindow?.DataContext is ViewModels.MainViewModel vm)
                    _ = vm.HandleNxmLinkAsync(link);
                else
                    PendingNxmLink = link;
            }
            catch (Exception ex)
            {
                AppLogger.Warn($"Second-instance nxm failed: {ex.Message}");
            }
        });
    }

    protected override void OnExit(ExitEventArgs e)
    {
        SingleInstanceService.Stop();
        base.OnExit(e);
    }
}
