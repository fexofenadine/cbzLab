namespace cbzLab.Services;

public static class AppPaths
{
    //names a folder to keep settings in instead of the usual one: for trying a build without
    //touching the real settings, or for a portable copy
    public const string ConfigDirVariable = "CBZLAB_CONFIG_DIR";

    //where the bundled schema, themes and logo are read from. beside the executable on the
    //desktop; android has no such folder, so its head unpacks them somewhere and points this there
    public static string BundledAssetsDir { get; set; } = Path.Combine(AppContext.BaseDirectory, "Assets");

    //null uses %APPDATA%\cbzLab. set from the environment variable above, or by a test harness
    //before anything is constructed, so driving the real views never touches a real user's settings
    public static string? ConfigDir { get; set; } = ConfigDirFromEnvironment();

    private static string? ConfigDirFromEnvironment()
    {
        try
        {
            var value = Environment.GetEnvironmentVariable(ConfigDirVariable);
            return string.IsNullOrWhiteSpace(value) ? null : Path.GetFullPath(value.Trim());
        }
        catch
        {
            //an unusable path falls back to the usual folder
            return null;
        }
    }
}
