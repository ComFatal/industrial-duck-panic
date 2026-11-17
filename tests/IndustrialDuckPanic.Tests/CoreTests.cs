using System.Text;
using System.Text.Json;
using IndustrialDuckPanic.Core;
using IndustrialDuckPanic.Core.Config;
using IndustrialDuckPanic.Core.Discord;
using IndustrialDuckPanic.Core.Esi;
using IndustrialDuckPanic.Core.Monitoring;

namespace IndustrialDuckPanic.Tests;

public class ConfigTests
{
    private const string Hook = "https://discord.com/api/webhooks/123456789012345678/abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789_-abcdefghijklmn";

    private static IdpConfig Sample() => new()
    {
        WebhookUrl = Hook,
        OwnerType = OwnerType.Alliance,
        OwnerId = 99000001,
        OwnerName = "Duck Alliance",
        CreatedBy = "Mining Duck",
    };

    [Fact]
    public void Round_trips_and_hides_the_url()
    {
        var bytes = IdpConfigCodec.Encode(Sample());
        var text = Encoding.Latin1.GetString(bytes);
        Assert.DoesNotContain("discord", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("webhooks", text, StringComparison.OrdinalIgnoreCase);

        var back = IdpConfigCodec.Decode(bytes);
        Assert.Equal(Hook, back.WebhookUrl);
        Assert.Equal(OwnerType.Alliance, back.OwnerType);
        Assert.Equal("Duck Alliance", back.OwnerName);
        Assert.Equal(new MessageSet().Tackled, back.Messages.Tackled);
    }

    [Fact]
    public void Rejects_edited_file()
    {
        var bytes = IdpConfigCodec.Encode(Sample());
        bytes[^3] ^= 0x01;
        var ex = Assert.Throws<ConfigException>(() => IdpConfigCodec.Decode(bytes));
        Assert.Contains("edited", ex.Message);
    }

    [Fact]
    public void Rejects_other_files()
    {
        Assert.Throws<ConfigException>(() => IdpConfigCodec.Decode("{\"webhookUrl\":\"x\"}"u8.ToArray()));
        Assert.Throws<ConfigException>(() => IdpConfigCodec.Decode([]));
    }

    [Fact]
    public void Validates_content()
    {
        Assert.Empty(IdpConfigValidator.Validate(Sample()));

        var bad = Sample();
        bad.WebhookUrl = "https://evil.example.com/api/webhooks/1/2";
        bad.Messages.Panic = " ";
        bad.Ping = new PingSettings { Mode = PingMode.Role, RoleId = "abc" };
        var errors = IdpConfigValidator.Validate(bad);
        Assert.Equal(3, errors.Count);
    }

    [Theory]
    [InlineData(OwnerType.Corporation, 98000001, 98000001, null, true)]
    [InlineData(OwnerType.Corporation, 98000001, 98000002, null, false)]
    [InlineData(OwnerType.Alliance, 99000001, 98000002, 99000001, true)]
    [InlineData(OwnerType.Alliance, 99000001, 98000002, 99000009, false)]
    [InlineData(OwnerType.Alliance, 99000001, 98000002, null, false)]
    public void Checks_membership(OwnerType type, int ownerId, int corp, int? alliance, bool expected)
    {
        var c = new IdpConfig { OwnerType = type, OwnerId = ownerId };
        Assert.Equal(expected, IdpConfigValidator.IsAuthorized(c, corp, alliance));
    }

    [Fact]
    public void Local_settings_protect_secrets()
    {
        var dir = Path.Combine(Path.GetTempPath(), "idp-settings-" + Guid.NewGuid().ToString("N"));
        try
        {
            var store = new LocalSettingsStore(dir);
            var s = new LocalSettings { CharacterId = 5, RefreshToken = "very-secret-refresh", Config = Sample() };
            store.Save(s);
            var raw = File.ReadAllText(store.FilePath);
            Assert.DoesNotContain("very-secret-refresh", raw);
            Assert.DoesNotContain("discord.com", raw);

            var loaded = store.Load();
            Assert.Equal("very-secret-refresh", loaded.RefreshToken);
            Assert.Equal(Hook, loaded.Config!.WebhookUrl);
        }
        finally { try { Directory.Delete(dir, true); } catch { } }
    }
}

public class DiscordTests
{
    [Theory]
    [InlineData("https://discord.com/api/webhooks/123456789012345678/abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789", true)]
    [InlineData("https://discordapp.com/api/webhooks/123456789012345678/abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789", true)]
    [InlineData("https://ptb.discord.com/api/v10/webhooks/123456789012345678/abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789", true)]
    [InlineData("http://discord.com/api/webhooks/123456789012345678/abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789", false)]
    [InlineData("https://discord.com.evil.io/api/webhooks/123456789012345678/abcdefghijklmnopqrstuvwxyzABCDEF", false)]
    [InlineData("", false)]
    public void Validates_webhook_url(string url, bool ok) => Assert.Equal(ok, WebhookUrl.IsValid(url));

    [Fact]
    public void Payload_with_here_ping()
    {
        var json = DiscordWebhook.BuildPayload("Duck tackled", new PingSettings { Mode = PingMode.Here });
        using var doc = JsonDocument.Parse(json);
        Assert.Equal("@here Duck tackled", doc.RootElement.GetProperty("content").GetString());
        Assert.Equal("everyone", doc.RootElement.GetProperty("allowed_mentions").GetProperty("parse")[0].GetString());
        Assert.Equal(AppConstants.AppName, doc.RootElement.GetProperty("username").GetString());
    }

    [Fact]
    public void Payload_with_role_ping_only_allows_that_role()
    {
        var json = DiscordWebhook.BuildPayload("x", new PingSettings { Mode = PingMode.Role, RoleId = "123456789012345678" });
        using var doc = JsonDocument.Parse(json);
        Assert.Equal("<@&123456789012345678> x", doc.RootElement.GetProperty("content").GetString());
        var allowed = doc.RootElement.GetProperty("allowed_mentions");
        Assert.Equal(0, allowed.GetProperty("parse").GetArrayLength());
        Assert.Equal("123456789012345678", allowed.GetProperty("roles")[0].GetString());
    }

    [Fact]
    public void Payload_without_ping_blocks_mentions_typed_in_messages()
    {
        var json = DiscordWebhook.BuildPayload("@everyone hi", new PingSettings { Mode = PingMode.None });
        using var doc = JsonDocument.Parse(json);
        Assert.Equal(0, doc.RootElement.GetProperty("allowed_mentions").GetProperty("parse").GetArrayLength());
    }

    [Fact]
    public void Renders_placeholders()
    {
        var text = MessageTemplates.Render(new MessageSet().Tackled, new MessageContext("Mining Duck", "1DQ1-A", "Rorqual"));
        Assert.Equal("Mining Duck tackled in 1DQ1-A on a Rorqual", text);
        Assert.Equal("Mining Duck is alive and safe in unknown system",
            MessageTemplates.Render(new MessageSet().ImOk, new MessageContext("Mining Duck", null, null)));
        Assert.Equal("by Rat", MessageTemplates.Render("by %ATTACKER%", new MessageContext("a", "b", "c", "Rat")));
    }
}

public class EsiTests
{
    [Fact]
    public void Pkce_challenge_matches_rfc7636_example()
    {
        // RFC 7636 appendix B
        Assert.Equal("E9Melhoa2OwvFrEMTJguCHaoeK1t8URWbuGJSstw-cM", Pkce.ChallengeFor("dBjftJeZ4CVP-mB92K27uhbUJU1p1r_wW1gFWFOEjXk"));
    }

    [Fact]
    public void Authorize_url_has_pkce_and_minimal_scopes()
    {
        var url = EsiAuth.BuildAuthorizeUrl("st", "ch");
        Assert.StartsWith("https://login.eveonline.com/v2/oauth/authorize?", url);
        Assert.Contains("client_id=" + AppConstants.EsiClientId, url);
        Assert.Contains("code_challenge=ch&code_challenge_method=S256", url);
        Assert.Contains("redirect_uri=http%3A%2F%2Flocalhost%3A8642%2Fcallback", url);
        Assert.Contains("esi-location.read_location.v1%20esi-location.read_ship_type.v1%20esi-location.read_online.v1", url);
    }

    [Fact]
    public void Reads_character_from_jwt()
    {
        static string B64(string s) => Pkce.Base64Url(Encoding.UTF8.GetBytes(s));
        var jwt = B64("{\"alg\":\"RS256\"}") + "." + B64("{\"sub\":\"CHARACTER:EVE:95465499\",\"name\":\"Mining Duck\",\"exp\":1900000000}") + ".sig";
        var (id, name, exp) = JwtReader.Read(jwt);
        Assert.Equal(95465499, id);
        Assert.Equal("Mining Duck", name);
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1900000000), exp);
    }
}

public class MonitoringTests
{
    private static readonly Func<int, bool> Allowed = id => AppConstants.FallbackAllowedShips.ContainsKey(id);
    private const int Rorqual = 28352, Venture = 32880, Capsule = 670;

    [Fact]
    public void Detects_destruction_in_space()
    {
        var m = new ShipMonitor(Allowed);
        m.Update(new ShipSnapshot(true, 1, false, 100, Rorqual));
        var ev = Assert.Single(m.Update(new ShipSnapshot(true, 1, false, 200, Capsule)));
        Assert.Equal(ShipEventKind.Destroyed, ev.Kind);
        Assert.Equal(Rorqual, ev.Previous.ShipTypeId);
    }

    [Fact]
    public void Leaving_ship_in_station_is_not_destruction()
    {
        var m = new ShipMonitor(Allowed);
        m.Update(new ShipSnapshot(true, 1, true, 100, Rorqual));
        Assert.Equal(ShipEventKind.ShipChanged, Assert.Single(m.Update(new ShipSnapshot(true, 1, true, 200, Capsule))).Kind);
    }

    [Fact]
    public void Losing_non_allowed_ship_is_not_reported()
    {
        var m = new ShipMonitor(Allowed);
        m.Update(new ShipSnapshot(true, 1, false, 100, Venture));
        Assert.Equal(ShipEventKind.ShipChanged, Assert.Single(m.Update(new ShipSnapshot(true, 1, false, 200, Capsule))).Kind);
    }

    [Fact]
    public void Reports_dock_undock_and_jumps()
    {
        var m = new ShipMonitor(Allowed);
        Assert.Empty(m.Update(new ShipSnapshot(true, 1, true, 100, Rorqual)));
        Assert.Equal(ShipEventKind.Undocked, Assert.Single(m.Update(new ShipSnapshot(true, 1, false, 100, Rorqual))).Kind);
        Assert.Equal(ShipEventKind.SystemChanged, Assert.Single(m.Update(new ShipSnapshot(true, 2, false, 100, Rorqual))).Kind);
        Assert.Equal(ShipEventKind.Docked, Assert.Single(m.Update(new ShipSnapshot(true, 2, true, 100, Rorqual))).Kind);
    }

    [Fact]
    public void Tackle_episode_sends_once_and_times_out()
    {
        var t0 = new DateTime(2026, 11, 17, 18, 0, 0, DateTimeKind.Utc);
        var tr = new TackleTracker(TimeSpan.FromSeconds(60));
        Assert.True(tr.OnTackle("Rat", t0));
        Assert.False(tr.OnTackle("Rat", t0.AddSeconds(5)));
        Assert.True(tr.IsAlerting);
        Assert.False(tr.Tick(t0.AddSeconds(60)));
        Assert.True(tr.Tick(t0.AddSeconds(66)));
        Assert.False(tr.IsAlerting);
        Assert.True(tr.OnTackle("Rat", t0.AddSeconds(200)));
    }

    [Fact]
    public void Acknowledge_silences_but_keeps_episode()
    {
        var t0 = DateTime.UtcNow;
        var tr = new TackleTracker(TimeSpan.FromSeconds(60));
        tr.OnTackle("Rat", t0);
        tr.Acknowledge();
        Assert.False(tr.IsAlerting);
        Assert.False(tr.OnTackle("Rat", t0.AddSeconds(5)));   // same episode: no new message
        Assert.False(tr.IsAlerting);
        tr.Reset();
        Assert.True(tr.OnTackle("Rat", t0.AddSeconds(10)));
    }
}
