using Avalonia;

namespace BandwidthTester.AvaloniaGui;

internal static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        System.Diagnostics.Trace.Listeners.Add(new System.Diagnostics.ConsoleTraceListener());
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            Console.WriteLine($"[UNHANDLED] {e.ExceptionObject}");
        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            Console.WriteLine($"[UNOBSERVED TASK] {e.Exception}");
            e.SetObserved();
        };
        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<App>()
        .UsePlatformDetect()
        .WithInterFont()
        .LogToTrace();
}
