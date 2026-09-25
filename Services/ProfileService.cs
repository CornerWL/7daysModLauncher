using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using SevenDaysModLauncher.Models;

namespace SevenDaysModLauncher.Services;

public class ProfileService
{
    private readonly string _profilesDirectory;

    public ProfileService()
    {
        _profilesDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "7daysModLauncher", "Profiles");
        Directory.CreateDirectory(_profilesDirectory);

        // Однократная миграция профилей рядом с exe
        try
        {
            var legacyDir = Path.Combine(AppContext.BaseDirectory, "Profiles");
            if (Directory.Exists(legacyDir))
            {
                foreach (var file in Directory.GetFiles(legacyDir, "*.json"))
                {
                    var dest = Path.Combine(_profilesDirectory, Path.GetFileName(file));
                    if (!File.Exists(dest))
                        File.Copy(file, dest);
                }
            }
        }
        catch (Exception ex)
        {
            AppLogger.Warn($"Profile migration failed: {ex.Message}");
        }
    }

    public List<Profile> GetAllProfiles()
    {
        var profiles = new List<Profile>();
        if (!Directory.Exists(_profilesDirectory))
            return profiles;

        foreach (var file in Directory.GetFiles(_profilesDirectory, "*.json"))
        {
            try
            {
                var json = File.ReadAllText(file);
                var profile = JsonSerializer.Deserialize<Profile>(json);
                if (profile != null)
                    profiles.Add(profile);
            }
            catch (Exception ex)
            {
                AppLogger.Warn($"Skipping broken profile '{file}': {ex.Message}");
            }
        }
        return profiles.OrderBy(p => p.Name).ToList();
    }

    public Profile? LoadProfile(string name)
    {
        var filePath = GetProfilePath(name);
        if (!File.Exists(filePath))
            return null;

        try
        {
            var json = File.ReadAllText(filePath);
            return JsonSerializer.Deserialize<Profile>(json);
        }
        catch (Exception ex)
        {
            AppLogger.Warn($"Failed to load profile '{name}': {ex.Message}");
            return null;
        }
    }

    public void SaveProfile(Profile profile)
    {
        var filePath = GetProfilePath(profile.Name);
        var json = JsonSerializer.Serialize(profile, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(filePath, json);
    }

    public void DeleteProfile(string name)
    {
        var filePath = GetProfilePath(name);
        if (File.Exists(filePath))
            File.Delete(filePath);
    }

    private string GetProfilePath(string name)
    {
        var safeName = string.Join("_", name.Split(Path.GetInvalidFileNameChars()));
        return Path.Combine(_profilesDirectory, $"{safeName}.json");
    }

    public Profile CreateFromCurrentState(List<ModItem> mods)
    {
        return new Profile
        {
            Mods = mods.Select(m => new Profile.ModState
            {
                Name = m.Name,
                IsEnabled = m.IsEnabled
            }).ToList()
        };
    }
}