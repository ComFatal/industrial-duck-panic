using IndustrialDuckPanic.Core;
using IndustrialDuckPanic.Core.Discord;
using IndustrialDuckPanic.Core.Esi;

namespace IndustrialDuckPanic.Tests;

/// <summary>Local loopback checks: no call to EVE servers.</summary>
public class SsoCallbackTests
{
    [Fact]
    public async Task Callback_listener_runs_without_admin_and_rejects_a_wrong_state()
    {
        using var http = new HttpClient();
        var auth = new EsiAuth(http);
        string? authorizeUrl = null;

        var login = auth.LoginAsync(url =>
        {
            authorizeUrl = url;
            // Pretend the browser came back with a forged state.
            _ = Task.Run(async () =>
            {
                using var browser = new HttpClient();
                await browser.GetAsync($"{AppConstants.CallbackUrl}?code=abc&state=forged");
            });
        }, new CancellationTokenSource(TimeSpan.FromSeconds(10)).Token);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => login);
        Assert.Contains("refused", ex.Message);
        Assert.NotNull(authorizeUrl);
        Assert.Contains("code_challenge_method=S256", authorizeUrl);
    }

    [Fact]
    public async Task Login_can_be_cancelled()
    {
        using var http = new HttpClient();
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(300));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new EsiAuth(http).LoginAsync(_ => { }, cts.Token));
    }
}

/// <summary>Calls the public ESI API. Run with: dotnet test --filter Category=Live</summary>
[Trait("Category", "Live")]
public class LiveEsiTests
{
    [Fact]
    public async Task Allowed_ship_list_comes_from_esi()
    {
        using var http = new HttpClient();
        http.DefaultRequestHeaders.UserAgent.ParseAdd(AppConstants.UserAgent);
        var ships = await new EsiClient(http).GetAllowedShipsAsync(CancellationToken.None);
        Assert.Equal("Rorqual", ships[28352]);
        Assert.Equal("Orca", ships[28606]);
        Assert.Equal("Porpoise", ships[42244]);
    }

    [Fact]
    public async Task Public_endpoints_parse()
    {
        using var http = new HttpClient();
        http.DefaultRequestHeaders.UserAgent.ParseAdd(AppConstants.UserAgent);
        var esi = new EsiClient(http);
        Assert.Equal("Jita", (await esi.GetSystemAsync(30000142, CancellationToken.None)).Name);
        Assert.Equal("Outer Ring Excavations", (await esi.GetCorporationAsync(AppConstants.OreCorporationId, CancellationToken.None)).Name);
    }

    [Fact]
    public async Task Unknown_webhook_is_reported_as_not_working()
    {
        using var http = new HttpClient();
        var ok = await new DiscordWebhook(http).CheckAsync(
            "https://discord.com/api/webhooks/100000000000000000/aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa", CancellationToken.None);
        Assert.False(ok);
    }
}
