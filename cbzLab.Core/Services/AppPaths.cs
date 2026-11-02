namespace cbzLab.Services;

public static class AppPaths
{
    //where the bundled schema, themes and logo are read from. beside the executable on the
    //desktop; android has no such folder, so its head unpacks them somewhere and points this there
    public static string BundledAssetsDir { get; set; } = Path.Combine(AppContext.BaseDirectory, "Assets");
}
