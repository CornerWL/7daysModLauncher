using System.IO;
using System.Text.RegularExpressions;

namespace SevenDaysModLauncher.Services;

/// <summary>Один бэкап: папка вида &lt;mod&gt;_yyyyMMdd_HHmmss[_n] в Mods_Backup.</summary>
public sealed class BackupItem
{
    public string ModName { get; init; } = string.Empty;
    public string BackupFolder { get; init; } = string.Empty;
    public string FullPath { get; init; } = string.Empty;
    public DateTime Created { get; init; }
    public string CreatedText { get; init; } = string.Empty;
    public string SizeText { get; init; } = string.Empty;
}

/// <summary>Корзина: сканирование, восстановление, удаление навсегда.</summary>
public sealed class BackupService
{
    private static readonly Regex _nameRx = new(
        @"^(?<name>.+)_(?<date>\d{8}_\d{6})(_(?<n>\d+))?$",
        RegexOptions.Compiled);

    public string GetBackupPath(string gameFolder) => Path.Combine(gameFolder, "Mods_Backup");

    public List<BackupItem> ScanBackups(string gameFolder, string? filter = null)
    {
        var result = new List<BackupItem>();
        var root = Path.Combine(gameFolder, "Mods_Backup");
        if (!Directory.Exists(root))
            return result;

        foreach (var dir in Directory.GetDirectories(root))
        {
            try
            {
                var folder = Path.GetFileName(dir);
                string modName = folder;
                DateTime created;
                var m = _nameRx.Match(folder);
                if (m.Success)
                {
                    modName = m.Groups["name"].Value;
                    if (!DateTime.TryParseExact(m.Groups["date"].Value, "yyyyMMdd_HHmmss",
                            System.Globalization.CultureInfo.InvariantCulture,
                            System.Globalization.DateTimeStyles.None, out created))
                        created = Directory.GetCreationTime(dir);
                }
                else
                {
                    created = Directory.GetCreationTime(dir);
                }

                if (!string.IsNullOrWhiteSpace(filter) &&
                    !modName.Contains(filter, StringComparison.OrdinalIgnoreCase))
                    continue;

                result.Add(new BackupItem
                {
                    ModName = modName,
                    BackupFolder = folder,
                    FullPath = dir,
                    Created = created,
                    CreatedText = created.ToString("dd.MM.yyyy HH:mm"),
                    SizeText = FormatSize(DirSize(dir)),
                });
            }
            catch (Exception ex)
            {
                AppLogger.Warn($"Scan backup '{dir}' failed: {ex.Message}");
            }
        }

        return result.OrderByDescending(b => b.Created).ToList();
    }

    /// <summary>Восстановить бэкап в Mods. Занятую папку — в новый бэкап.</summary>
    public void Restore(string gameFolder, BackupItem backup)
    {
        var modsPath = GamePathHelper.GetModsPath(gameFolder);
        var disabledPath = GamePathHelper.GetDisabledModsPath(gameFolder);
        Directory.CreateDirectory(modsPath);

        if (!Directory.Exists(backup.FullPath))
            throw new DirectoryNotFoundException($"Backup not found: {backup.FullPath}");

        var target = Path.Combine(modsPath, backup.ModName);
        var occupant = Directory.Exists(target) ? target
            : Directory.Exists(Path.Combine(disabledPath, backup.ModName))
                ? Path.Combine(disabledPath, backup.ModName) : null;

        if (occupant != null)
        {
            // Текущую версию — в бэкап, затем восстанавливаем
            var stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            var rescue = Path.Combine(gameFolder, "Mods_Backup", $"{backup.ModName}_{stamp}");
            int i = 1;
            while (Directory.Exists(rescue))
                rescue = Path.Combine(gameFolder, "Mods_Backup", $"{backup.ModName}_{stamp}_{i++}");
            Directory.Move(occupant, rescue);
            AppLogger.Info($"Restore: current version backed up to '{rescue}'");
        }

        Directory.Move(backup.FullPath, target);
        AppLogger.Info($"Restored backup '{backup.BackupFolder}' to Mods");
    }

    public void DeleteForever(BackupItem backup)
    {
        if (Directory.Exists(backup.FullPath))
        {
            Directory.Delete(backup.FullPath, true);
            AppLogger.Info($"Deleted backup forever '{backup.BackupFolder}'");
        }
    }

    private static long DirSize(string dir)
    {
        try
        {
            long size = 0;
            foreach (var f in Directory.GetFiles(dir, "*", SearchOption.AllDirectories))
            {
                try { size += new FileInfo(f).Length; } catch { }
            }
            return size;
        }
        catch
        {
            return 0;
        }
    }

    private static string FormatSize(long bytes)
    {
        if (bytes <= 0) return "—";
        if (bytes < 1024) return $"{bytes} B";
        if (bytes < 1024 * 1024) return $"{bytes / 1024.0:F0} KB";
        if (bytes < 1024L * 1024 * 1024) return $"{bytes / (1024.0 * 1024):F1} MB";
        return $"{bytes / (1024.0 * 1024 * 1024):F2} GB";
    }
}
