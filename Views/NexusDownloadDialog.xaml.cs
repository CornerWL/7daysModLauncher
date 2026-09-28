using System.IO;
using System.Text.Json;
using System.Windows;
using MessageBox = System.Windows.MessageBox;
using SevenDaysModLauncher.Models;
using SevenDaysModLauncher.Services;

namespace SevenDaysModLauncher.Views;

/// <summary>
/// Nexus-флоу: OAuth-логин или личный ключ → поиск мода → файлы →
/// скачивание/установка + сверка версии с уже установленным.
/// Возвращает пути скачанных ZIP через <see cref="DownloadedZips"/>.
/// </summary>
public partial class NexusDownloadDialog : Window
{
    private readonly SettingsService _settings = new();
    private readonly NexusApiService _api = new();
    private readonly NexusOAuthService _oauth = new();
    private CancellationTokenSource? _cts;

    public List<string> DownloadedZips { get; } = new();
    public IReadOnlyList<ModItem> InstalledMods { get; set; } = Array.Empty<ModItem>();

    private NexusApiService.NexusCredential _cred = new(null, null);
    private int _currentModId;
    private string _currentModVersion = "";

    public NexusDownloadDialog()
    {
        InitializeComponent();
        Loaded += (_, _) => RestoreAuth();
    }

    // ---------- Auth ----------

    private void RestoreAuth()
    {
        try
        {
            var s = _settings.Load();
            if (!string.IsNullOrWhiteSpace(s.NexusApiKey))
                ApiKeyBox.Text = s.NexusApiKey;

            var tokens = LoadTokens(s);
            if (tokens != null && !tokens.IsExpired)
            {
                _cred = new NexusApiService.NexusCredential(null, tokens.AccessToken);
                AuthStatusText.Text = "OAuth: вход выполнен.";
                _ = ValidateAsync();
                return;
            }
            if (!string.IsNullOrWhiteSpace(s.NexusApiKey))
                _ = ValidateAsync();
        }
        catch (Exception ex)
        {
            AppLogger.Warn($"Nexus restore auth failed: {ex.Message}");
        }
    }

    private NexusOAuthService.OAuthTokens? LoadTokens(AppSettings s)
    {
        try
        {
            return string.IsNullOrWhiteSpace(s.NexusOAuth)
                ? null
                : JsonSerializer.Deserialize<NexusOAuthService.OAuthTokens>(s.NexusOAuth);
        }
        catch
        {
            return null;
        }
    }

    private void SaveAuth(string? apiKey, NexusOAuthService.OAuthTokens? tokens)
    {
        try
        {
            var s = _settings.Load();
            if (apiKey != null)
                s.NexusApiKey = apiKey;
            if (tokens != null)
                s.NexusOAuth = JsonSerializer.Serialize(tokens);
            _settings.Save(s);
        }
        catch (Exception ex)
        {
            AppLogger.Warn($"Save Nexus auth failed: {ex.Message}");
        }
    }

    private async Task ValidateAsync()
    {
        var (ok, message, _) = await _api.ValidateAsync(_cred);
        AuthStatusText.Text = message;
        AppLogger.Info($"Nexus validate: {message}");
    }

    private async void ValidateButton_Click(object sender, RoutedEventArgs e)
    {
        _cred = new NexusApiService.NexusCredential(ApiKeyBox.Text.Trim(), null);
        AuthStatusText.Text = "Проверка...";
        await ValidateAsync();
        var (ok, _, _) = await _api.ValidateAsync(_cred);
        if (ok)
            SaveAuth(ApiKeyBox.Text.Trim(), null);
    }

    private async void OAuthButton_Click(object sender, RoutedEventArgs e)
    {
        _cts?.Cancel();
        _cts = new CancellationTokenSource();
        var token = _cts.Token;
        SetBusy(true, "Ожидание входа в браузере...");
        try
        {
            var cfg = NexusOAuthService.OAuthConfig.Defaults;
            var tokens = await _oauth.LoginAsync(cfg, token);
            _cred = new NexusApiService.NexusCredential(null, tokens.AccessToken);
            SaveAuth(null, tokens);
            await ValidateAsync();
        }
        catch (InvalidOperationException ex)
        {
            // Нет ClientId — честно показываем, ждем данные от Nexus Support
            AuthStatusText.Text = ex.Message;
            MessageBox.Show($"{ex.Message}\n\nФолбэк: вставьте личный API-ключ (My Account → API Access).",
                "OAuth не настроен", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (OperationCanceledException)
        {
            AuthStatusText.Text = "Вход отменен.";
        }
        catch (Exception ex)
        {
            AppLogger.Error("OAuth login failed", ex);
            AuthStatusText.Text = $"Ошибка входа: {ex.Message}";
        }
        finally
        {
            SetBusy(false);
        }
    }

    // ---------- Mod/files ----------

    private void SetBusy(bool busy, string? status = null)
    {
        ValidateButton.IsEnabled = !busy;
        OAuthButton.IsEnabled = !busy;
        FindButton.IsEnabled = !busy;
        DownloadButton.IsEnabled = !busy;
        OpenSiteButton.IsEnabled = !busy;
        ProgressPanel.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
        if (status != null)
            DownloadStatusText.Text = status;
        if (busy)
            DownloadProgress.Value = 0;
    }

    private async void FindButton_Click(object sender, RoutedEventArgs e)
    {
        if (_cred.IsEmpty)
        {
            AuthStatusText.Text = "Сначала войдите через Nexus или вставьте API-ключ.";
            return;
        }

        var modId = NexusApiService.ParseModId(ModLinkBox.Text);
        if (modId == null)
        {
            MessageBox.Show("Не понял ссылку. Вставьте ссылку вида\nhttps://www.nexusmods.com/7daystodie/mods/10784\nили просто id мода.",
                "Ссылка", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var domain = NexusApiService.ParseGameDomain(ModLinkBox.Text);
        if (domain != null && domain != NexusApiService.GameDomain)
        {
            MessageBox.Show($"Это мод для другой игры ({domain}). Лаунчер работает с 7 Days to Die.",
                "Другая игра", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        _cts?.Cancel();
        _cts = new CancellationTokenSource();
        var token = _cts.Token;
        SetBusy(true, "Запрос к Nexus...");
        try
        {
            var (mod, modError) = await _api.GetModAsync(_cred, modId.Value, token);
            if (mod == null)
            {
                DownloadStatusText.Text = modError ?? "Ошибка.";
                return;
            }

            _currentModId = modId.Value;
            _currentModVersion = mod.Version;
            ModTitleText.Text = mod.Name;
            ModAuthorText.Text = string.IsNullOrWhiteSpace(mod.Author) ? "" : $"by {mod.Author} • v{mod.Version}";
            ShowInstalledMatch(mod);

            DownloadStatusText.Text = "Загрузка списка файлов...";
            var (files, filesError) = await _api.GetFilesAsync(_cred, modId.Value, token);
            if (filesError != null)
            {
                DownloadStatusText.Text = filesError;
                return;
            }

            FilesList.ItemsSource = files.Select(f => new FileRow(f)).ToList();
            if (FilesList.Items.Count > 0)
                FilesList.SelectedIndex = 0;
            DownloadStatusText.Text = files.Count == 0 ? "У мода нет файлов." : $"Файлов: {files.Count}. Выберите и жмите Download.";
        }
        catch (OperationCanceledException)
        {
            DownloadStatusText.Text = "Отменено.";
        }
        catch (Exception ex)
        {
            AppLogger.Error("Nexus find failed", ex);
            DownloadStatusText.Text = $"Ошибка: {ex.Message}";
        }
        finally
        {
            SetBusy(false);
        }
    }

    /// <summary>Сверка версии Nexus с уже установленным модом (матч по папке/имени).</summary>
    private void ShowInstalledMatch(NexusApiService.NexusModInfo mod)
    {
        try
        {
            var match = InstalledMods.FirstOrDefault(m =>
                    (!string.IsNullOrWhiteSpace(m.FolderName) && mod.Name.Contains(m.FolderName, StringComparison.OrdinalIgnoreCase)) ||
                    (!string.IsNullOrWhiteSpace(m.Name) && mod.Name.Equals(m.Name, StringComparison.OrdinalIgnoreCase)))
                ?? InstalledMods.FirstOrDefault(m =>
                    !string.IsNullOrWhiteSpace(m.FolderName) &&
                    m.FolderName.Contains(mod.Name, StringComparison.OrdinalIgnoreCase));

            if (match == null)
            {
                MatchText.Text = "Не установлен. Можно качать.";
                return;
            }

            int cmp = UpdateCheckService.CompareVersions(mod.Version, match.Version ?? "");
            MatchText.Text = cmp > 0
                ? $"Установлен {match.FolderName} v{match.Version ?? "?"} — на Nexus новее (v{mod.Version}). Есть обновление!"
                : $"Установлен {match.FolderName} v{match.Version ?? "?"} — актуально.";
        }
        catch (Exception ex)
        {
            AppLogger.Warn($"Match failed: {ex.Message}");
            MatchText.Text = "";
        }
    }

    private async void DownloadButton_Click(object sender, RoutedEventArgs e)
    {
        if (FilesList.SelectedItem is not FileRow row)
        {
            MessageBox.Show("Выберите файл из списка.", "Файл", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        if (_cred.IsEmpty || _currentModId == 0)
            return;

        _cts?.Cancel();
        _cts = new CancellationTokenSource();
        var token = _cts.Token;
        SetBusy(true, "Получение ссылки...");
        try
        {
            var progress = new Progress<double>(v => DownloadProgress.Value = v);
            var (uris, linkError) = await _api.GetDownloadLinksAsync(_cred, _currentModId, row.File.FileId, token);
            if (linkError != null || uris.Count == 0)
            {
                DownloadStatusText.Text = linkError ?? "Нет ссылки.";
                return;
            }

            var safeName = string.Join("_", (string.IsNullOrWhiteSpace(row.File.FileName) ? $"nexus_{row.File.FileId}.zip" : row.File.FileName).Split(Path.GetInvalidFileNameChars()));
            var dest = Path.Combine(Path.GetTempPath(), $"nexus_{_currentModId}_{row.File.FileId}_{safeName}");
            DownloadStatusText.Text = $"Качаю {safeName}...";
            await _api.DownloadFileAsync(uris[0], dest, progress, token);

            DownloadedZips.Add(dest);
            DialogResult = true;
            Close();
        }
        catch (OperationCanceledException)
        {
            DownloadStatusText.Text = "Отменено.";
        }
        catch (Exception ex)
        {
            AppLogger.Error("Nexus download failed", ex);
            MessageBox.Show($"Ошибка скачивания:\n{ex.Message}", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
            DownloadStatusText.Text = "Ошибка скачивания.";
        }
        finally
        {
            if (DialogResult != true)
                SetBusy(false);
        }
    }

    private void OpenSiteButton_Click(object sender, RoutedEventArgs e)
    {
        var url = _currentModId != 0
            ? $"https://www.nexusmods.com/{NexusApiService.GameDomain}/mods/{_currentModId}?tab=files"
            : "https://www.nexusmods.com/7daystodie";
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo { FileName = url, UseShellExecute = true });
        }
        catch (Exception ex)
        {
            AppLogger.Warn($"Open site failed: {ex.Message}");
        }
    }

    public sealed class FileRow
    {
        public NexusApiService.NexusModFile File { get; }
        public FileRow(NexusApiService.NexusModFile file) => File = file;
        public string DisplayName => string.IsNullOrWhiteSpace(File.Name) ? File.FileName : File.Name;
        public string DisplayMeta => string.Join(" • ", new[] { File.Version, File.Uploaded }.Where(s => !string.IsNullOrWhiteSpace(s)));
        public string DisplaySize => File.Size > 0 ? FormatSize(File.Size) : "";

        private static string FormatSize(long bytes)
        {
            if (bytes < 1024) return $"{bytes} B";
            if (bytes < 1024 * 1024) return $"{bytes / 1024.0:F0} KB";
            if (bytes < 1024L * 1024 * 1024) return $"{bytes / (1024.0 * 1024):F1} MB";
            return $"{bytes / (1024.0 * 1024 * 1024):F2} GB";
        }
    }
}
