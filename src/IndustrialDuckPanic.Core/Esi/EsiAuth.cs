using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace IndustrialDuckPanic.Core.Esi;

/// <summary>Tokens returned by the EVE SSO.</summary>
public sealed record EsiTokens(string AccessToken, string RefreshToken, DateTimeOffset ExpiresAt, int CharacterId, string CharacterName)
{
    public bool IsExpiring(DateTimeOffset now) => now >= ExpiresAt - TimeSpan.FromSeconds(60);
}

public static class Pkce
{
    public static string Base64Url(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    public static string CreateVerifier() => Base64Url(RandomNumberGenerator.GetBytes(32));

    public static string ChallengeFor(string verifier) => Base64Url(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));
}

public static class JwtReader
{
    /// <summary>Reads character id and name from an EVE SSO access token (sub = "CHARACTER:EVE:&lt;id&gt;").</summary>
    public static (int CharacterId, string Name, DateTimeOffset Expires) Read(string jwt)
    {
        var parts = jwt.Split('.');
        if (parts.Length < 2) throw new FormatException("Invalid access token.");
        var payload = parts[1].Replace('-', '+').Replace('_', '/');
        payload = payload.PadRight(payload.Length + (4 - payload.Length % 4) % 4, '=');
        using var doc = JsonDocument.Parse(Convert.FromBase64String(payload));
        var root = doc.RootElement;
        var sub = root.GetProperty("sub").GetString() ?? "";
        var idText = sub.Split(':').LastOrDefault() ?? "";
        if (!int.TryParse(idText, out var id)) throw new FormatException("Access token has no character id.");
        var name = root.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "";
        var exp = root.TryGetProperty("exp", out var e) ? DateTimeOffset.FromUnixTimeSeconds(e.GetInt64()) : DateTimeOffset.UtcNow.AddMinutes(19);
        return (id, name, exp);
    }
}

/// <summary>EVE SSO v2 with PKCE and a local loopback listener.</summary>
public sealed class EsiAuth(HttpClient http)
{
    private const string AuthorizeUrl = "https://login.eveonline.com/v2/oauth/authorize";
    private const string TokenUrl = "https://login.eveonline.com/v2/oauth/token";

    public static string BuildAuthorizeUrl(string state, string challenge) =>
        AuthorizeUrl +
        "?response_type=code" +
        "&redirect_uri=" + Uri.EscapeDataString(AppConstants.CallbackUrl) +
        "&client_id=" + AppConstants.EsiClientId +
        "&scope=" + Uri.EscapeDataString(string.Join(' ', AppConstants.EsiScopes)) +
        "&code_challenge=" + challenge +
        "&code_challenge_method=S256" +
        "&state=" + state;

    /// <summary>Opens the browser through <paramref name="openBrowser"/> and waits for the SSO callback.</summary>
    public async Task<EsiTokens> LoginAsync(Action<string> openBrowser, CancellationToken ct)
    {
        var verifier = Pkce.CreateVerifier();
        var state = Pkce.Base64Url(RandomNumberGenerator.GetBytes(16));

        using var listener = new HttpListener();
        listener.Prefixes.Add($"http://localhost:{AppConstants.CallbackPort}/");
        listener.Start();

        openBrowser(BuildAuthorizeUrl(state, Pkce.ChallengeFor(verifier)));

        using var reg = ct.Register(() => { try { listener.Stop(); } catch { /* already stopped */ } });
        while (true)
        {
            HttpListenerContext ctx;
            try { ctx = await listener.GetContextAsync(); }
            catch (Exception) when (ct.IsCancellationRequested) { throw new OperationCanceledException(ct); }

            if (ctx.Request.Url?.AbsolutePath != "/callback")
            {
                ctx.Response.StatusCode = 404;
                ctx.Response.Close();
                continue;
            }

            var code = ctx.Request.QueryString["code"];
            var returnedState = ctx.Request.QueryString["state"];
            var ok = !string.IsNullOrEmpty(code) && returnedState == state;
            await WriteBrowserPage(ctx.Response, ok);
            if (!ok) throw new InvalidOperationException("EVE login was refused or the answer was invalid. Try again.");

            var form = new Dictionary<string, string>
            {
                ["grant_type"] = "authorization_code",
                ["code"] = code!,
                ["client_id"] = AppConstants.EsiClientId,
                ["code_verifier"] = verifier,
            };
            return await RequestTokens(form, ct);
        }
    }

    public Task<EsiTokens> RefreshAsync(string refreshToken, CancellationToken ct) =>
        RequestTokens(new Dictionary<string, string>
        {
            ["grant_type"] = "refresh_token",
            ["refresh_token"] = refreshToken,
            ["client_id"] = AppConstants.EsiClientId,
        }, ct);

    private async Task<EsiTokens> RequestTokens(Dictionary<string, string> form, CancellationToken ct)
    {
        using var req = new HttpRequestMessage(HttpMethod.Post, TokenUrl) { Content = new FormUrlEncodedContent(form) };
        req.Headers.Host = "login.eveonline.com";
        using var resp = await http.SendAsync(req, ct);
        var body = await resp.Content.ReadAsStringAsync(ct);
        if (!resp.IsSuccessStatusCode)
            throw new EsiAuthException($"EVE login failed ({(int)resp.StatusCode}).", (int)resp.StatusCode);

        var token = JsonSerializer.Deserialize<TokenResponse>(body) ?? throw new EsiAuthException("Empty answer from EVE login.", 0);
        var (id, name, exp) = JwtReader.Read(token.AccessToken);
        return new EsiTokens(token.AccessToken, token.RefreshToken ?? form.GetValueOrDefault("refresh_token", ""), exp, id, name);
    }

    private static async Task WriteBrowserPage(HttpListenerResponse response, bool ok)
    {
        var title = ok ? "Character connected" : "Login failed";
        var text = ok ? "You can close this tab and go back to Industrial Duck Panic." : "Go back to Industrial Duck Panic and try again.";
        var html = $"<!doctype html><meta charset=utf-8><title>{title}</title>" +
                   "<body style=\"background:#100f0d;color:#efe6d6;font-family:Segoe UI,sans-serif;display:grid;place-items:center;height:100vh;margin:0\">" +
                   $"<div style=\"text-align:center\"><h1 style=\"color:#f0a020\">{title}</h1><p>{text}</p></div></body>";
        var bytes = Encoding.UTF8.GetBytes(html);
        response.ContentType = "text/html; charset=utf-8";
        response.ContentLength64 = bytes.Length;
        await response.OutputStream.WriteAsync(bytes);
        response.Close();
    }

    private sealed class TokenResponse
    {
        [JsonPropertyName("access_token")] public string AccessToken { get; set; } = "";
        [JsonPropertyName("refresh_token")] public string? RefreshToken { get; set; }
        [JsonPropertyName("expires_in")] public int ExpiresIn { get; set; }
    }
}

public sealed class EsiAuthException(string message, int statusCode) : Exception(message)
{
    public int StatusCode { get; } = statusCode;
    /// <summary>The refresh token is no longer valid; the user must log in again.</summary>
    public bool IsInvalidGrant => StatusCode is 400 or 401;
}
