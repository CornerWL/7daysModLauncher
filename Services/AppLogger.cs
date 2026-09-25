using System.IO;

namespace SevenDaysModLauncher.Services;

/// <summary>
/// Минимальный файловый логгер без внешних зависимостей.
/// Путь: %LocalAppData%/7daysModLauncher/Logs/launcher.log
/// </summary>
public static class AppLogger
{
    private static readonly object _lock = new();
    private static string? _logPath;

    public static string LogPath
    {
        get
        {
            if (_logPath != null)
                return _logPath;

            var dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "7daysModLauncher", "Logs");
            Directory.CreateDirectory(dir);
            _logPath = Path.Combine(dir, "launcher.log");
            return _logPath;
        }
    }

    public static void Info(string message) => Write("INFO", message, null);

    public static void Warn(string message) => Write("WARN", message, null);

    public static void Error(string message, Exception? ex = null) => Write("ERROR", message, ex);

    private static void Write(string level, string message, Exception? ex)
    {
        try
        {
            var line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} [{level}] {message}";
            if (ex != null)
                line += $"{Environment.NewLine}{ex}";

            lock (_lock)
            {
                File.AppendAllText(LogPath, line + Environment.NewLine);
            }
        }
        catch
        {
            // Логгер никогда не должен ронять приложение.
        }
    }
}
