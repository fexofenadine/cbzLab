using cbzLab.Services;

namespace cbzLab.Tests;

//real services wired to a throwaway config directory, so tests exercise the actual
//settings/schema/validation code without ever touching a real user's %APPDATA%\cbzLab
public sealed class TestConfig : IDisposable
{
    public string Root { get; } = Path.Combine(Path.GetTempPath(), "cbzLabTests_cfg_" + Guid.NewGuid().ToString("N"));
    public LogService Log { get; }
    public SettingsService Settings { get; }
    public SchemaService Schema { get; }
    public ValidationService Validation { get; }

    public TestConfig(Action<string>? beforeStart = null)
    {
        Directory.CreateDirectory(ConfigDir);
        beforeStart?.Invoke(ConfigDir);
        Log = new LogService(Path.Combine(Root, "logs"));
        Settings = new SettingsService(Log, ConfigDir);
        Schema = new SchemaService(Settings, Log);
        Validation = new ValidationService(Schema);
    }

    public string ConfigDir => Path.Combine(Root, "config");

    public string TempFile(string name)
    {
        var dir = Path.Combine(Root, "files");
        Directory.CreateDirectory(dir);
        return Path.Combine(dir, name);
    }

    public void Dispose()
    {
        try { Directory.Delete(Root, recursive: true); } catch { /* best effort */ }
    }
}
