using System.IO;
using System.Text.Json;

namespace SevenDaysModLauncher.Services;

/// <summary>
/// Привязка локальных папок модов к Nexus mod id: { "0_TFP_Harmony": 10784 }.
/// Заполняется при установке через Nexus/NXM, лежит в %LocalAppData%.
/// </summary>
public sealed class NexusModMapService
{
    private static string MapPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "7daysModLauncher", "nexus_mods.json");

    public Dictionary<string, int> Load()
    {
        try
        {
            if (!File.Exists(MapPath))
                return new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            var json = File.ReadAllText(MapPath);
            var raw = JsonSerializer.Deserialize<Dictionary<string, int>>(json);
            return raw != null
                ? new Dictionary<string, int>(raw, StringComparer.OrdinalIgnoreCase)
                : new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        }
        catch (Exception ex)
        {
            AppLogger.Warn($"Nexus map load failed: {ex.Message}");
            return new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        }
    }

    public void Save(Dictionary<string, int> map)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(MapPath)!);
            File.WriteAllText(MapPath, JsonSerializer.Serialize(map, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception ex)
        {
            AppLogger.Warn($"Nexus map save failed: {ex.Message}");
        }
    }

    public void Set(string folderName, int modId)
    {
        if (string.IsNullOrWhiteSpace(folderName))
            return;
        var map = Load();
        map[folderName.Trim()] = modId;
        Save(map);
    }

    public void SetMany(IEnumerable<string> folderNames, int modId)
    {
        var map = Load();
        bool changed = false;
        foreach (var f in folderNames)
        {
            if (string.IsNullOrWhiteSpace(f))
                continue;
            map[f.Trim()] = modId;
            changed = true;
        }
        if (changed)
            Save(map);
    }

    public void Remove(string folderName)
    {
        var map = Load();
        if (map.Remove(folderName))
            Save(map);
    }
}
