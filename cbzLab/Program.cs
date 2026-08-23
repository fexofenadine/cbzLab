using Avalonia;

namespace cbzLab;

internal static class Program
{
    //kept separate from BuildAvaloniaApp so designer tooling can call it without running the app
    [STAThread]
    public static void Main(string[] args) =>
        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);

    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .LogToTrace();
}
