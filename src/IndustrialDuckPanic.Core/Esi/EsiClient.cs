using System.Collections.Concurrent;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Serialization;

namespace IndustrialDuckPanic.Core.Esi;

public sealed record EsiLocation(
    [property: JsonPropertyName("solar_system_id")] int SolarSystemId,
    [property: JsonPropertyName("station_id")] long? StationId,
    [property: JsonPropertyName("structure_id")] long? StructureId)
{
    public bool IsDocked => StationId is not null || StructureId is not null;
}

public sealed record EsiShip(
    [property: JsonPropertyName("ship_item_id")] long ShipItemId,
    [property: JsonPropertyName("ship_name")] string ShipName,
    [property: JsonPropertyName("ship_type_id")] int ShipTypeId);

public sealed record EsiOnline([property: JsonPropertyName("online")] bool Online);

public sealed record EsiCharacterPublic(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("corporation_id")] int CorporationId,
    [property: JsonPropertyName("alliance_id")] int? AllianceId);

public sealed record EsiNamedEntity(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("ticker")] string? Ticker);

public sealed record EsiSystem(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("security_status")] double SecurityStatus);

public sealed record EsiType(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("group_id")] int GroupId);

public sealed record EsiGroup(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("types")] int[] Types);

public sealed class EsiException(string message, int statusCode) : Exception(message)
{
    public int StatusCode { get; } = statusCode;
}

/// <summary>Thin ESI client. Static data (names, systems, types) is cached for the session.</summary>
public sealed class EsiClient(HttpClient http)
{
    private const string Base = "https://esi.evetech.net/latest";
    private readonly ConcurrentDictionary<string, object> _cache = new();

    public Task<EsiLocation> GetLocationAsync(int characterId, string token, CancellationToken ct) =>
        GetAuthed<EsiLocation>($"/characters/{characterId}/location/", token, ct);

    public Task<EsiShip> GetShipAsync(int characterId, string token, CancellationToken ct) =>
        GetAuthed<EsiShip>($"/characters/{characterId}/ship/", token, ct);

    public Task<EsiOnline> GetOnlineAsync(int characterId, string token, CancellationToken ct) =>
        GetAuthed<EsiOnline>($"/characters/{characterId}/online/", token, ct);

    /// <summary>Corporation and alliance are public and change rarely, but are not cached so a corp change is seen on restart.</summary>
    public Task<EsiCharacterPublic> GetCharacterAsync(int characterId, CancellationToken ct) =>
        Get<EsiCharacterPublic>($"/characters/{characterId}/", null, ct);

    public Task<EsiNamedEntity> GetCorporationAsync(int id, CancellationToken ct) => Cached($"corp:{id}", () => Get<EsiNamedEntity>($"/corporations/{id}/", null, ct));
    public Task<EsiNamedEntity> GetAllianceAsync(int id, CancellationToken ct) => Cached($"ally:{id}", () => Get<EsiNamedEntity>($"/alliances/{id}/", null, ct));
    public Task<EsiSystem> GetSystemAsync(int id, CancellationToken ct) => Cached($"sys:{id}", () => Get<EsiSystem>($"/universe/systems/{id}/", null, ct));
    public Task<EsiType> GetTypeAsync(int id, CancellationToken ct) => Cached($"type:{id}", () => Get<EsiType>($"/universe/types/{id}/", null, ct));
    public Task<EsiGroup> GetGroupAsync(int id, CancellationToken ct) => Cached($"group:{id}", () => Get<EsiGroup>($"/universe/groups/{id}/", null, ct));

    /// <summary>Every published ship of the allowed groups (Industrial Command Ships + Capital Industrial Ships).</summary>
    public async Task<IReadOnlyDictionary<int, string>> GetAllowedShipsAsync(CancellationToken ct)
    {
        var result = new SortedDictionary<int, string>();
        foreach (var groupId in AppConstants.AllowedShipGroups)
        {
            var group = await GetGroupAsync(groupId, ct);
            foreach (var typeId in group.Types)
                result[typeId] = (await GetTypeAsync(typeId, ct)).Name;
        }
        return result;
    }

    private async Task<T> Cached<T>(string key, Func<Task<T>> factory) where T : notnull
    {
        if (_cache.TryGetValue(key, out var hit)) return (T)hit;
        var value = await factory();
        _cache[key] = value;
        return value;
    }

    private Task<T> GetAuthed<T>(string path, string token, CancellationToken ct) => Get<T>(path, token, ct);

    private async Task<T> Get<T>(string path, string? token, CancellationToken ct)
    {
        using var req = new HttpRequestMessage(HttpMethod.Get, $"{Base}{path}?datasource=tranquility");
        if (token is not null) req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var resp = await http.SendAsync(req, ct);
        if (!resp.IsSuccessStatusCode)
            throw new EsiException($"ESI {path} answered {(int)resp.StatusCode}.", (int)resp.StatusCode);
        return await resp.Content.ReadFromJsonAsync<T>(ct) ?? throw new EsiException($"ESI {path} sent an empty answer.", 0);
    }
}
