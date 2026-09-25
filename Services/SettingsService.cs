using System.IO;
using System.Text.Json;
using SevenDaysModLauncher.Models;

namespace SevenDaysModLauncher.Services;

public class SettingsService
{
    private readonly string _settingsPath;
    private readonly string _legacySettingsPath;

    public SettingsService()
    {
        var appDataDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "7daysModLauncher");
        Directory.CreateDirectory(appDataDir);
        _settingsPath = Path.Combine(appDataDir, "settings.json");
        _legacySettingsPath = Path.Combine(AppContext.BaseDirectory, "settings.json");

        // Однократная миграция со старого места (рядом с exe)
        try
        {
            if (!File.Exists(_settingsPath) && File.Exists(_legacySettingsPath))
                File.Copy(_legacySettingsPath, _settingsPath);
        }
        catch (Exception ex)
        {
            AppLogger.Warn($"Settings migration failed: {ex.Message}");
        }
    }

    public AppSettings Load()
    {
        if (!File.Exists(_settingsPath))
            return new AppSettings();

        try
        {
            var json = File.ReadAllText(_settingsPath);
            var settings = JsonSerializer.Deserialize<AppSettings>(json) ?? new AppSettings();
            settings.GameFolderPath = GamePathHelper.NormalizeGameFolderPath(settings.GameFolderPath);
            if (!string.IsNullOrEmpty(settings.GameFolderPath) && string.IsNullOrEmpty(settings.GameFolderPath))
                settings.GameFolderPath = null;
            return settings;
        }
        catch (Exception ex)
        {
            AppLogger.Warn($"Failed to load settings: {ex.Message}");
            return new AppSettings();
        }
    }

    public void Save(AppSettings settings)
    {
        settings.GameFolderPath = GamePathHelper.NormalizeGameFolderPath(settings.GameFolderPath);
        var json = JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(_settingsPath, json);
    }
}
