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
    [NotifyPropertyChangedFor(nameof(IsNotBusy))]
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

    public bool CanLaunchGame => !string.IsNullOrEmpty(GameFolderPath) && Directory.Exists(GameFolderPath);

    public int EnabledCount => _allMods.Count(m => m.IsEnabled);

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

    private void ApplyFilter()
    {
        var q = SearchText?.Trim();
        IEnumerable<ModItem> query = _allMods.OrderBy(m => m.Name);
        if (!string.IsNullOrWhiteSpace(q))
        {
            query = query.Where(m =>
                m.Name.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                (m.Author != null && m.Author.Contains(q, StringComparison.OrdinalIgnoreCase)) ||
                (m.Version != null && m.Version.Contains(q, StringComparison.OrdinalIgnoreCase)));
        }
        Mods = new ObservableCollection<ModItem>(query);
        OnPropertyChanged(nameof(EnabledCount));
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
                var result = MessageBox.Show("Не удалось автоматически найти папку игры. Выбрать вручную?", "Папка игры не найдена", MessageBoxButton.YesNo, MessageBoxImage.Question);
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
        _settingsService.Save(new AppSettings { GameFolderPath = GameFolderPath });
    }

    [RelayCommand]
    private void BrowseGameFolder()
    {
        var dialog = new System.Windows.Forms.FolderBrowserDialog
        {
            Description = "Выберите папку с игрой 7 Days to Die (где лежит 7DaysToDie.exe)"
        };

        if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
        {
            GameFolderPath = NormalizeGameFolderPath(dialog.SelectedPath);
            if (!GamePathHelper.IsValidGameFolder(GameFolderPath))
            {
                var res = MessageBox.Show(
                    $"В папке не найден {GamePathHelper.GameExeName}.\n\n{GameFolderPath}\n\nВсе равно использовать эту папку?",
                    "Похоже, это не папка игры",
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
            StatusMessage = "Папка игры найдена автоматически!";
        }
        else
        {
            MessageBox.Show("Не удалось автоматически найти папку игры. Попробуйте выбрать её вручную.", "Поиск не удался", MessageBoxButton.OK, MessageBoxImage.Information);
            StatusMessage = "Автоматический поиск не удался.";
        }
    }

    [RelayCommand]
    private void LaunchGame()
    {
        if (!CanLaunchGame)
        {
            MessageBox.Show("Сначала выберите папку с игрой.", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        if (!_launcherService.TryLaunch(GameFolderPath, out var error))
        {
            MessageBox.Show($"Не удалось запустить игру:\n{error}", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
            StatusMessage = "Ошибка запуска игры.";
        }
        else
        {
            StatusMessage = "Игра запускается...";
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
                MessageBox.Show("Папка мода не найдена.", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            Process.Start(new ProcessStartInfo { FileName = path, UseShellExecute = true });
        }
        catch (Exception ex)
        {
            AppLogger.Error("OpenModFolder failed", ex);
            MessageBox.Show($"Не удалось открыть папку:\n{ex.Message}", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
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
        try
        {
            foreach (var mod in _allMods.Where(m => m.IsEnabled != enabled).ToList())
            {
                _modService.ToggleMod(GameFolderPath, mod);
            }
            RefreshMods();
            StatusMessage = enabled ? "Все моды включены." : "Все моды отключены.";
        }
        catch (Exception ex)
        {
            AppLogger.Error("SetAllModsEnabled failed", ex);
            MessageBox.Show($"Ошибка:\n{ex.Message}", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    [RelayCommand]
    private void RefreshMods()
    {
        if (string.IsNullOrEmpty(GameFolderPath) || !Directory.Exists(GameFolderPath))
        {
            _allMods = new List<ModItem>();
            Mods.Clear();
            OnPropertyChanged(nameof(EnabledCount));
            return;
        }

        _allMods = _modService.ScanMods(GameFolderPath);
        ApplyFilter();
    }

    [RelayCommand]
    private async Task InstallModAsync()
    {
        if (string.IsNullOrEmpty(GameFolderPath))
        {
            MessageBox.Show("Сначала выберите папку с игрой.", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var dialog = new OpenFileDialog
        {
            Filter = "ZIP архивы (*.zip)|*.zip|Все файлы (*.*)|*.*",
            Title = "Выберите архив мода"
        };

        if (dialog.ShowDialog() != true)
            return;

        IsBusy = true;
        ProgressValue = 0;
        StatusMessage = "Установка мода...";

        try
        {
            var progress = new Progress<double>(value => ProgressValue = value);
            await _modService.InstallModAsync(GameFolderPath, dialog.FileName, progress);
            StatusMessage = "Мод успешно установлен!";
            RefreshMods();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Ошибка установки мода:\n{ex.Message}", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
            StatusMessage = "Ошибка установки мода.";
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
            MessageBox.Show("Сначала выберите папку с игрой.", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var zipFiles = files.Where(f => Path.GetExtension(f).Equals(".zip", StringComparison.OrdinalIgnoreCase)).ToList();
        if (!zipFiles.Any())
        {
            MessageBox.Show("Перетащите ZIP архивы.", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        IsBusy = true;
        StatusMessage = "Установка модов...";

        try
        {
            foreach (var zipFile in zipFiles)
            {
                var progress = new Progress<double>(value => ProgressValue = value);
                await _modService.InstallModAsync(GameFolderPath, zipFile, progress);
            }
            StatusMessage = "Моды успешно установлены!";
            RefreshMods();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Ошибка установки мода:\n{ex.Message}", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
            StatusMessage = "Ошибка установки мода.";
        }
        finally
        {
            IsBusy = false;
            ProgressValue = 0;
        }
    }

    [RelayCommand]
    private void DeleteMod(ModItem? mod)
    {
        if (mod == null)
            return;

        var result = MessageBox.Show($"Удалить мод \"{mod.Name}\"?", "Подтверждение", MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (result != MessageBoxResult.Yes)
            return;

        try
        {
            _modService.DeleteMod(mod);
            _allMods.Remove(mod);
            AppLogger.Info($"Deleted mod '{mod.Name}'");
            ApplyFilter();
            StatusMessage = $"Мод \"{mod.Name}\" удалён.";
        }
        catch (Exception ex)
        {
            AppLogger.Error("DeleteMod failed", ex);
            MessageBox.Show($"Ошибка удаления мода:\n{ex.Message}", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    [RelayCommand]
    private void ToggleMod(ModItem? mod)
    {
        if (mod == null || string.IsNullOrEmpty(GameFolderPath))
            return;

        try
        {
            _modService.ToggleMod(GameFolderPath, mod);
            // Update IsEnabled based on actual folder location after move
            var modsPath = GamePathHelper.GetModsPath(GameFolderPath);
            mod.IsEnabled = mod.FolderPath.StartsWith(modsPath, System.StringComparison.OrdinalIgnoreCase);
            StatusMessage = $"Мод \"{mod.Name}\" {(mod.IsEnabled ? "включён" : "отключён")}.";
            // Refresh the list to ensure UI reflects the current state
            RefreshMods();
        }
        catch (Exception ex)
        {
            AppLogger.Error("ToggleMod failed", ex);
            MessageBox.Show($"Ошибка переключения мода:\n{ex.Message}", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    [RelayCommand]
    private void SaveProfile()
    {
        if (string.IsNullOrEmpty(GameFolderPath))
        {
            MessageBox.Show("Сначала выберите папку с игрой.", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Warning);
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
        StatusMessage = $"Профиль \"{profileName}\" сохранён.";
    }

    [RelayCommand]
    private void LoadProfile()
    {
        if (string.IsNullOrEmpty(SelectedProfile))
            return;

        var profile = _profileService.LoadProfile(SelectedProfile);
        if (profile == null)
        {
            MessageBox.Show("Не удалось загрузить профиль.", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        try
        {
            _modService.ApplyProfile(GameFolderPath, profile);
            RefreshMods();
            StatusMessage = $"Профиль \"{SelectedProfile}\" применён.";
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Ошибка применения профиля:\n{ex.Message}", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    [RelayCommand]
    private void DeleteProfile()
    {
        if (string.IsNullOrEmpty(SelectedProfile))
            return;

        var result = MessageBox.Show($"Удалить профиль \"{SelectedProfile}\"?", "Подтверждение", MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (result != MessageBoxResult.Yes)
            return;

        _profileService.DeleteProfile(SelectedProfile);
        RefreshProfiles();
        SelectedProfile = string.Empty;
        StatusMessage = "Профиль удалён.";
    }
}
