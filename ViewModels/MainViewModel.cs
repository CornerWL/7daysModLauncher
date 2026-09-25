using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MessageBox = System.Windows.MessageBox;
using OpenFileDialog = Microsoft.Win32.OpenFileDialog;
using SevenDaysModLauncher.Models;
using SevenDaysModLauncher.Services;

namespace SevenDaysModLauncher.ViewModels;

public partial class MainViewModel : ObservableObject
{
    private readonly SettingsService _settingsService;
    private readonly ModService _modService;
    private readonly ProfileService _profileService;
    private readonly IGameLauncherService _launcherService;
    private List<ModItem> _allMods = new();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanLaunchGame))]
    private string _gameFolderPath = string.Empty;

    [ObservableProperty]
    private ObservableCollection<ModItem> _mods = new();

    [ObservableProperty]
    private string _searchText = string.Empty;

    [ObservableProperty]
    private string _sortBy = "Name";

    [ObservableProperty]
    private bool _sortAscending = true;

    [ObservableProperty]
    private string _statusFilter = "All";

    [ObservableProperty]
    private ModItem? _selectedMod;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsNotBusy))]
    [NotifyPropertyChangedFor(nameof(ShowStatus))]
    private bool _isBusy;

    [ObservableProperty]
    private double _progressValue;

    [ObservableProperty]
    private string _statusMessage = string.Empty;

    [ObservableProperty]
    private ObservableCollection<string> _profiles = new();

    [ObservableProperty]
    private string _selectedProfile = string.Empty;

    public bool IsNotBusy => !IsBusy;

    /// <summary>Тост виден, когда есть сообщение и не идет установка.</summary>
    public bool ShowStatus => !IsBusy && !string.IsNullOrEmpty(StatusMessage);

    private int _statusSeq;

    partial void OnStatusMessageChanged(string value)
    {
        OnPropertyChanged(nameof(ShowStatus));
        if (string.IsNullOrEmpty(value))
            return;
        // Автоскрытие тоста через 6 секунд
        var seq = ++_statusSeq;
        _ = Task.Run(async () =>
        {
            await Task.Delay(6000);
            if (seq != _statusSeq)
                return;
            await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
            {
                if (seq == _statusSeq && !IsBusy)
                    StatusMessage = string.Empty;
            });
        });
    }

    public bool CanLaunchGame => !string.IsNullOrEmpty(GameFolderPath) && Directory.Exists(GameFolderPath);

    public int EnabledCount => _allMods.Count(m => m.IsEnabled);

    public int TotalCount => _allMods.Count;

    public string SortDirectionLabel => SortAscending ? "▲" : "▼";

    public MainViewModel() : this(new SettingsService(), new ModService(), new ProfileService(), new GameLauncherService())
    {
    }

    public MainViewModel(SettingsService settingsService, ModService modService, ProfileService profileService, IGameLauncherService launcherService)
    {
        _settingsService = settingsService;
        _modService = modService;
        _profileService = profileService;
        _launcherService = launcherService;

        LoadSettings();
        RefreshProfiles();
        RestoreSelectedProfile();
        _ = CheckForUpdatesAsync();
    }

    /// <summary>Возвращает последний выбранный профиль и применяет его (состояния модов).</summary>
    private void RestoreSelectedProfile()
    {
        try
        {
            var saved = _settingsService.Load().SelectedProfile;
            if (!string.IsNullOrWhiteSpace(saved) && Profiles.Contains(saved))
                SelectedProfile = saved;
        }
        catch (Exception ex)
        {
            AppLogger.Warn($"RestoreSelectedProfile failed: {ex.Message}");
        }
    }

    partial void OnSelectedProfileChanged(string value) => SaveSettings();

    private async Task CheckForUpdatesAsync()
    {
        try
        {
            var info = await new UpdateCheckService().CheckAsync();
            if (info?.HasUpdate == true)
            {
                StatusMessage = $"New version {info.Tag} available (you have {info.Current}).";
                var res = MessageBox.Show(
                    $"New version {info.Tag} is out (you have {info.Current}).\nOpen the release page?",
                    "Update available", MessageBoxButton.YesNo, MessageBoxImage.Information);
                if (res == MessageBoxResult.Yes && !string.IsNullOrWhiteSpace(info.Url))
                    Process.Start(new ProcessStartInfo { FileName = info.Url, UseShellExecute = true });
            }
        }
        catch (Exception ex)
        {
            AppLogger.Warn($"Update check UI failed: {ex.Message}");
        }
    }

    private void RefreshProfiles()
    {
        Profiles.Clear();
        var profiles = _profileService.GetAllProfiles();
        foreach (var profile in profiles)
        {
            Profiles.Add(profile.Name);
        }
    }

    partial void OnSearchTextChanged(string value) => ApplyFilter();
    partial void OnSortByChanged(string value) => ApplyFilter();
    partial void OnStatusFilterChanged(string value) => ApplyFilter();

    partial void OnSortAscendingChanged(bool value)
    {
        OnPropertyChanged(nameof(SortDirectionLabel));
        ApplyFilter();
    }

    public string[] SortOptions { get; } = { "Name", "Author", "Version", "Status" };
    public string[] StatusFilterOptions { get; } = { "All", "Enabled", "Disabled" };

    private void ApplyFilter()
    {
        var q = SearchText?.Trim();
        IEnumerable<ModItem> query = _allMods;

        if (StatusFilter == "Enabled")
            query = query.Where(m => m.IsEnabled);
        else if (StatusFilter == "Disabled")
            query = query.Where(m => !m.IsEnabled);

        if (!string.IsNullOrWhiteSpace(q))
        {
            query = query.Where(m =>
                m.Name.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                (m.Author != null && m.Author.Contains(q, StringComparison.OrdinalIgnoreCase)) ||
                (m.Version != null && m.Version.Contains(q, StringComparison.OrdinalIgnoreCase)));
        }

        query = SortBy switch
        {
            "Author" => SortAscending
                ? query.OrderBy(m => m.Author ?? "").ThenBy(m => m.Name)
                : query.OrderByDescending(m => m.Author ?? "").ThenBy(m => m.Name),
            "Version" => SortAscending
                ? query.OrderBy(m => m.Version ?? "").ThenBy(m => m.Name)
                : query.OrderByDescending(m => m.Version ?? "").ThenBy(m => m.Name),
            "Status" => SortAscending
                ? query.OrderByDescending(m => m.IsEnabled).ThenBy(m => m.Name)
                : query.OrderBy(m => m.IsEnabled).ThenBy(m => m.Name),
            _ => SortAscending
                ? query.OrderBy(m => m.Name)
                : query.OrderByDescending(m => m.Name),
        };

        Mods = new ObservableCollection<ModItem>(query);
        OnPropertyChanged(nameof(EnabledCount));
        OnPropertyChanged(nameof(TotalCount));
    }

    private string NormalizeGameFolderPath(string path) => GamePathHelper.NormalizeGameFolderPath(path);

    private void LoadSettings()
    {
        var settings = _settingsService.Load();
        if (!string.IsNullOrEmpty(settings.GameFolderPath) && Directory.Exists(settings.GameFolderPath))
        {
            GameFolderPath = NormalizeGameFolderPath(settings.GameFolderPath);
        }
        else
        {
            // Попытка автоматически найти папку игры
            var detectedPath = _modService.FindGameFolder();
            if (!string.IsNullOrEmpty(detectedPath))
            {
                GameFolderPath = NormalizeGameFolderPath(detectedPath);
                // Сохраняем найденный путь
                SaveSettings();
            }
            else
            {
                // Предложить пользователю выбрать вручную
                var result = MessageBox.Show("Could not find the game folder automatically. Select it manually?", "Game folder not found", MessageBoxButton.YesNo, MessageBoxImage.Question);
                if (result == MessageBoxResult.Yes)
                {
                    BrowseGameFolder();
                }
            }
        }
        if (!string.IsNullOrEmpty(GameFolderPath) && Directory.Exists(GameFolderPath))
        {
            // Убедиться, что папки Mods и Mods_Disabled существуют
            GamePathHelper.EnsureModDirectories(GameFolderPath);
            RefreshMods();
        }
    }

    private void SaveSettings()
    {
        _settingsService.Save(new AppSettings { GameFolderPath = GameFolderPath, SelectedProfile = SelectedProfile });
    }

    [RelayCommand]
    private void BrowseGameFolder()
    {
        var dialog = new System.Windows.Forms.FolderBrowserDialog
        {
            Description = "Select the 7 Days to Die game folder (containing 7DaysToDie.exe)"
        };

        if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
        {
            GameFolderPath = NormalizeGameFolderPath(dialog.SelectedPath);
            if (!GamePathHelper.IsValidGameFolder(GameFolderPath))
            {
                var res = MessageBox.Show(
                    $"{GamePathHelper.GameExeName} not found in this folder.\n\n{GameFolderPath}\n\nUse this folder anyway?",
                    "This doesn't look like the game folder",
                    MessageBoxButton.YesNo, MessageBoxImage.Warning);
                if (res != MessageBoxResult.Yes)
                    return;
            }
            // Ensure required directories exist
            GamePathHelper.EnsureModDirectories(GameFolderPath);
            SaveSettings();
            RefreshMods();
            OnPropertyChanged(nameof(CanLaunchGame));
        }
    }

    [RelayCommand]
    private void AutoDetectGameFolder()
    {
        var detectedPath = _modService.FindGameFolder();
        if (!string.IsNullOrEmpty(detectedPath))
        {
            GameFolderPath = NormalizeGameFolderPath(detectedPath);
            // Ensure required directories exist
            GamePathHelper.EnsureModDirectories(GameFolderPath);
            SaveSettings();
            RefreshMods();
            OnPropertyChanged(nameof(CanLaunchGame));
            StatusMessage = "Game folder detected automatically!";
        }
        else
        {
            MessageBox.Show("Could not detect the game folder automatically. Please select it manually.", "Detection failed", MessageBoxButton.OK, MessageBoxImage.Information);
            StatusMessage = "Automatic detection failed.";
        }
    }

    [RelayCommand]
    private void LaunchGame()
    {
        if (!CanLaunchGame)
        {
            MessageBox.Show("Select the game folder first.", "Error", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        if (!_launcherService.TryLaunch(GameFolderPath, out var error))
        {
            MessageBox.Show($"Failed to launch the game:\n{error}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            StatusMessage = "Failed to launch the game.";
        }
        else
        {
            StatusMessage = "Launching the game...";
        }
    }

    [RelayCommand]
    private void OpenModFolder(ModItem? mod)
    {
        try
        {
            var path = mod?.FolderPath;
            if (string.IsNullOrEmpty(path) || !Directory.Exists(path))
            {
                MessageBox.Show("Mod folder not found.", "Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            Process.Start(new ProcessStartInfo { FileName = path, UseShellExecute = true });
        }
        catch (Exception ex)
        {
            AppLogger.Error("OpenModFolder failed", ex);
            MessageBox.Show($"Failed to open the folder:\n{ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    [RelayCommand]
    private void EnableAllMods()
    {
        SetAllModsEnabled(true);
    }

    [RelayCommand]
    private void DisableAllMods()
    {
        SetAllModsEnabled(false);
    }

    private void SetAllModsEnabled(bool enabled)
    {
        if (string.IsNullOrEmpty(GameFolderPath) || !Directory.Exists(GameFolderPath))
            return;
        if (!EnsureGameNotRunning("toggle mods"))
            return;
        try
        {
            foreach (var mod in _allMods.Where(m => m.IsEnabled != enabled).ToList())
            {
                _modService.ToggleMod(GameFolderPath, mod);
            }
            RefreshMods();
            AutoSaveProfile();
            StatusMessage = enabled ? "All mods enabled." : "All mods disabled.";
        }
        catch (Exception ex)
        {
            AppLogger.Error("SetAllModsEnabled failed", ex);
            MessageBox.Show($"Failed:\n{ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    [RelayCommand]
    private void RefreshMods()
    {
        // Запоминаем выделение по именам папок, чтобы восстановить после перескана
        var selectedNames = _allMods.Where(m => m.IsSelected).Select(m => m.FolderName).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var focusedKey = SelectedMod?.FolderName;

        if (string.IsNullOrEmpty(GameFolderPath) || !Directory.Exists(GameFolderPath))
        {
            _allMods = new List<ModItem>();
            Mods.Clear();
            SelectedMod = null;
            OnPropertyChanged(nameof(EnabledCount));
            OnPropertyChanged(nameof(TotalCount));
            return;
        }

        _allMods = _modService.ScanMods(GameFolderPath);
        foreach (var mod in _allMods)
            mod.IsSelected = selectedNames.Contains(mod.FolderName);
        SelectedMod = string.IsNullOrEmpty(focusedKey)
            ? null
            : _allMods.FirstOrDefault(m => m.FolderName.Equals(focusedKey, StringComparison.OrdinalIgnoreCase));
        ApplyFilter();
    }

    private CancellationTokenSource? _installCts;

    /// <summary>
    /// Автозапись: любое изменение набора модов сразу сохраняется в выбранный профиль.
    /// Кнопка Save нужна только для создания нового профиля.
    /// </summary>
    private void AutoSaveProfile()
    {
        if (string.IsNullOrWhiteSpace(SelectedProfile))
            return;
        try
        {
            var profile = _profileService.CreateFromCurrentState(_allMods.ToList());
            profile.Name = SelectedProfile;
            _profileService.SaveProfile(profile);
            AppLogger.Info($"Autosaved profile '{SelectedProfile}'");
        }
        catch (Exception ex)
        {
            AppLogger.Warn($"Autosave profile failed: {ex.Message}");
        }
    }

    private bool EnsureGameNotRunning(string action)
    {
        if (_launcherService.IsGameRunning())
        {
            MessageBox.Show($"Cannot {action} while the game is running.\nClose 7 Days to Die and try again.",
                "Game is running", MessageBoxButton.OK, MessageBoxImage.Warning);
            StatusMessage = "Wait until the game is closed.";
            return false;
        }
        return true;
    }

    [RelayCommand]
    private void CancelInstall()
    {
        _installCts?.Cancel();
    }

    [RelayCommand]
    private async Task InstallModAsync()
    {
        if (string.IsNullOrEmpty(GameFolderPath))
        {
            MessageBox.Show("Select the game folder first.", "Error", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        if (!EnsureGameNotRunning("install mods"))
            return;

        var dialog = new OpenFileDialog
        {
            Filter = "ZIP archives (*.zip)|*.zip|All files (*.*)|*.*",
            Title = "Select a mod archive"
        };

        if (dialog.ShowDialog() != true)
            return;

        await InstallZipsAsync(new[] { dialog.FileName });
    }

    private async Task InstallZipsAsync(IEnumerable<string> zipFiles)
    {
        _installCts?.Dispose();
        _installCts = new CancellationTokenSource();
        var token = _installCts.Token;

        IsBusy = true;
        ProgressValue = 0;
        StatusMessage = "Installing mod...";

        try
        {
            int index = 0;
            foreach (var zip in zipFiles)
            {
                token.ThrowIfCancellationRequested();
                index++;
                StatusMessage = zipFiles.Count() > 1
                    ? $"Installing {index}/{zipFiles.Count()}: {Path.GetFileName(zip)}..."
                    : $"Installing: {Path.GetFileName(zip)}...";
                var progress = new Progress<double>(value => ProgressValue = value);
                await _modService.InstallModAsync(GameFolderPath, zip, progress, token);
            }
            StatusMessage = zipFiles.Count() > 1 ? "Mods installed (old versions moved to Mods_Backup)." : "Mod installed (old version moved to Mods_Backup).";
            RefreshMods();
            AutoSaveProfile();
        }
        catch (OperationCanceledException)
        {
            StatusMessage = "Install cancelled.";
            AppLogger.Info("Install cancelled by user");
        }
        catch (Exception ex)
        {
            AppLogger.Error("Install failed", ex);
            MessageBox.Show($"Failed to install mod:\n{ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            StatusMessage = "Failed to install mod.";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task InstallModFromDropAsync(string[] files)
    {
        if (string.IsNullOrEmpty(GameFolderPath))
        {
            MessageBox.Show("Select the game folder first.", "Error", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        if (!EnsureGameNotRunning("install mods"))
            return;

        var zipFiles = files.Where(f => Path.GetExtension(f).Equals(".zip", StringComparison.OrdinalIgnoreCase)).ToList();
        if (!zipFiles.Any())
        {
            MessageBox.Show("Drop ZIP archives.", "Error", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        await InstallZipsAsync(zipFiles);
        ProgressValue = 0;
    }

    [RelayCommand]
    private void DeleteMod(ModItem? mod)
    {
        if (mod == null)
            return;
        if (!EnsureGameNotRunning("delete mods"))
            return;

        var result = MessageBox.Show($"Delete mod \"{mod.Name}\"?\n\nA backup copy will be kept in Mods_Backup.", "Confirm", MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (result != MessageBoxResult.Yes)
            return;

        try
        {
            var backup = _modService.DeleteModWithBackup(mod, GameFolderPath);
            _allMods.Remove(mod);
            ApplyFilter();
            AutoSaveProfile();
            StatusMessage = backup != null
                ? $"Mod \"{mod.Name}\" deleted (backup: {backup})."
                : $"Mod \"{mod.Name}\" deleted.";
        }
        catch (Exception ex)
        {
            AppLogger.Error("DeleteMod failed", ex);
            MessageBox.Show($"Failed to delete mod:\n{ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    [RelayCommand]
    private void ToggleMod(ModItem? mod)
    {
        if (mod == null || string.IsNullOrEmpty(GameFolderPath))
            return;
        if (!EnsureGameNotRunning("toggle mods"))
            return;

        try
        {
            _modService.ToggleMod(GameFolderPath, mod);
            // Update IsEnabled based on actual folder location after move
            var modsPath = GamePathHelper.GetModsPath(GameFolderPath);
            mod.IsEnabled = mod.FolderPath.StartsWith(modsPath, System.StringComparison.OrdinalIgnoreCase);
            StatusMessage = $"Mod \"{mod.Name}\" {(mod.IsEnabled ? "enabled" : "disabled")}.";
            // Refresh the list to ensure UI reflects the current state
            RefreshMods();
            AutoSaveProfile();
        }
        catch (Exception ex)
        {
            AppLogger.Error("ToggleMod failed", ex);
            MessageBox.Show($"Failed to toggle mod:\n{ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    [RelayCommand]
    private void SaveProfile()
    {
        if (string.IsNullOrEmpty(GameFolderPath))
        {
            MessageBox.Show("Select the game folder first.", "Error", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var dialog = new Views.ProfileNameDialog();
        if (dialog.ShowDialog() != true)
            return;

        var profileName = dialog.ProfileName;
        if (string.IsNullOrWhiteSpace(profileName))
            return;

        var profile = _profileService.CreateFromCurrentState(_allMods.ToList());
        profile.Name = profileName;
        
        _profileService.SaveProfile(profile);
        RefreshProfiles();
        SelectedProfile = profileName;
        StatusMessage = $"Profile \"{profileName}\" saved.";
    }

    [RelayCommand]
    private void LoadProfile()
    {
        if (string.IsNullOrEmpty(SelectedProfile))
            return;
        if (string.IsNullOrEmpty(GameFolderPath) || !Directory.Exists(GameFolderPath))
            return;
        if (!EnsureGameNotRunning("apply the profile"))
            return;

        var profile = _profileService.LoadProfile(SelectedProfile);
        if (profile == null)
        {
            MessageBox.Show("Failed to load the profile.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        try
        {
            var result = _modService.ApplyProfile(GameFolderPath, profile);
            if (result.Healed)
                _profileService.SaveProfile(profile);
            RefreshMods();
            StatusMessage = result.Missing.Count == 0
                ? (result.Moved > 0
                    ? $"Profile \"{SelectedProfile}\" applied (moved: {result.Moved})."
                    : $"Profile \"{SelectedProfile}\" is already in sync.")
                : $"Profile applied (moved: {result.Moved}), missing on disk ({result.Missing.Count}): {string.Join(", ", result.Missing.Take(5))}{(result.Missing.Count > 5 ? "..." : "")}";
            if (result.Missing.Count > 0)
                MessageBox.Show($"Profile partially applied.\nMods not found on disk:\n• {string.Join("\n• ", result.Missing)}",
                    "Some mods are missing", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Failed to apply profile:\n{ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    [RelayCommand]
    private void DeleteProfile()
    {
        if (string.IsNullOrEmpty(SelectedProfile))
            return;

        var result = MessageBox.Show($"Delete profile \"{SelectedProfile}\"?", "Confirm", MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (result != MessageBoxResult.Yes)
            return;

        _profileService.DeleteProfile(SelectedProfile);
        RefreshProfiles();
        SelectedProfile = string.Empty;
        StatusMessage = "Profile deleted.";
    }

    [RelayCommand]
    private void ToggleSortDirection()
    {
        SortAscending = !SortAscending;
    }

    /// <summary>Сброс выделения (клик по пустому месту списка).</summary>
    [RelayCommand]
    private void ClearSelection()
    {
        SelectedMod = null;
        foreach (var mod in _allMods)
            mod.IsSelected = false;
    }

    [RelayCommand]
    private void OpenWebsite(string? url)
    {
        if (string.IsNullOrWhiteSpace(url))
            return;
        try
        {
            var fixed_url = url.StartsWith("http", StringComparison.OrdinalIgnoreCase) ? url : "https://" + url;
            Process.Start(new ProcessStartInfo { FileName = fixed_url, UseShellExecute = true });
        }
        catch (Exception ex)
        {
            AppLogger.Error("OpenWebsite failed", ex);
            MessageBox.Show($"Failed to open the link:\n{ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    [RelayCommand]
    private void EnableSelectedMods()
    {
        SetSelectedModsEnabled(true);
    }

    [RelayCommand]
    private void DisableSelectedMods()
    {
        SetSelectedModsEnabled(false);
    }

    private void SetSelectedModsEnabled(bool enabled)
    {
        var list = _allMods.Where(m => m.IsSelected).ToList();
        if (list.Count == 0 || string.IsNullOrEmpty(GameFolderPath))
            return;
        if (!EnsureGameNotRunning("toggle mods"))
            return;
        try
        {
            foreach (var mod in list.Where(m => m.IsEnabled != enabled))
                _modService.ToggleMod(GameFolderPath, mod);
            RefreshMods();
            AutoSaveProfile();
            StatusMessage = enabled ? $"Enabled: {list.Count}." : $"Disabled: {list.Count}.";
        }
        catch (Exception ex)
        {
            AppLogger.Error("SetSelectedModsEnabled failed", ex);
            MessageBox.Show($"Failed:\n{ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    [RelayCommand]
    private void DeleteSelectedMods()
    {
        var list = _allMods.Where(m => m.IsSelected).ToList();
        if (list.Count == 0)
            return;
        if (!EnsureGameNotRunning("delete mods"))
            return;

        var result = MessageBox.Show($"Delete {list.Count} mods?\nBackup copies will be kept in Mods_Backup.",
            "Confirm", MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (result != MessageBoxResult.Yes)
            return;

        try
        {
            foreach (var mod in list)
            {
                _modService.DeleteModWithBackup(mod, GameFolderPath);
                _allMods.Remove(mod);
            }
            SelectedMod = null;
            ApplyFilter();
            AutoSaveProfile();
            StatusMessage = $"Deleted {list.Count} mods (backups in Mods_Backup).";
        }
        catch (Exception ex)
        {
            AppLogger.Error("DeleteSelectedMods failed", ex);
            MessageBox.Show($"Failed to delete:\n{ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }
}
