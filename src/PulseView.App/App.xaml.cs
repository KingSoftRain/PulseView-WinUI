using Microsoft.UI.Xaml;
using System;
using System.IO;

namespace PulseView.App;

public partial class App : Application
{
    private Window? _window;

    public App()
    {
        WriteStartupLog("App constructed.");
        UnhandledException += OnUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += OnAppDomainUnhandledException;

        InitializeComponent();
    }

    protected override void OnLaunched(Microsoft.UI.Xaml.LaunchActivatedEventArgs args)
    {
        WriteStartupLog("OnLaunched entered.");
        _window = new MainWindow();
        WriteStartupLog("MainWindow created.");
        _window.Activate();
        WriteStartupLog("MainWindow activated.");
    }

    private static void OnAppDomainUnhandledException(object sender, System.UnhandledExceptionEventArgs e)
    {
        WriteStartupLog($"AppDomain unhandled exception: {e.ExceptionObject}");
    }

    private static void OnUnhandledException(object sender, Microsoft.UI.Xaml.UnhandledExceptionEventArgs e)
    {
        WriteStartupLog($"XAML unhandled exception: {e.Exception}");
    }

    private static void WriteStartupLog(string message)
    {
        try {
            string logDirectory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "PulseView.WinUI",
                "logs");
            Directory.CreateDirectory(logDirectory);
            string logPath = Path.Combine(logDirectory, "startup.log");
            File.AppendAllText(logPath, $"{DateTimeOffset.Now:O} {message}{Environment.NewLine}");
        }
        catch {
            // Startup diagnostics must not prevent the application from launching.
        }
    }
}
