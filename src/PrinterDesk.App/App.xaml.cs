using System.Windows;
using PrinterDesk.Core;
using PrinterDesk.Core.Settings;

namespace PrinterDesk.App;

public partial class App : Application
{
    private Mutex? _singleInstance;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // One instance only: all instances would share the same working session folder.
        _singleInstance = new Mutex(initiallyOwned: true, @"Local\PrinterDesk.SingleInstance", out var isFirst);
        if (!isFirst)
        {
            MessageBox.Show("PrinterDesk is already running.", "PrinterDesk", MessageBoxButton.OK, MessageBoxImage.Information);
            Shutdown();
            return;
        }

        Log.Cleanup();
        Log.Info($"Start {Environment.ProcessPath}");
        LegacyDataMigration.Run();

        DispatcherUnhandledException += (_, args) =>
        {
            Log.Error("Unhandled UI exception", args.Exception);
            MessageBox.Show($"Unexpected error:\n\n{args.Exception.Message}\n\nDetails are in the log ({Log.Folder}).",
                "PrinterDesk", MessageBoxButton.OK, MessageBoxImage.Error);
            args.Handled = true;
        };
        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            Log.Error("Unobserved task exception", args.Exception);
            args.SetObserved();
        };

        MainWindow = new MainWindow();
        MainWindow.Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _singleInstance?.Dispose();
        base.OnExit(e);
    }
}
