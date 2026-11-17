using System.IO;
using System.Windows;
using IndustrialDuckPanic.Core;
using Velopack;

namespace IndustrialDuckPanic;

public static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        // Must run first: handles install/update/uninstall hooks of the Velopack installer.
        VelopackApp.Build().Run();

        var app = new App();
        app.InitializeComponent();
        app.DispatcherUnhandledException += (_, e) =>
        {
            var path = WriteCrashLog(e.Exception);
            MessageBox.Show($"Something went wrong:\n{e.Exception.Message}\n\nDetails were saved to:\n{path}",
                AppConstants.AppName, MessageBoxButton.OK, MessageBoxImage.Error);
            e.Handled = true;
        };
        AppDomain.CurrentDomain.UnhandledException += (_, e) => WriteCrashLog(e.ExceptionObject as Exception);

        app.Run(new MainWindow());
    }

    private static string WriteCrashLog(Exception? ex)
    {
        var folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), AppConstants.AppId);
        var path = Path.Combine(folder, "crash.log");
        try
        {
            Directory.CreateDirectory(folder);
            File.AppendAllText(path, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {ex}\n\n");
        }
        catch { /* nothing else we can do */ }
        return path;
    }
}
