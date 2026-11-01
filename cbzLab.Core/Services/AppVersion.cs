using System.Reflection;

namespace cbzLab.Services;

/// <summary>
/// The running app's version, read from the app itself (the entry assembly), so cbzLab.csproj's
/// <c>Version</c> is the only place it's written. Shared code lives in libraries with no version of
/// their own, which is why this doesn't read its own assembly.
/// </summary>
public static class AppVersion
{
    public static readonly string Display = Read();

    //the informational version carries "+<commit>" when built from a git checkout
    private static string Read()
    {
        var assembly = Assembly.GetEntryAssembly();
        if (assembly is null)
            return "0.0.0";
        var informational = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        if (!string.IsNullOrEmpty(informational))
            return informational.Split('+')[0];
        return assembly.GetName().Version?.ToString(3) ?? "0.0.0";
    }
}
