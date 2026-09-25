using System.IO;

namespace SevenDaysModLauncher.Services;

/// <summary>
/// Общая нормализация и валидация пути к игре.
/// Убирает дублирование NormalizeGameFolderPath из MainViewModel/SettingsService.
/// </summary>
public static class GamePathHelper
{
    public const string ModsFolderName = "Mods";
    public const string DisabledModsFolderName = "Mods_Disabled";
    public const string GameExeName = "7DaysToDie.exe";

    public static string NormalizeGameFolderPath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return string.Empty;

        var trimmed = path.Trim().TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var folderName = Path.GetFileName(trimmed);
        if (folderName.Equals(ModsFolderName, StringComparison.OrdinalIgnoreCase) ||
            folderName.Equals(DisabledModsFolderName, StringComparison.OrdinalIgnoreCase))
        {
            return Path.GetDirectoryName(trimmed) ?? path;
        }
        return trimmed;
    }

    public static bool IsValidGameFolder(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path))
            return false;
        return File.Exists(Path.Combine(path, GameExeName));
    }

    public static string GetModsPath(string gameFolder) => Path.Combine(gameFolder, ModsFolderName);

    public static string GetDisabledModsPath(string gameFolder) => Path.Combine(gameFolder, DisabledModsFolderName);

    public static void EnsureModDirectories(string gameFolder)
    {
        Directory.CreateDirectory(GetModsPath(gameFolder));
        Directory.CreateDirectory(GetDisabledModsPath(gameFolder));
    }
}
