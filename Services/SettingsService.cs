using System.IO;
using System.Text.Json;
using SevenDaysModLauncher.Models;

namespace SevenDaysModLauncher.Services;

public class SettingsService
{
    private readonly string _settingsPath;

    public SettingsService()
    {
        _settingsPath = Path.Combine(AppContext.BaseDirectory, "settings.json");
    }

    public AppSettings Load()
    {
        if (!File.Exists(_settingsPath))
            return new AppSettings();

        try
        {
            var json = File.ReadAllText(_settingsPath);
            var settings = JsonSerializer.Deserialize<AppSettings>(json) ?? new AppSettings();
            // Ensure stored path points to game root, not to Mods or Mods_Disabled subfolders
            if (!string.IsNullOrEmpty(settings.GameFolderPath))
            {
                var trimmed = settings.GameFolderPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                var folderName = Path.GetFileName(trimmed);
                if (folderName.Equals("Mods", System.StringComparison.OrdinalIgnoreCase) ||
                    folderName.Equals("Mods_Disabled", System.StringComparison.OrdinalIgnoreCase))
                {
                    settings.GameFolderPath = Path.GetDirectoryName(trimmed) ?? settings.GameFolderPath;
                }
            }
            return settings;
        }
        catch
        {
            return new AppSettings();
        }
    }

    public void Save(AppSettings settings)
    {
        // Guard against saving a Mods subfolder as the game folder path
        if (!string.IsNullOrEmpty(settings.GameFolderPath))
        {
            var trimmed = settings.GameFolderPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var folderName = Path.GetFileName(trimmed);
            if (folderName.Equals("Mods", System.StringComparison.OrdinalIgnoreCase) ||
                folderName.Equals("Mods_Disabled", System.StringComparison.OrdinalIgnoreCase))
            {
                settings.GameFolderPath = Path.GetDirectoryName(trimmed) ?? settings.GameFolderPath;
            }
        }
        var json = JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(_settingsPath, json);
    }
}
