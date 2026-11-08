namespace cbzLab.Services;

public static class AppPaths
{
    //where the bundled schema, themes and logo are read from. beside the executable on the
    //desktop; android has no such folder, so its head unpacks them somewhere and points this there
    public static string BundledAssetsDir { get; set; } = Path.Combine(AppContext.BaseDirectory, "Assets");

    //null in the app, which uses %APPDATA%\cbzLab; a test harness points it at a scratch folder before
    //anything is constructed, so driving the real views never touches a real user's settings
    public static string? ConfigDir { get; set; }
}
