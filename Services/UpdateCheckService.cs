using System.Net.Http;
using System.Reflection;
using System.Text.Json;

namespace SevenDaysModLauncher.Services;

/// <summary>
/// Проверка новых релизов на GitHub. Тихо молчит без интернета.
/// </summary>
public sealed class UpdateCheckService
{
    private const string LatestApi = "https://api.github.com/repos/CornerWL/7daysModLauncher/releases/latest";
    private static readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(8) };

    public record UpdateInfo(bool HasUpdate, string Tag, string Url, string Current);

    public static string CurrentVersion =>
        Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "1.1.0";

    public async Task<UpdateInfo?> CheckAsync(CancellationToken token = default)
    {
        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, LatestApi);
            req.Headers.UserAgent.ParseAdd("7daysModLauncher");
            using var res = await _http.SendAsync(req, token);
            if (!res.IsSuccessStatusCode)
                return null;

            using var stream = await res.Content.ReadAsStreamAsync(token);
            using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: token);
            var root = doc.RootElement;
            var tag = root.TryGetProperty("tag_name", out var t) ? t.GetString() ?? "" : "";
            var url = root.TryGetProperty("html_url", out var u) ? u.GetString() ?? "" : "";
            if (string.IsNullOrWhiteSpace(tag))
                return null;

            var current = CurrentVersion;
            bool newer = CompareVersions(tag, current) > 0;
            AppLogger.Info($"Update check: current={current}, latest={tag}, hasUpdate={newer}");
            return new UpdateInfo(newer, tag, url, current);
        }
        catch (OperationCanceledException)
        {
            return null;
        }
        catch (Exception ex)
        {
            AppLogger.Warn($"Update check failed: {ex.Message}");
            return null;
        }
    }

    internal static int CompareVersions(string tag, string current)
    {
        static int[] Parse(string s)
        {
            s = s.Trim().TrimStart('v', 'V');
            // отрезаем суффиксы вида -beta
            var dash = s.IndexOf('-');
            if (dash >= 0)
                s = s[..dash];
            return s.Split('.').Select(p => int.TryParse(p, out var n) ? n : 0).ToArray();
        }

        var a = Parse(tag);
        var b = Parse(current);
        int len = Math.Max(a.Length, b.Length);
        for (int i = 0; i < len; i++)
        {
            int x = i < a.Length ? a[i] : 0;
            int y = i < b.Length ? b[i] : 0;
            if (x != y)
                return x.CompareTo(y);
        }
        return 0;
    }
}
