using cbzLab.Services;
using Microsoft.UI.Xaml;

namespace cbzLab;

/// <summary>
/// Application entry point. Constructs the service layer, registers theme
/// resources before any xaml is realised, then creates the main window and
/// hands it any file paths passed on the command line.
/// </summary>
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
        //catches anything that slips past a local try/catch so it at least
        //leaves a trace instead of just vanishing into a crash dialog
        UnhandledException += OnUnhandledException;
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        //logging first: every other service can then log its own setup problems
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

        //brushes must exist in application resources before MainWindow.xaml parses
        Theme.RegisterResources();
        Theme.Apply(Settings.Settings.Theme);

        Window = new MainWindow();

        //files passed as command-line arguments are queued and opened once the
        //window content has loaded, so dialogs have a valid XamlRoot to attach to
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
