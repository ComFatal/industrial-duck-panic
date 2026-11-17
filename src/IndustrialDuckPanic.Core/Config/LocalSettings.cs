using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace IndustrialDuckPanic.Core.Config;

/// <summary>Per-user settings stored in %AppData%\IndustrialDuckPanic. Secrets are protected with Windows DPAPI.</summary>
public sealed class LocalSettings
{
    [JsonPropertyName("characterId")] public int? CharacterId { get; set; }
    [JsonPropertyName("characterName")] public string? CharacterName { get; set; }
    [JsonPropertyName("refreshToken")] public string? ProtectedRefreshToken { get; set; }

    /// <summary>The active IDP.config, kept in its encrypted form.</summary>
    [JsonPropertyName("config")] public string? EncodedConfig { get; set; }

    [JsonPropertyName("allowedShips")] public Dictionary<int, string>? AllowedShips { get; set; }
    [JsonPropertyName("gamelogFolder")] public string? GamelogFolder { get; set; }

    [JsonPropertyName("windowWidth")] public double? WindowWidth { get; set; }
    [JsonPropertyName("windowHeight")] public double? WindowHeight { get; set; }
    [JsonPropertyName("windowLeft")] public double? WindowLeft { get; set; }
    [JsonPropertyName("windowTop")] public double? WindowTop { get; set; }
    [JsonPropertyName("topmost")] public bool Topmost { get; set; }

    [JsonIgnore]
    public string? RefreshToken
    {
        get => Unprotect(ProtectedRefreshToken);
        set => ProtectedRefreshToken = Protect(value);
    }

    [JsonIgnore]
    public IdpConfig? Config
    {
        get
        {
            if (string.IsNullOrEmpty(EncodedConfig)) return null;
            try { return IdpConfigCodec.Decode(Convert.FromBase64String(EncodedConfig)); }
            catch { return null; }
        }
        set => EncodedConfig = value is null ? null : Convert.ToBase64String(IdpConfigCodec.Encode(value));
    }

    private static readonly byte[] Entropy = "IDP-local-v1"u8.ToArray();

    private static string? Protect(string? value)
    {
        if (string.IsNullOrEmpty(value)) return null;
        if (!OperatingSystem.IsWindows()) return Convert.ToBase64String(Encoding.UTF8.GetBytes(value));
        return Convert.ToBase64String(ProtectedData.Protect(Encoding.UTF8.GetBytes(value), Entropy, DataProtectionScope.CurrentUser));
    }

    private static string? Unprotect(string? value)
    {
        if (string.IsNullOrEmpty(value)) return null;
        try
        {
            var bytes = Convert.FromBase64String(value);
            if (!OperatingSystem.IsWindows()) return Encoding.UTF8.GetString(bytes);
            return Encoding.UTF8.GetString(ProtectedData.Unprotect(bytes, Entropy, DataProtectionScope.CurrentUser));
        }
        catch { return null; }
    }
}

public sealed class LocalSettingsStore(string folder)
{
    public static LocalSettingsStore Default() =>
        new(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), AppConstants.AppId));

    public string FilePath => Path.Combine(folder, "settings.json");

    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    public LocalSettings Load()
    {
        try
        {
            if (File.Exists(FilePath))
                return JsonSerializer.Deserialize<LocalSettings>(File.ReadAllText(FilePath), Json) ?? new LocalSettings();
        }
        catch { /* corrupted file: start fresh */ }
        return new LocalSettings();
    }

    public void Save(LocalSettings settings)
    {
        Directory.CreateDirectory(folder);
        var tmp = FilePath + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(settings, Json));
        File.Move(tmp, FilePath, overwrite: true);
    }
}
