using System.IO;
using Microsoft.Win32;

namespace SevenDaysModLauncher.Services;

/// <summary>
/// Регистрация протокола nxm:// (кнопка Mod Manager Download на сайте открывает лаунчер).
/// Только HKCU — админ не нужен. Чужую регистрацию (напр. Vortex) не трогаем.
/// </summary>
public static class NexusProtocolService
{
    public const string Scheme = "nxm";

    public static string ExePath => Environment.ProcessPath
        ?? Path.Combine(AppContext.BaseDirectory, "7daysModLauncher.exe");

    /// <summary>true, если nxm:// уже ведет на наш exe.</summary>
    public static bool IsOurs()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Classes\nxm\shell\open\command");
            var cmd = key?.GetValue(null) as string;
            return !string.IsNullOrEmpty(cmd) &&
                   cmd.Contains("7daysModLauncher", StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception ex)
        {
            AppLogger.Warn($"nxm check failed: {ex.Message}");
            return false;
        }
    }

    public static bool IsTakenByOther()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Classes\nxm\shell\open\command");
            var cmd = key?.GetValue(null) as string;
            return !string.IsNullOrEmpty(cmd) && !IsOurs();
        }
        catch
        {
            return false;
        }
    }

    /// <summary>Регистрирует nxm:// на наш exe, если протокол свободен. Возвращает итог текстом для лога.</summary>
    public static string EnsureRegistered()
    {
        try
        {
            if (IsOurs())
                return "nxm:// already ours";
            if (IsTakenByOther())
                return "nxm:// owned by another app, skipped";

            var exe = ExePath;
            using (var scheme = Registry.CurrentUser.CreateSubKey(@"Software\Classes\nxm"))
            {
                scheme.SetValue(null, "URL:nxm Protocol");
                scheme.SetValue("URL Protocol", "");
            }
            using (var icon = Registry.CurrentUser.CreateSubKey(@"Software\Classes\nxm\DefaultIcon"))
                icon.SetValue(null, $"\"{exe}\",0");
            using (var cmd = Registry.CurrentUser.CreateSubKey(@"Software\Classes\nxm\shell\open\command"))
                cmd.SetValue(null, $"\"{exe}\" \"%1\"");

            AppLogger.Info($"Registered nxm:// -> {exe}");
            return "nxm:// registered";
        }
        catch (Exception ex)
        {
            AppLogger.Warn($"nxm register failed: {ex.Message}");
            return $"nxm:// register failed: {ex.Message}";
        }
    }

    public static string? FindNxmArg(string[] args)
    {
        foreach (var a in args)
        {
            var t = a.Trim().Trim('"');
            if (t.StartsWith("nxm://", StringComparison.OrdinalIgnoreCase))
                return t;
        }
        return null;
    }
}
