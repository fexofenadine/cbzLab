using Avalonia;

namespace cbzLab.Avalonia;

internal static class Program
{
    //avaloniaui insists this stay separate from BuildAvaloniaApp so designer
    //tooling can call BuildAvaloniaApp without also running the app
    [STAThread]
    public static void Main(string[] args) =>
        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);

    //WithInterFont() needs the separate Avalonia.Fonts.Inter package - skipped
    //for this skeleton, default platform fonts are fine to prove the toolchain
    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .LogToTrace();
}
