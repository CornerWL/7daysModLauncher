using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace SevenDaysModLauncher.Services;

/// <summary>
/// OAuth 2.0 + PKCE логин через системный браузер (как у Vortex):
/// браузер → Approve на сайте → код на http://127.0.0.1/callback/ → обмен на токены.
/// Точные эндпоинты и client id выдает Nexus Support при регистрации приложения
/// (см. OAuthConfig.Defaults — заглушки с TODO).
/// </summary>
public sealed class NexusOAuthService
{
    public record OAuthConfig(
        string AuthorizeEndpoint,
        string TokenEndpoint,
        string ClientId,
        string Scopes,
        string RedirectPath = "/callback/")
    {
        /// <summary>Заглушки до ответа Nexus Support. LoginAsync без ClientId бросает понятную ошибку.</summary>
        public static OAuthConfig Defaults => new(
            AuthorizeEndpoint: "https://www.nexusmods.com/oauth/authorize", // TODO: подтвердить у Nexus Support
            TokenEndpoint: "https://api.nexusmods.com/oauth/token",          // TODO: подтвердить у Nexus Support
            ClientId: "",                                                     // TODO: выдает Nexus Support при регистрации
            Scopes: "");
    }

    public record OAuthTokens(string AccessToken, string? RefreshToken, DateTimeOffset ExpiresAt, string TokenType = "Bearer")
    {
        public bool IsExpired => DateTimeOffset.UtcNow >= ExpiresAt.AddMinutes(-2);
    }

    // ---------- PKCE (чистые функции, покрыты тестами) ----------

    public static string CreateCodeVerifier()
    {
        // 64 случайных байта -> base64url, 86 символов (RFC 7636: 43..128)
        return Base64Url(RandomNumberGenerator.GetBytes(64));
    }

    public static string CreateCodeChallenge(string verifier)
    {
        var hash = SHA256.HashData(Encoding.ASCII.GetBytes(verifier));
        return Base64Url(hash);
    }

    public static string CreateState() => Guid.NewGuid().ToString("N");

    public static string Base64Url(byte[] data)
        => Convert.ToBase64String(data).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    public static string BuildAuthorizeUrl(OAuthConfig cfg, string redirectUri, string state, string challenge)
    {
        if (string.IsNullOrWhiteSpace(cfg.ClientId))
            throw new InvalidOperationException("OAuth не настроен: нет ClientId от Nexus Support.");

        var sb = new StringBuilder(cfg.AuthorizeEndpoint);
        sb.Append(cfg.AuthorizeEndpoint.Contains('?') ? '&' : '?');
        sb.Append("response_type=code");
        sb.Append("&client_id=").Append(Uri.EscapeDataString(cfg.ClientId));
        sb.Append("&redirect_uri=").Append(Uri.EscapeDataString(redirectUri));
        sb.Append("&state=").Append(Uri.EscapeDataString(state));
        sb.Append("&code_challenge=").Append(Uri.EscapeDataString(challenge));
        sb.Append("&code_challenge_method=S256");
        if (!string.IsNullOrWhiteSpace(cfg.Scopes))
            sb.Append("&scope=").Append(Uri.EscapeDataString(cfg.Scopes));
        return sb.ToString();
    }

    public static int FindFreePort()
    {
        using var socket = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0);
        socket.Start();
        int port = ((IPEndPoint)socket.LocalEndpoint).Port;
        socket.Stop();
        return port;
    }

    // ---------- Flow ----------

    /// <summary>Полный логин: открывает браузер, ждет код на localhost, меняет на токены.</summary>
    public async Task<OAuthTokens> LoginAsync(OAuthConfig cfg, CancellationToken token = default)
    {
        var verifier = CreateCodeVerifier();
        var challenge = CreateCodeChallenge(verifier);
        var state = CreateState();
        int port = FindFreePort();
        string redirectUri = $"http://127.0.0.1:{port}{cfg.RedirectPath}";
        string authUrl = BuildAuthorizeUrl(cfg, redirectUri, state, challenge);

        using var listener = new HttpListener();
        listener.Prefixes.Add($"http://127.0.0.1:{port}{cfg.RedirectPath}");
        listener.Start();
        AppLogger.Info($"OAuth: listening on {redirectUri}");

        try
        {
            Process.Start(new ProcessStartInfo { FileName = authUrl, UseShellExecute = true });

            using var timeoutCts = new CancellationTokenSource(TimeSpan.FromMinutes(5));
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(token, timeoutCts.Token);
            var context = await listener.GetContextAsync().WaitAsync(linked.Token);

            string? code = context.Request.QueryString["code"];
            string? returnedState = context.Request.QueryString["state"];
            string? error = context.Request.QueryString["error"];

            // Сразу отвечаем браузеру, чтобы не висел
            const string okHtml = "<html><body style='background:#0B0E14;color:#E8EDF7;font-family:sans-serif'><h3>OK — вернитесь в 7daysModLauncher</h3></body></html>";
            byte[] bytes = Encoding.UTF8.GetBytes(error != null
                ? okHtml.Replace("OK — вернитесь в 7daysModLauncher", "Ошибка: " + error)
                : okHtml);
            context.Response.ContentType = "text/html; charset=utf-8";
            await context.Response.OutputStream.WriteAsync(bytes, token);
            context.Response.Close();

            if (!string.IsNullOrEmpty(error))
                throw new InvalidOperationException($"OAuth отклонен сайтом: {error}");
            if (string.IsNullOrEmpty(code))
                throw new InvalidOperationException("OAuth: сайт не вернул код.");
            if (!string.Equals(returnedState, state, StringComparison.Ordinal))
                throw new InvalidOperationException("OAuth: state не совпал (возможна подмена).");

            return await ExchangeCodeAsync(cfg, code, redirectUri, verifier, token);
        }
        finally
        {
            listener.Stop();
        }
    }

    public async Task<OAuthTokens> ExchangeCodeAsync(OAuthConfig cfg, string code, string redirectUri, string verifier, CancellationToken token = default)
    {
        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        using var content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "authorization_code",
            ["code"] = code,
            ["redirect_uri"] = redirectUri,
            ["client_id"] = cfg.ClientId,
            ["code_verifier"] = verifier,
        });
        using var res = await client.PostAsync(cfg.TokenEndpoint, content, token);
        var body = await res.Content.ReadAsStringAsync(token);
        if (!res.IsSuccessStatusCode)
        {
            AppLogger.Warn($"OAuth token exchange {(int)res.StatusCode}: {body[..Math.Min(300, body.Length)]}");
            throw new InvalidOperationException($"Обмен кода на токен не удался ({(int)res.StatusCode}).");
        }
        return ParseTokens(body);
    }

    public async Task<OAuthTokens?> RefreshAsync(OAuthConfig cfg, string refreshToken, CancellationToken token = default)
    {
        try
        {
            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
            using var content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["grant_type"] = "refresh_token",
                ["refresh_token"] = refreshToken,
                ["client_id"] = cfg.ClientId,
            });
            using var res = await client.PostAsync(cfg.TokenEndpoint, content, token);
            if (!res.IsSuccessStatusCode)
                return null;
            var body = await res.Content.ReadAsStringAsync(token);
            return ParseTokens(body);
        }
        catch (Exception ex)
        {
            AppLogger.Warn($"OAuth refresh failed: {ex.Message}");
            return null;
        }
    }

    internal static OAuthTokens ParseTokens(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        string? Get(params string[] names)
        {
            foreach (var n in names)
                if (root.TryGetProperty(n, out var v) && v.ValueKind == JsonValueKind.String)
                    return v.GetString();
            return null;
        }
        var access = Get("access_token", "accessToken") ?? throw new InvalidOperationException("В ответе нет access_token.");
        var refresh = Get("refresh_token", "refreshToken");
        var type = Get("token_type", "tokenType") ?? "Bearer";
        double expiresIn = 3600;
        if (root.TryGetProperty("expires_in", out var e) && e.ValueKind == JsonValueKind.Number && e.TryGetDouble(out var d))
            expiresIn = d;
        return new OAuthTokens(access, refresh, DateTimeOffset.UtcNow.AddSeconds(expiresIn), type);
    }
}
