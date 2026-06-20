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
        var version = TryReadVersion(folderPath);

        return new ModItem
        {
            Name = name,
            IsEnabled = isEnabled,
            Version = version,
            FolderPath = folderPath
        };
    }

    private static string? TryReadVersion(string modFolder)
    {
        try
        {
            var modInfoPath = Path.Combine(modFolder, "ModInfo.xml");
            if (File.Exists(modInfoPath))
            {
                var doc = System.Xml.Linq.XDocument.Load(modInfoPath);
                var versionAttr = doc.Root?.Attribute("Version");
                if (versionAttr != null)
                    return versionAttr.Value;

                var versionElement = doc.Root?.Element("Version");
                if (versionElement != null)
                    return versionElement.Value;
            }
        }
        catch { }

        return null;
    }

    public async Task InstallModAsync(string gameFolder, string zipPath, IProgress<double>? progress = null)
    {
        // Создаем папки, если их нет
        var modsPath = GetModsPath(gameFolder);
        Directory.CreateDirectory(modsPath);
        var disabledPath = GetDisabledModsPath(gameFolder);
        Directory.CreateDirectory(disabledPath);

        var tempFolder = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(tempFolder);

        try
        {
            progress?.Report(0.1);
            await Task.Run(() => ZipFile.ExtractToDirectory(zipPath, tempFolder));
            progress?.Report(0.4);

            var modFolder = FindModFolder(tempFolder);
            if (modFolder == null)
                throw new InvalidOperationException("Не удалось найти папку мода в архиве.");

            // Determine mod name: if the found folder is the temp root (no dedicated folder), use zip file name
            var modName = Path.GetFileName(modFolder);
            if (modFolder == tempFolder)
            {
                modName = Path.GetFileNameWithoutExtension(zipPath);
            }
            var targetPath = Path.Combine(modsPath, modName);
            if (Directory.Exists(targetPath))
                Directory.Delete(targetPath, true);

            progress?.Report(0.6);
            await Task.Run(() => CopyDirectory(modFolder, targetPath));
            progress?.Report(1.0);
        }
        finally
        {
            if (Directory.Exists(tempFolder))
                Directory.Delete(tempFolder, true);
        }
    }

    private static string? FindModFolder(string extractedPath)
    {
        // Если в корне есть ModInfo.xml — это и есть папка мода
        if (File.Exists(Path.Combine(extractedPath, "ModInfo.xml")))
            return extractedPath;

        // Ищем в подпапках
        foreach (var dir in Directory.GetDirectories(extractedPath))
        {
            if (File.Exists(Path.Combine(dir, "ModInfo.xml")))
                return dir;
        }

        // Если ModInfo.xml не найден, берём первую подпапку
        var subDirs = Directory.GetDirectories(extractedPath);
        if (subDirs.Length == 1)
            return subDirs[0];

        return extractedPath;
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
        catch { }

        return string.Empty;
    }
}
