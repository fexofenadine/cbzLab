namespace cbzLab.Services;

public enum LogSeverity { Info, Warning, Error }

/// <summary>Plain-text logger, one dated file per day under %appdata%\cbzLab\logs. Constructed first, before any other service.</summary>
public class LogService
{
    private readonly string _logDir;
    private readonly object _gate = new();

    public string LogDir => _logDir;

    public LogService()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        _logDir = Path.Combine(appData, SettingsService.AppFolderName, "logs");
        try
        {
            Directory.CreateDirectory(_logDir);
        }
        catch
        {
            //logging must never be the thing that crashes the app
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
