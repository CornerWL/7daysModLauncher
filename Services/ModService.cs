using System.IO;
using System.IO.Compression;
using SevenDaysModLauncher.Models;

namespace SevenDaysModLauncher.Services;

public class ModService
{
    public string GetModsPath(string gameFolder) => Path.Combine(gameFolder, "Mods");
    public string GetDisabledModsPath(string gameFolder) => Path.Combine(gameFolder, "Mods_Disabled");

    public List<ModItem> ScanMods(string gameFolder)
    {
        var mods = new List<ModItem>();

        var modsPath = GetModsPath(gameFolder);
        if (Directory.Exists(modsPath))
        {
            foreach (var dir in Directory.GetDirectories(modsPath))
            {
                mods.Add(CreateModItem(dir, true));
            }
        }

        var disabledPath = GetDisabledModsPath(gameFolder);
        if (Directory.Exists(disabledPath))
        {
            foreach (var dir in Directory.GetDirectories(disabledPath))
            {
                mods.Add(CreateModItem(dir, false));
            }
        }

        return mods.OrderBy(m => m.Name).ToList();
    }

    private static ModItem CreateModItem(string folderPath, bool isEnabled)
    {
        var folderName = Path.GetFileName(folderPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        var (version, author, description, displayName, website) = TryReadModInfo(folderPath);

        return new ModItem
        {
            Name = string.IsNullOrWhiteSpace(displayName) ? folderName : displayName,
            FolderName = folderName,
            IsEnabled = isEnabled,
            Version = version,
            Author = author,
            Description = description,
            Website = website,
            FolderPath = folderPath
        };
    }

    /// <summary>
    /// Читает ModInfo.xml в обоих распространенных форматах:
    /// 1) &lt;ModInfo Name="..." Author="..." Version="..." Website="..." ...&gt; (&lt;Description&gt;...&gt;)
    /// 2) &lt;ModInfo&gt;&lt;Name&gt;...&lt;Version value="..."/&gt;... (формат 7DTD v2)
    /// Возвращает (version, author, description, displayName, website).
    /// </summary>
    private static (string? Version, string? Author, string? Description, string? DisplayName, string? Website) TryReadModInfo(string modFolder)
    {
        try
        {
            var modInfoPath = Path.Combine(modFolder, "ModInfo.xml");
            if (!File.Exists(modInfoPath))
                return (null, null, null, null, null);

            var doc = System.Xml.Linq.XDocument.Load(modInfoPath);
            var root = doc.Root;
            if (root == null)
                return (null, null, null, null, null);

            // Формат 1: атрибуты
            string? version = root.Attribute("Version")?.Value
                ?? root.Element("Version")?.Value
                ?? root.Element("Version")?.Attribute("value")?.Value;
            string? author = root.Attribute("Author")?.Value ?? root.Element("Author")?.Value;
            string? description = root.Attribute("Description")?.Value ?? root.Element("Description")?.Value;
            string? displayName = root.Attribute("Name")?.Value ?? root.Element("Name")?.Value;
            string? website = root.Attribute("Website")?.Value ?? root.Element("Website")?.Value;

            // Формат 2: <ModInfo><Version value="1.0" /> ...
            if (string.IsNullOrWhiteSpace(version))
            {
                var vEl = root.Descendants("Version").FirstOrDefault();
                version = vEl?.Attribute("value")?.Value ?? vEl?.Value;
            }
            if (string.IsNullOrWhiteSpace(website))
                website = root.Descendants("Website").FirstOrDefault()?.Value;

            return (
                string.IsNullOrWhiteSpace(version) ? null : version.Trim(),
                string.IsNullOrWhiteSpace(author) ? null : author.Trim(),
                string.IsNullOrWhiteSpace(description) ? null : description.Trim(),
                string.IsNullOrWhiteSpace(displayName) ? null : displayName.Trim(),
                string.IsNullOrWhiteSpace(website) ? null : website.Trim());
        }
        catch (Exception ex)
        {
            AppLogger.Warn($"Failed to read ModInfo.xml in '{modFolder}': {ex.Message}");
        }

        return (null, null, null, null, null);
    }

    private static string? TryReadVersion(string modFolder) => TryReadModInfo(modFolder).Version;

    public string GetBackupPath(string gameFolder) => Path.Combine(gameFolder, "Mods_Backup");

    /// <summary>
    /// Перемещает существующую папку мода в Mods_Backup/&lt;name&gt;_yyyyMMdd_HHmmss.
    /// Возвращает путь бэкапа или null, если бэкапить нечего.
    /// </summary>
    public string? BackupExistingMod(string gameFolder, string modName)
    {
        var backupRoot = GetBackupPath(gameFolder);
        Directory.CreateDirectory(backupRoot);

        string? existing = null;
        var inMods = Path.Combine(GetModsPath(gameFolder), modName);
        var inDisabled = Path.Combine(GetDisabledModsPath(gameFolder), modName);
        if (Directory.Exists(inMods))
            existing = inMods;
        else if (Directory.Exists(inDisabled))
            existing = inDisabled;

        if (existing == null)
            return null;

        var stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
        var backupPath = Path.Combine(backupRoot, $"{modName}_{stamp}");
        int i = 1;
        while (Directory.Exists(backupPath))
            backupPath = Path.Combine(backupRoot, $"{modName}_{stamp}_{i++}");

        Directory.Move(existing, backupPath);
        AppLogger.Info($"Backed up mod '{modName}' to '{backupPath}'");
        return backupPath;
    }

    public bool ModExists(string gameFolder, string modName)
        => Directory.Exists(Path.Combine(GetModsPath(gameFolder), modName))
        || Directory.Exists(Path.Combine(GetDisabledModsPath(gameFolder), modName));

    public Task InstallModAsync(string gameFolder, string zipPath, IProgress<double>? progress = null)
        => InstallModAsync(gameFolder, zipPath, progress, CancellationToken.None);

    public async Task InstallModAsync(string gameFolder, string zipPath, IProgress<double>? progress, CancellationToken cancellationToken)
    {
        // Создаем папки, если их нет
        var modsPath = GetModsPath(gameFolder);
        Directory.CreateDirectory(modsPath);
        var disabledPath = GetDisabledModsPath(gameFolder);
        Directory.CreateDirectory(disabledPath);

        if (!File.Exists(zipPath))
            throw new FileNotFoundException($"Archive not found: {zipPath}");

        var tempFolder = Path.Combine(Path.GetTempPath(), "7dtd_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempFolder);

        try
        {
            progress?.Report(0.02);
            await Task.Run(() => ZipFile.ExtractToDirectory(zipPath, tempFolder, overwriteFiles: true), cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            progress?.Report(0.25);

            var modFolders = FindModFolders(tempFolder, zipPath);
            if (modFolders.Count == 0)
                throw new InvalidOperationException("Could not find the mod folder in the archive.");

            // Честный прогресс: считаем файлы заранее
            var allFiles = new List<(string Source, string ModFolder)>();
            foreach (var mf in modFolders)
                foreach (var f in Directory.GetFiles(mf, "*", SearchOption.AllDirectories))
                    allFiles.Add((f, mf));
            int totalFiles = Math.Max(1, allFiles.Count);
            int copiedFiles = 0;

            // Бэкапим существующие моды с теми же именами вместо молчаливого удаления
            var plannedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var modFolder in modFolders)
            {
                var modName = ResolveModName(modFolder, tempFolder, zipPath);
                plannedNames.Add(modName);
            }
            foreach (var modName in plannedNames)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (ModExists(gameFolder, modName))
                    BackupExistingMod(gameFolder, modName);
            }

            foreach (var modFolder in modFolders)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var modName = ResolveModName(modFolder, tempFolder, zipPath);
                var targetPath = Path.Combine(modsPath, modName);
                Directory.CreateDirectory(targetPath);

                foreach (var (src, root) in allFiles.Where(x => x.ModFolder == modFolder))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var rel = Path.GetRelativePath(root, src);
                    var dest = Path.Combine(targetPath, rel);
                    Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
                    File.Copy(src, dest, true);
                    copiedFiles++;
                    progress?.Report(0.25 + 0.75 * copiedFiles / totalFiles);
                }
                AppLogger.Info($"Installed mod '{modName}' from '{zipPath}'");
            }
            progress?.Report(1.0);
        }
        finally
        {
            try
            {
                if (Directory.Exists(tempFolder))
                    Directory.Delete(tempFolder, true);
            }
            catch (Exception ex)
            {
                AppLogger.Warn($"Failed to cleanup temp '{tempFolder}': {ex.Message}");
            }
        }
    }

    private static string ResolveModName(string modFolder, string tempFolder, string zipPath)
    {
        var modName = Path.GetFileName(modFolder);
        if (modFolder == tempFolder)
            modName = Path.GetFileNameWithoutExtension(zipPath);

        modName = string.Join("_", modName.Split(Path.GetInvalidFileNameChars()));
        if (string.IsNullOrWhiteSpace(modName))
            modName = Path.GetFileNameWithoutExtension(zipPath);
        return modName;
    }

    /// <summary>
    /// Находит все папки модов в распакованном архиве.
    /// Поддерживает архивы с несколькими модами (каждый с ModInfo.xml).
    /// </summary>
    private static List<string> FindModFolders(string extractedPath, string zipPath)
    {
        // Если в корне есть ModInfo.xml — это и есть папка мода
        if (File.Exists(Path.Combine(extractedPath, "ModInfo.xml")))
            return new List<string> { extractedPath };

        var result = new List<string>();
        foreach (var dir in Directory.GetDirectories(extractedPath))
        {
            if (File.Exists(Path.Combine(dir, "ModInfo.xml")))
            {
                result.Add(dir);
            }
            else
            {
                // Вложенность вида Collection/ModName/ModInfo.xml
                foreach (var sub in Directory.GetDirectories(dir))
                {
                    if (File.Exists(Path.Combine(sub, "ModInfo.xml")))
                        result.Add(sub);
                }
            }
        }

        if (result.Count > 0)
            return result;

        // Если ModInfo.xml не найден, берём первую подпапку (legacy-поведение)
        var subDirs = Directory.GetDirectories(extractedPath);
        if (subDirs.Length == 1)
            return new List<string> { subDirs[0] };

        // Иначе считаем корнем мод (по имени архива)
        return new List<string> { extractedPath };
    }

    private static string? FindModFolder(string extractedPath)
    {
        return FindModFolders(extractedPath, string.Empty).FirstOrDefault();
    }

    private static void CopyDirectory(string sourceDir, string targetDir)
    {
        Directory.CreateDirectory(targetDir);

        foreach (var file in Directory.GetFiles(sourceDir))
        {
            File.Copy(file, Path.Combine(targetDir, Path.GetFileName(file)), true);
        }

        foreach (var dir in Directory.GetDirectories(sourceDir))
        {
            CopyDirectory(dir, Path.Combine(targetDir, Path.GetFileName(dir)));
        }
    }

    public void ToggleMod(string gameFolder, ModItem mod)
    {
        var modsPath = GetModsPath(gameFolder);
        var disabledPath = GetDisabledModsPath(gameFolder);

        Directory.CreateDirectory(modsPath);
        Directory.CreateDirectory(disabledPath);

        var modName = Path.GetFileName(mod.FolderPath);
        var targetPath = Path.Combine(modsPath, modName);
        var disabledTarget = Path.Combine(disabledPath, modName);

        // Determine actual location based on current folder path
        // Determine current location by parent folder name to avoid false positives (e.g., "Mods_Disabled" starts with "Mods")
        var parentPath = Path.GetDirectoryName(mod.FolderPath);
        var parentFolderName = !string.IsNullOrEmpty(parentPath) ? Path.GetFileName(parentPath) : string.Empty;

        if (parentFolderName.Equals("Mods", StringComparison.OrdinalIgnoreCase))
        {
            // Move to Mods_Disabled
            if (Directory.Exists(disabledTarget))
                Directory.Delete(disabledTarget, true);
            Directory.Move(mod.FolderPath, disabledTarget);
            mod.FolderPath = disabledTarget;
        }
        else if (parentFolderName.Equals("Mods_Disabled", StringComparison.OrdinalIgnoreCase))
        {
            // Move back to Mods
            if (Directory.Exists(targetPath))
                Directory.Delete(targetPath, true);
            Directory.Move(mod.FolderPath, targetPath);
            mod.FolderPath = targetPath;
        }
    }

    public void DeleteMod(ModItem mod)
    {
        DeleteModWithBackup(mod, null);
    }

    /// <summary>
    /// Удаление с бэкапом: папка переезжает в Mods_Backup рядом с игрой.
    /// Возвращает путь бэкапа (для показа пользователю).
    /// </summary>
    public string? DeleteModWithBackup(ModItem mod, string? gameFolder)
    {
        if (string.IsNullOrEmpty(mod.FolderPath) || !Directory.Exists(mod.FolderPath))
            return null;

        try
        {
            string backupRoot;
            if (!string.IsNullOrEmpty(gameFolder))
                backupRoot = GetBackupPath(gameFolder);
            else
            {
                // Выводим корень бэкапа из родителя: <game>/Mods[/_Disabled] -> <game>/Mods_Backup
                var parent = Path.GetDirectoryName(mod.FolderPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
                var gameRoot = parent != null ? Path.GetDirectoryName(parent) : null;
                backupRoot = string.IsNullOrEmpty(gameRoot)
                    ? Path.Combine(Path.GetTempPath(), "7dtd_Mods_Backup")
                    : Path.Combine(gameRoot, "Mods_Backup");
            }
            Directory.CreateDirectory(backupRoot);

            var modName = Path.GetFileName(mod.FolderPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
            var stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            var backupPath = Path.Combine(backupRoot, $"{modName}_{stamp}");
            int i = 1;
            while (Directory.Exists(backupPath))
                backupPath = Path.Combine(backupRoot, $"{modName}_{stamp}_{i++}");

            Directory.Move(mod.FolderPath, backupPath);
            AppLogger.Info($"Deleted mod '{modName}' (backed up to '{backupPath}')");
            return backupPath;
        }
        catch (Exception ex)
        {
            AppLogger.Error("DeleteModWithBackup failed", ex);
            throw;
        }
    }

    public record ApplyResult(List<string> Missing, int Moved, bool Healed);

    /// <summary>
    /// Применяет профиль. Матчит записи с папками на диске по FolderName, затем по Name
    /// (чинит профили, сохраненные по отображаемому имени). Проставляет недостающие
    /// FolderName прямо в переданный профиль (VM пересохраняет его).
    /// </summary>
    public ApplyResult ApplyProfile(string gameFolder, Profile profile)
    {
        var modsPath = GetModsPath(gameFolder);
        var disabledPath = GetDisabledModsPath(gameFolder);

        Directory.CreateDirectory(modsPath);
        Directory.CreateDirectory(disabledPath);

        // Текущее состояние с диска: у каждого мода есть FolderName (папка) и Name (из ModInfo).
        // Матчим записи профиля по любому из них — чинит профили, сохраненные по отображаемому имени.
        var current = ScanMods(gameFolder);

        static ModItem? Find(List<ModItem> items, string? key)
        {
            if (string.IsNullOrWhiteSpace(key))
                return null;
            return items.FirstOrDefault(m => m.FolderName.Equals(key, StringComparison.OrdinalIgnoreCase))
                ?? items.FirstOrDefault(m => m.Name.Equals(key, StringComparison.OrdinalIgnoreCase));
        }

        var missing = new List<string>();
        int moved = 0;
        bool healed = false;

        foreach (var modState in profile.Mods)
        {
            var mod = Find(current, modState.FolderName) ?? Find(current, modState.Name);
            if (mod == null)
            {
                missing.Add(modState.FolderName ?? modState.Name);
                continue;
            }

            // Самолечение: дописываем реальное имя папки
            if (string.IsNullOrWhiteSpace(modState.FolderName) ||
                !modState.FolderName.Equals(mod.FolderName, StringComparison.OrdinalIgnoreCase))
            {
                modState.FolderName = mod.FolderName;
                healed = true;
            }

            var targetPath = Path.Combine(modsPath, mod.FolderName);
            var disabledTarget = Path.Combine(disabledPath, mod.FolderName);

            if (modState.IsEnabled && !mod.IsEnabled)
            {
                if (Directory.Exists(targetPath))
                    Directory.Delete(targetPath, true);
                Directory.Move(mod.FolderPath, targetPath);
                AppLogger.Info($"ApplyProfile: enabled '{mod.FolderName}'");
                moved++;
            }
            else if (!modState.IsEnabled && mod.IsEnabled)
            {
                if (Directory.Exists(disabledTarget))
                    Directory.Delete(disabledTarget, true);
                Directory.Move(mod.FolderPath, disabledTarget);
                AppLogger.Info($"ApplyProfile: disabled '{mod.FolderName}'");
                moved++;
            }
        }
        if (missing.Count > 0)
            AppLogger.Warn($"ApplyProfile: missing mods: {string.Join(", ", missing)}");
        return new ApplyResult(missing, moved, healed);
    }

    public string FindGameFolder()
    {
        // Попытка найти игру в типичных местах
        string[] potentialPaths = {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "7DaysToDie"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "7DaysToDie"),
            Path.Combine(Path.GetPathRoot(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles)) ?? string.Empty, "7DaysToDie"),
            // Steam - папка с пробелами "7 Days To Die"
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Steam", "steamapps", "common", "7 Days To Die"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Steam", "steamapps", "common", "7 Days To Die"),
            Path.Combine("C:", "Steam", "steamapps", "common", "7 Days To Die"),
            Path.Combine("D:", "Steam", "steamapps", "common", "7 Days To Die"),
            Path.Combine("C:", "SteamLibrary", "steamapps", "common", "7 Days To Die"),
            Path.Combine("D:", "SteamLibrary", "steamapps", "common", "7 Days To Die"),
        };

        foreach (var path in potentialPaths)
        {
            if (Directory.Exists(path) && File.Exists(Path.Combine(path, "7DaysToDie.exe")))
                return path;
        }

        // Поиск через реестр Steam и чтение всех библиотек Steam (libraryfolders.vdf)
        try
        {
            using var registryKey = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam");
            if (registryKey != null)
            {
                var steamPath = registryKey.GetValue("SteamPath") as string;
                if (!string.IsNullOrEmpty(steamPath))
                {
                    // 1. Проверяем основную библиотеку Steam с папкой "7 Days To Die"
                    var mainGamePath = Path.Combine(steamPath, "steamapps", "common", "7 Days To Die");
                    if (Directory.Exists(mainGamePath) && File.Exists(Path.Combine(mainGamePath, "7DaysToDie.exe")))
                        return mainGamePath;

                    // 2. Читаем файл libraryfolders.vdf, чтобы найти все дополнительные библиотеки Steam на других дисках (D:, E:, F: и т.д.)
                    var libraryFoldersPath = Path.Combine(steamPath, "steamapps", "libraryfolders.vdf");
                    if (File.Exists(libraryFoldersPath))
                    {
                        var content = File.ReadAllText(libraryFoldersPath);
                        // Ищем все строки вида "path" "C:\\Steam" или "path" "D:\\SteamLibrary"
                        var matches = System.Text.RegularExpressions.Regex.Matches(content, "\"path\"\\s*\"([^\"]+)\"");
                        foreach (System.Text.RegularExpressions.Match match in matches)
                        {
                            if (match.Success)
                            {
                                var libPath = match.Groups[1].Value.Replace(@"\\", @"\");
                                var gamePath = Path.Combine(libPath, "steamapps", "common", "7 Days To Die");
                                if (Directory.Exists(gamePath) && File.Exists(Path.Combine(gamePath, "7DaysToDie.exe")))
                                    return gamePath;
                            }
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            AppLogger.Warn($"Steam registry lookup failed: {ex.Message}");
        }

        return string.Empty;
    }
}
