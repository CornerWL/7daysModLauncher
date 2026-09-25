using System.Windows;
using SevenDaysModLauncher.Services;
using Application = System.Windows.Application;

namespace SevenDaysModLauncher;

public partial class App : Application
{
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
}
