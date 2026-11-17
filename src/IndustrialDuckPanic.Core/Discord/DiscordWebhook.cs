using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace IndustrialDuckPanic.Core.Discord;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum PingMode { None, Here, Everyone, Role }

public sealed class PingSettings
{
    [JsonPropertyName("mode")] public PingMode Mode { get; set; } = PingMode.Here;
    [JsonPropertyName("roleId")] public string? RoleId { get; set; }

    public string Prefix => Mode switch
    {
        PingMode.Here => "@here ",
        PingMode.Everyone => "@everyone ",
        PingMode.Role when IsValidRoleId(RoleId) => $"<@&{RoleId}> ",
        _ => "",
    };

    public static bool IsValidRoleId(string? id) => !string.IsNullOrWhiteSpace(id) && Regex.IsMatch(id, @"^\d{15,22}$");
}

public sealed class DiscordException(string message) : Exception(message);

public static partial class WebhookUrl
{
    [GeneratedRegex(@"^https://(?:(?:ptb|canary)\.)?discord(?:app)?\.com/api(?:/v\d+)?/webhooks/\d{15,22}/[A-Za-z0-9_\-]{30,100}/?$")]
    private static partial Regex Pattern();

    public static bool IsValid(string? url) => !string.IsNullOrWhiteSpace(url) && Pattern().IsMatch(url.Trim());
}

public sealed class DiscordWebhook(HttpClient http)
{
    public static string BuildPayload(string message, PingSettings ping)
    {
        var allowed = new JsonObject();
        switch (ping.Mode)
        {
            case PingMode.Here:
            case PingMode.Everyone:
                allowed["parse"] = new JsonArray("everyone");
                break;
            case PingMode.Role when PingSettings.IsValidRoleId(ping.RoleId):
                allowed["parse"] = new JsonArray();
                allowed["roles"] = new JsonArray(ping.RoleId);
                break;
            default:
                allowed["parse"] = new JsonArray();
                break;
        }

        var content = ping.Prefix + message;
        if (content.Length > 2000) content = content[..2000];
        var payload = new JsonObject
        {
            ["username"] = AppConstants.AppName,
            ["avatar_url"] = AppConstants.DiscordAvatarUrl,
            ["content"] = content,
            ["allowed_mentions"] = allowed,
        };
        return payload.ToJsonString();
    }

    /// <summary>Posts a message. Retries once when Discord asks to slow down.</summary>
    public async Task SendAsync(string url, string message, PingSettings ping, CancellationToken ct)
    {
        if (!WebhookUrl.IsValid(url)) throw new DiscordException("The webhook URL is not a Discord webhook URL.");
        var json = BuildPayload(message, ping);
        for (var attempt = 0; attempt < 2; attempt++)
        {
            using var content = new StringContent(json, Encoding.UTF8, "application/json");
            using var resp = await http.PostAsync(url.Trim(), content, ct);
            if (resp.IsSuccessStatusCode) return;
            if (resp.StatusCode == HttpStatusCode.TooManyRequests && attempt == 0)
            {
                var wait = resp.Headers.RetryAfter?.Delta ?? TimeSpan.FromSeconds(2);
                await Task.Delay(wait < TimeSpan.FromSeconds(10) ? wait : TimeSpan.FromSeconds(10), ct);
                continue;
            }
            throw new DiscordException(resp.StatusCode switch
            {
                HttpStatusCode.NotFound or HttpStatusCode.Unauthorized => "Discord does not know this webhook anymore. Ask for a new IDP.config.",
                _ => $"Discord refused the message ({(int)resp.StatusCode}).",
            });
        }
    }

    /// <summary>Checks the webhook exists without posting anything.</summary>
    public async Task<bool> CheckAsync(string url, CancellationToken ct)
    {
        if (!WebhookUrl.IsValid(url)) return false;
        using var resp = await http.GetAsync(url.Trim(), ct);
        return resp.IsSuccessStatusCode;
    }
}
