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
        _profilesDirectory = Path.Combine(AppContext.BaseDirectory, "Profiles");
        Directory.CreateDirectory(_profilesDirectory);
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
            catch
            {
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
        catch
        {
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