namespace HVAKR.Api;

/// <summary>
/// Plain file logger used across the plugin.
/// Writes to %LOCALAPPDATA%\HVAKR\plugin.log (falls back to %TEMP% if LOCALAPPDATA is unavailable).
/// </summary>
public static class Logger
{
    private static readonly string LogFilePath = ResolveLogFilePath();
    private static readonly object LockObject = new();

    public static void LogMessage(string msg) => Write("INFO ", msg);

    public static void LogError(string msg) => Write("ERROR", msg);

    public static void LogError(string msg, Exception ex) => Write("ERROR", $"{msg} — {ex.GetType().Name}: {ex.Message}");

    private static void Write(string level, string msg)
    {
        var line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} - {level}: {msg}{Environment.NewLine}";
        try
        {
            lock (LockObject)
            {
                File.AppendAllText(LogFilePath, line);
            }
        }
        catch
        {
            // Logging must never throw.
        }
    }

    private static string ResolveLogFilePath()
    {
        var baseDir = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (string.IsNullOrEmpty(baseDir))
        {
            baseDir = Path.GetTempPath();
        }
        var dir = Path.Combine(baseDir, "HVAKR");
        Directory.CreateDirectory(dir);
        return Path.Combine(dir, "plugin.log");
    }
}
