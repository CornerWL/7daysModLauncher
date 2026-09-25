using System.Diagnostics;
using System.IO;

namespace SevenDaysModLauncher.Services;

/// <summary>
/// Запуск игры. Раньше README обещал "Launch the game", но кнопки не было.
/// </summary>
public interface IGameLauncherService
{
    string? FindExe(string gameFolder);
    bool TryLaunch(string gameFolder, out string error);
}

public sealed class GameLauncherService : IGameLauncherService
{
    public string? FindExe(string gameFolder)
    {
        if (string.IsNullOrWhiteSpace(gameFolder) || !Directory.Exists(gameFolder))
            return null;

        // Обычный и EAC-вариант. Предпочитаем обычный exe.
        var candidates = new[]
        {
            Path.Combine(gameFolder, "7DaysToDie.exe"),
            Path.Combine(gameFolder, "7DaysToDie_EAC.exe"),
        };

        foreach (var c in candidates)
        {
            if (File.Exists(c))
                return c;
        }

        return null;
    }

    public bool TryLaunch(string gameFolder, out string error)
    {
        error = string.Empty;
        try
        {
            var exe = FindExe(gameFolder);
            if (exe == null)
            {
                error = $"Не найден {GamePathHelper.GameExeName} в папке:\n{gameFolder}";
                return false;
            }

            var psi = new ProcessStartInfo
            {
                FileName = exe,
                WorkingDirectory = gameFolder,
                UseShellExecute = true,
            };
            Process.Start(psi);
            AppLogger.Info($"Game launched: {exe}");
            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            AppLogger.Error("Failed to launch game", ex);
            return false;
        }
    }
}
