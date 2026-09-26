namespace cbzLab.Services;

public enum LogSeverity { Info, Warning, Error }

/// <summary>Plain-text logger, one dated file per day under %appdata%\cbzLab\logs. Constructed first, before any other service.</summary>
public class LogService
{
    private readonly string _logDir;
    private readonly object _gate = new();

    public string LogDir => _logDir;

    //logDir is for tests only - the app always uses the real config directory
    public LogService(string? logDir = null)
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        _logDir = logDir ?? Path.Combine(appData, SettingsService.AppFolderName, "logs");
        try
        {
            Directory.CreateDirectory(_logDir);
            PruneOldLogs(DateTime.Now);
        }
        catch
        {
            //logging must never be the thing that crashes the app
        }
    }

    //one file per day otherwise accumulates forever
    public const int KeepLogDays = 30;

    //dated by the name rather than the file time, since copying a logs folder resets file times
    public void PruneOldLogs(DateTime now)
    {
        foreach (var file in Directory.GetFiles(_logDir, "cbzLab-*.log"))
        {
            var stamp = Path.GetFileNameWithoutExtension(file)["cbzLab-".Length..];
            if (DateTime.TryParseExact(stamp, "yyyyMMdd", null, System.Globalization.DateTimeStyles.None, out var day)
                && day < now.Date.AddDays(-KeepLogDays))
            {
                try { File.Delete(file); } catch { /* in use or read-only - try again next launch */ }
            }
        }
    }

    private string CurrentLogPath => Path.Combine(_logDir, $"cbzLab-{DateTime.Now:yyyyMMdd}.log");

    public void Info(string message) => Write(LogSeverity.Info, message);
    public void Warning(string message) => Write(LogSeverity.Warning, message);
    public void Error(string message) => Write(LogSeverity.Error, message);

    //convenience overload for the common "caught an exception" case
    public void Error(string message, Exception ex) => Write(LogSeverity.Error, $"{message}: {ex}");

    private void Write(LogSeverity level, string message)
    {
        try
        {
            var line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} [{level,-7}] {message}{Environment.NewLine}";
            //multiple services can log from different threads; one file, one lock
            lock (_gate)
            {
                File.AppendAllText(CurrentLogPath, line);
            }
        }
        catch
        {
            //a failing logger must never crash the app it's meant to help debug
        }
    }
}
