using cbzLab.Services;
using Microsoft.UI.Xaml;

namespace cbzLab;

/// <summary>Entry point — builds services, then the main window.</summary>
public partial class App : Application
{
    //simple service locator — a full di container is overkill for a single window
    public static LogService Log { get; private set; } = null!;
    public static SettingsService Settings { get; private set; } = null!;
    public static SchemaService Schema { get; private set; } = null!;
    public static ThemeService Theme { get; private set; } = null!;
    public static ArchiveService Archive { get; private set; } = null!;
    public static ValidationService Validation { get; private set; } = null!;
    public static RecentValuesService RecentValues { get; private set; } = null!;
    public static ComicVineCacheService ComicVineCache { get; private set; } = null!;
    public static ComicVineService ComicVine { get; private set; } = null!;

    public static MainWindow? Window { get; private set; }

    public App()
    {
        InitializeComponent();
        UnhandledException += OnUnhandledException;
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        Log = new LogService();
        Log.Info($"cbzLab {typeof(App).Assembly.GetName().Version} starting");

        Settings = new SettingsService(Log);
        Schema = new SchemaService(Settings, Log);
        Theme = new ThemeService(Settings, Log);
        Archive = new ArchiveService(Settings, Schema, Log);
        Validation = new ValidationService(Schema);
        RecentValues = new RecentValuesService(Settings, Log);
        ComicVineCache = new ComicVineCacheService(Settings, Log);
        ComicVine = new ComicVineService(Settings, ComicVineCache, Log);

        Theme.RegisterResources();
        Theme.Apply(Settings.Settings.Theme);

        Window = new MainWindow();

        //queued rather than opened immediately so dialogs have a valid XamlRoot
        var cliPaths = Environment.GetCommandLineArgs()
            .Skip(1)
            .Where(File.Exists)
            .ToList();
        if (cliPaths.Count > 0)
            Window.QueueStartupPaths(cliPaths);

        Window.Activate();
    }

    private void OnUnhandledException(object sender, Microsoft.UI.Xaml.UnhandledExceptionEventArgs e)
    {
        try { Log?.Error("Unhandled exception", e.Exception); } catch { /*never let logging mask the real crash*/ }
    }
}
