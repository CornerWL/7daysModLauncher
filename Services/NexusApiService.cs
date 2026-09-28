using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace SevenDaysModLauncher.Services;

/// <summary>
/// Клиент Nexus Mods v1 REST API. Домен игры 7 Days to Die: "7daystodie".
/// Авторизация: личный API-ключ юзера (заголовок apikey) или OAuth Bearer-токен.
/// Скачивание прямых ссылок через API доступно только Premium (правило Nexus,
/// free-аккаунты получают 403) — этот случай обрабатывается понятным текстом.
/// </summary>
public sealed class NexusApiService
{
    public const string GameDomain = "7daystodie";
    private const string ApiBase = "https://api.nexusmods.com";

    public record NexusUser(string Name, bool IsPremium, bool IsSupporter);
    public record NexusModInfo(int ModId, string Name, string Summary, string Author, string Version);
    public record NexusModFile(int FileId, string Name, string FileName, string Version, long Size, string? Uploaded);

    /// <summary>Учетка для API: либо ключ, либо OAuth-токен (токен приоритетнее).</summary>
    public record NexusCredential(string? ApiKey, string? BearerToken)
    {
        public bool IsEmpty => string.IsNullOrWhiteSpace(ApiKey) && string.IsNullOrWhiteSpace(BearerToken);
    }

    private static HttpClient CreateClient(NexusCredential cred, TimeSpan? timeout = null)
    {
        var client = new HttpClient { Timeout = timeout ?? TimeSpan.FromSeconds(30) };
        if (!string.IsNullOrWhiteSpace(cred.BearerToken))
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", cred.BearerToken!.Trim());
        else if (!string.IsNullOrWhiteSpace(cred.ApiKey))
            client.DefaultRequestHeaders.Add("apikey", cred.ApiKey!.Trim());
        client.DefaultRequestHeaders.Add("Application-Name", "7daysModLauncher");
        client.DefaultRequestHeaders.Add("Application-Version", UpdateCheckService.CurrentVersion);
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        client.DefaultRequestHeaders.UserAgent.ParseAdd("7daysModLauncher");
        return client;
    }

    /// <summary>Принимает полную ссылку, голый id или "10784". Возвращает mod id.</summary>
    public static int? ParseModId(string? input)
    {
        if (string.IsNullOrWhiteSpace(input))
            return null;
        input = input.Trim();

        if (int.TryParse(input, out var direct))
            return direct;

        // https://www.nexusmods.com/7daystodie/mods/10784?...
        var m = Regex.Match(input, @"nexusmods\.com/(?<domain>[a-z0-9_]+)/mods/(?<id>\d+)", RegexOptions.IgnoreCase);
        if (m.Success)
            return int.Parse(m.Groups["id"].Value);

        // nxm://7daystodie/mods/10784/files/...
        m = Regex.Match(input, @"^nxm://[^/]+/mods/(?<id>\d+)", RegexOptions.IgnoreCase);
        if (m.Success)
            return int.Parse(m.Groups["id"].Value);

        return null;
    }

    public static string? ParseGameDomain(string? input)
    {
        if (string.IsNullOrWhiteSpace(input))
            return null;
        var m = Regex.Match(input.Trim(), @"nexusmods\.com/(?<domain>[a-z0-9_]+)/mods/\d+", RegexOptions.IgnoreCase);
        if (m.Success)
            return m.Groups["domain"].Value.ToLowerInvariant();
        // nxm://domain/mods/...
        m = Regex.Match(input.Trim(), @"^nxm://(?<domain>[^/]+)/mods/\d+", RegexOptions.IgnoreCase);
        return m.Success ? m.Groups["domain"].Value.ToLowerInvariant() : null;
    }

    /// <summary>
    /// Разбор NXM-ссылки вида nxm://domain/mods/10784/files/123?key=XXX&expires=YYY.
    /// key/expires — токены клика, по ним бесплатным юзерам тоже отдают ссылку.
    /// </summary>
    public record NxmLink(string Domain, int ModId, int? FileId, string? Key, string? Expires);

    public static NxmLink? ParseNxmLink(string? input)
    {
        if (string.IsNullOrWhiteSpace(input) || !input.TrimStart().StartsWith("nxm://", StringComparison.OrdinalIgnoreCase))
            return null;

        var m = Regex.Match(input.Trim(),
            @"^nxm://(?<domain>[^/]+)/mods/(?<mod>\d+)(/files/(?<file>\d+))?",
            RegexOptions.IgnoreCase);
        if (!m.Success || !int.TryParse(m.Groups["mod"].Value, out var modId))
            return null;

        int? fileId = int.TryParse(m.Groups["file"].Value, out var f) ? f : null;

        string? key = null, expires = null;
        var q = input.IndexOf('?');
        if (q >= 0)
        {
            foreach (var pair in input[(q + 1)..].Split('&', StringSplitOptions.RemoveEmptyEntries))
            {
                var kv = pair.Split('=', 2);
                if (kv.Length != 2)
                    continue;
                var name = Uri.UnescapeDataString(kv[0]).Trim().ToLowerInvariant();
                var val = Uri.UnescapeDataString(kv[1]).Trim();
                if (name == "key") key = val;
                else if (name == "expires") expires = val;
            }
        }

        return new NxmLink(m.Groups["domain"].Value.ToLowerInvariant(), modId, fileId, key, expires);
    }

    public async Task<(bool Ok, string Message, NexusUser? User)> ValidateAsync(NexusCredential cred, CancellationToken token = default)
    {
        if (cred.IsEmpty)
            return (false, "Log in via Nexus or paste an API key.", null);

        try
        {
            using var client = CreateClient(cred);
            using var res = await client.GetAsync($"{ApiBase}/v1/users/validate.json", token);
            if (res.StatusCode == HttpStatusCode.Unauthorized || res.StatusCode == HttpStatusCode.Forbidden)
                return (false, "Credentials rejected (401/403).", null);
            if (res.StatusCode == (HttpStatusCode)429)
                return (false, "API rate limit hit (429). Wait a bit.", null);
            if (!res.IsSuccessStatusCode)
                return (false, $"Nexus returned {(int)res.StatusCode}. Try again later.", null);

            await using var stream = await res.Content.ReadAsStreamAsync(token);
            using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: token);
            var root = doc.RootElement;
            var name = root.TryGetProperty("name", out var n) ? n.GetString() ?? "?" : "?";
            var premium = root.TryGetProperty("is_premium", out var p) && p.ValueKind == JsonValueKind.True;
            var supporter = root.TryGetProperty("is_supporter", out var s) && s.ValueKind == JsonValueKind.True;
            return (true, premium ? $"Logged in: {name} (Premium)" : $"Logged in: {name}", new NexusUser(name, premium, supporter));
        }
        catch (OperationCanceledException)
        {
            return (false, "Timeout. Check your connection.", null);
        }
        catch (Exception ex)
        {
            AppLogger.Warn($"Nexus validate failed: {ex.Message}");
            return (false, $"Network error: {ex.Message}", null);
        }
    }

    public async Task<(NexusModInfo? Mod, string? Error)> GetModAsync(NexusCredential cred, int modId, CancellationToken token = default)
    {
        try
        {
            using var client = CreateClient(cred);
            using var res = await client.GetAsync($"{ApiBase}/v1/games/{GameDomain}/mods/{modId}.json", token);
            if (res.StatusCode == HttpStatusCode.NotFound)
                return (null, "Mod not found.");
            if (res.StatusCode == HttpStatusCode.Unauthorized || res.StatusCode == HttpStatusCode.Forbidden)
                return (null, $"Nexus denied ({(int)res.StatusCode}). Check your login.");
            if (res.StatusCode == (HttpStatusCode)429)
                return (null, "API rate limit hit (429). Wait a bit.");
            res.EnsureSuccessStatusCode();

            await using var stream = await res.Content.ReadAsStreamAsync(token);
            using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: token);
            var root = doc.RootElement;
            string S(string prop) => root.TryGetProperty(prop, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";
            return (new NexusModInfo(modId,
                string.IsNullOrWhiteSpace(S("name")) ? $"mod {modId}" : S("name"),
                S("summary"), S("author"), S("version")), null);
        }
        catch (Exception ex)
        {
            AppLogger.Warn($"Nexus GetMod failed: {ex.Message}");
            return (null, $"Error: {ex.Message}");
        }
    }

    public async Task<(List<NexusModFile> Files, string? Error)> GetFilesAsync(NexusCredential cred, int modId, CancellationToken token = default)
    {
        try
        {
            using var client = CreateClient(cred);
            using var res = await client.GetAsync($"{ApiBase}/v1/games/{GameDomain}/mods/{modId}/files.json", token);
            if (res.StatusCode == HttpStatusCode.NotFound)
                return (new(), "Files not found.");
            if (res.StatusCode == HttpStatusCode.Unauthorized || res.StatusCode == HttpStatusCode.Forbidden)
                return (new(), $"Nexus denied ({(int)res.StatusCode}). Check your login.");
            if (res.StatusCode == (HttpStatusCode)429)
                return (new(), "API rate limit hit (429). Wait a bit.");
            res.EnsureSuccessStatusCode();

            await using var stream = await res.Content.ReadAsStreamAsync(token);
            using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: token);
            var list = new List<NexusModFile>();
            if (doc.RootElement.TryGetProperty("files", out var files) && files.ValueKind == JsonValueKind.Array)
            {
                foreach (var f in files.EnumerateArray())
                {
                    int fileId = 0;
                    if (f.TryGetProperty("file_id", out var fid) && fid.ValueKind == JsonValueKind.Number)
                        fileId = fid.GetInt32();
                    if (fileId == 0)
                        continue;
                    string G(string p) => f.TryGetProperty(p, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";
                    long size = 0;
                    if (f.TryGetProperty("size", out var sz) && sz.ValueKind == JsonValueKind.Number && sz.TryGetInt64(out var s))
                        size = s;
                    string? uploaded = f.TryGetProperty("uploaded_timestamp", out var ts) && ts.ValueKind == JsonValueKind.Number
                        ? DateTimeOffset.FromUnixTimeSeconds(ts.GetInt64()).LocalDateTime.ToString("dd.MM.yyyy")
                        : null;
                    list.Add(new NexusModFile(fileId, G("name"), G("file_name"), G("version"), size, uploaded));
                }
            }
            list.Sort((a, b) => b.FileId.CompareTo(a.FileId));
            return (list, null);
        }
        catch (Exception ex)
        {
            AppLogger.Warn($"Nexus GetFiles failed: {ex.Message}");
            return (new(), $"Error: {ex.Message}");
        }
    }

    public Task<(List<string> Uris, string? Error)> GetDownloadLinksAsync(NexusCredential cred, int modId, int fileId, CancellationToken token = default)
        => GetDownloadLinksAsync(cred, modId, fileId, null, null, token);

    /// <summary>
    /// nxmKey/nxmExpires — токены из NXM-ссылки (клик Mod Manager Download на сайте).
    /// С ними ссылку отдают и бесплатным аккаунтам.
    /// </summary>
    public async Task<(List<string> Uris, string? Error)> GetDownloadLinksAsync(
        NexusCredential cred, int modId, int fileId, string? nxmKey, string? nxmExpires, CancellationToken token = default)
    {
        try
        {
            var url = $"{ApiBase}/v1/games/{GameDomain}/mods/{modId}/files/{fileId}/download_link.json";
            if (!string.IsNullOrWhiteSpace(nxmKey) && !string.IsNullOrWhiteSpace(nxmExpires))
                url += $"?key={Uri.EscapeDataString(nxmKey)}&expires={Uri.EscapeDataString(nxmExpires)}";

            using var client = CreateClient(cred);
            using var res = await client.GetAsync(url, token);
            if (res.StatusCode == HttpStatusCode.Unauthorized || res.StatusCode == HttpStatusCode.Forbidden)
            {
                var body = await SafeReadBody(res);
                AppLogger.Warn($"Nexus download_link {(int)res.StatusCode}: {body}");
                bool viaNxm = !string.IsNullOrWhiteSpace(nxmKey);
                return (new(), viaNxm
                    ? "Nexus refused the link. Click Mod Manager Download on the site again (tokens expire) or download manually."
                    : "API download is Premium-only. Either click Mod Manager Download on the site (NXM links work free too) or download the ZIP manually.");
            }
            if (res.StatusCode == HttpStatusCode.NotFound)
                return (new(), "File not found (404).");
            if (res.StatusCode == (HttpStatusCode)429)
            {
                var retry = res.Headers.RetryAfter?.Delta?.TotalSeconds;
                return (new(), retry.HasValue
                    ? $"API rate limit hit (429). Retry in {retry.Value:F0} sec."
                    : "API rate limit hit (429). Wait a bit.");
            }
            if (!res.IsSuccessStatusCode)
            {
                var body = await SafeReadBody(res);
                AppLogger.Warn($"Nexus download_link {(int)res.StatusCode}: {body}");
                return (new(), $"Nexus returned {(int)res.StatusCode}. See the log for details.");
            }

            await using var stream = await res.Content.ReadAsStreamAsync(token);
            using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: token);
            var uris = new List<string>();
            if (doc.RootElement.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in doc.RootElement.EnumerateArray())
                {
                    if (item.TryGetProperty("URI", out var u) && u.GetString() is { } uri && !string.IsNullOrWhiteSpace(uri))
                        uris.Add(uri);
                }
            }
            if (uris.Count == 0)
                AppLogger.Warn($"Nexus download_link: пустой список (mod={modId}, file={fileId})");
            return uris.Count == 0 ? (new(), "Server gave no download link.") : (uris, null);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            AppLogger.Warn($"Nexus GetDownloadLinks failed: {ex.Message}");
            return (new(), $"Error: {ex.Message}");
        }
    }

    /// <summary>Качает файл (CDN-ссылка уже авторизована). Возвращает путь.</summary>
    public async Task<string> DownloadFileAsync(string uri, string destPath, IProgress<double>? progress, CancellationToken token)
    {
        using var client = new HttpClient { Timeout = Timeout.InfiniteTimeSpan };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("7daysModLauncher");
        using var res = await client.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, token);
        res.EnsureSuccessStatusCode();

        var total = res.Content.Headers.ContentLength ?? -1L;
        await using var net = await res.Content.ReadAsStreamAsync(token);
        await using var file = File.Create(destPath);

        var buffer = new byte[81920];
        long done = 0;
        int read;
        while ((read = await net.ReadAsync(buffer, token)) > 0)
        {
            await file.WriteAsync(buffer.AsMemory(0, read), token);
            done += read;
            if (total > 0)
                progress?.Report((double)done / total);
        }
        progress?.Report(1.0);
        AppLogger.Info($"Downloaded {done} bytes to '{destPath}'");
        return destPath;
    }

    private static async Task<string> SafeReadBody(HttpResponseMessage res)
    {
        try
        {
            var body = await res.Content.ReadAsStringAsync();
            return body.Length > 500 ? body[..500] : body;
        }
        catch
        {
            return "<no body>";
        }
    }
}
