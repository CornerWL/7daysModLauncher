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
        var name = Path.GetFileName(folderPath);
        var (version, author, description, displayName) = TryReadModInfo(folderPath);

        return new ModItem
        {
            Name = string.IsNullOrWhiteSpace(displayName) ? name : displayName,
            IsEnabled = isEnabled,
            Version = version,
            Author = author,
            Description = description,
            FolderPath = folderPath
        };
    }

    /// <summary>
    /// Читает ModInfo.xml в обоих распространенных форматах:
    /// 1) &lt;ModInfo Name="..." Author="..." Version="..." ...&gt; (&lt;Description&gt;...&gt;)
    /// 2) &lt;ModInfo&gt;&lt;Name&gt;...&lt;Version value="..."/&gt;... (формат 7DTD v2)
    /// Возвращает (version, author, description, displayName).
    /// </summary>
    private static (string? Version, string? Author, string? Description, string? DisplayName) TryReadModInfo(string modFolder)
    {
        try
        {
            var modInfoPath = Path.Combine(modFolder, "ModInfo.xml");
            if (!File.Exists(modInfoPath))
                return (null, null, null, null);

            var doc = System.Xml.Linq.XDocument.Load(modInfoPath);
            var root = doc.Root;
            if (root == null)
                return (null, null, null, null);

            // Формат 1: атрибуты
            string? version = root.Attribute("Version")?.Value
                ?? root.Element("Version")?.Value
                ?? root.Element("Version")?.Attribute("value")?.Value;
            string? author = root.Attribute("Author")?.Value ?? root.Element("Author")?.Value;
            string? description = root.Attribute("Description")?.Value ?? root.Element("Description")?.Value;
            string? displayName = root.Attribute("Name")?.Value ?? root.Element("Name")?.Value;

            // Формат 2: <ModInfo><Version value="1.0" /> ...
            if (string.IsNullOrWhiteSpace(version))
            {
                var vEl = root.Descendants("Version").FirstOrDefault();
                version = vEl?.Attribute("value")?.Value ?? vEl?.Value;
            }

            return (
                string.IsNullOrWhiteSpace(version) ? null : version.Trim(),
                string.IsNullOrWhiteSpace(author) ? null : author.Trim(),
                string.IsNullOrWhiteSpace(description) ? null : description.Trim(),
                string.IsNullOrWhiteSpace(displayName) ? null : displayName.Trim());
        }
        catch (Exception ex)
        {
            AppLogger.Warn($"Failed to read ModInfo.xml in '{modFolder}': {ex.Message}");
        }

        return (null, null, null, null);
    }

    private static string? TryReadVersion(string modFolder) => TryReadModInfo(modFolder).Version;

    public async Task InstallModAsync(string gameFolder, string zipPath, IProgress<double>? progress = null)
    {
        // Создаем папки, если их нет
        var modsPath = GetModsPath(gameFolder);
        Directory.CreateDirectory(modsPath);
        var disabledPath = GetDisabledModsPath(gameFolder);
        Directory.CreateDirectory(disabledPath);

        if (!File.Exists(zipPath))
            throw new FileNotFoundException($"Архив не найден: {zipPath}");

        var tempFolder = Path.Combine(Path.GetTempPath(), "7dtd_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempFolder);

        try
        {
            progress?.Report(0.1);
            await Task.Run(() => ZipFile.ExtractToDirectory(zipPath, tempFolder, overwriteFiles: true));
            progress?.Report(0.4);

            var modFolders = FindModFolders(tempFolder, zipPath);
            if (modFolders.Count == 0)
                throw new InvalidOperationException("Не удалось найти папку мода в архиве.");

            progress?.Report(0.6);
            int done = 0;
            foreach (var modFolder in modFolders)
            {
                var modName = Path.GetFileName(modFolder);
                if (modFolder == tempFolder)
                    modName = Path.GetFileNameWithoutExtension(zipPath);

                modName = string.Join("_", modName.Split(Path.GetInvalidFileNameChars()));
                if (string.IsNullOrWhiteSpace(modName))
                    modName = Path.GetFileNameWithoutExtension(zipPath);

                var targetPath = Path.Combine(modsPath, modName);
                if (Directory.Exists(targetPath))
                {
                    AppLogger.Warn($"Overwriting existing mod '{modName}' at '{targetPath}'");
                    Directory.Delete(targetPath, true);
                }
                // Не затираем отключенную копию молча: удаляем конфликт, чтобы не было дублей
                var disabledTarget = Path.Combine(disabledPath, modName);
                if (Directory.Exists(disabledTarget))
                    Directory.Delete(disabledTarget, true);

                await Task.Run(() => CopyDirectory(modFolder, targetPath));
                AppLogger.Info($"Installed mod '{modName}' from '{zipPath}'");
                done++;
                progress?.Report(0.6 + 0.4 * done / modFolders.Count);
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
        if (Directory.Exists(mod.FolderPath))
            Directory.Delete(mod.FolderPath, true);
    }

    public void ApplyProfile(string gameFolder, Profile profile)
    {
        var modsPath = GetModsPath(gameFolder);
        var disabledPath = GetDisabledModsPath(gameFolder);

        Directory.CreateDirectory(modsPath);
        Directory.CreateDirectory(disabledPath);

        foreach (var modState in profile.Mods)
        {
            var modPath = Path.Combine(modsPath, modState.Name);
            var disabledPath2 = Path.Combine(disabledPath, modState.Name);

            var isInMods = Directory.Exists(modPath);
            var isInDisabled = Directory.Exists(disabledPath2);

            if (modState.IsEnabled && isInDisabled)
            {
                if (Directory.Exists(modPath))
                    Directory.Delete(modPath, true);
                Directory.Move(disabledPath2, modPath);
            }
            else if (!modState.IsEnabled && isInMods)
            {
                if (Directory.Exists(disabledPath2))
                    Directory.Delete(disabledPath2, true);
                Directory.Move(modPath, disabledPath2);
            }
        }
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
