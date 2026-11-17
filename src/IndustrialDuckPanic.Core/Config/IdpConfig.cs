using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using IndustrialDuckPanic.Core.Discord;

namespace IndustrialDuckPanic.Core.Config;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum OwnerType { Corporation, Alliance }

/// <summary>Shared configuration a corporation or alliance hands out as IDP.config.</summary>
public sealed class IdpConfig
{
    public const int CurrentFormatVersion = 1;

    [JsonPropertyName("formatVersion")] public int FormatVersion { get; set; } = CurrentFormatVersion;
    [JsonPropertyName("webhookUrl")] public string WebhookUrl { get; set; } = "";
    [JsonPropertyName("ownerType")] public OwnerType OwnerType { get; set; }
    [JsonPropertyName("ownerId")] public int OwnerId { get; set; }
    [JsonPropertyName("ownerName")] public string OwnerName { get; set; } = "";
    [JsonPropertyName("ping")] public PingSettings Ping { get; set; } = new();
    [JsonPropertyName("messages")] public MessageSet Messages { get; set; } = new();
    [JsonPropertyName("createdBy")] public string CreatedBy { get; set; } = "";
    [JsonPropertyName("createdAt")] public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    public string OwnerLabel => $"{(OwnerType == OwnerType.Alliance ? "Alliance" : "Corporation")} {OwnerName}";
}

public sealed class ConfigException(string message) : Exception(message);

/// <summary>
/// Encrypts IDP.config with AES-256-GCM. The file is unreadable and any edit breaks it.
/// The key ships with the app, so this stops casual reading and copy-paste spam, not a determined reverse engineer.
/// </summary>
public static class IdpConfigCodec
{
    private static readonly byte[] Magic = "IDP1"u8.ToArray();
    private const int NonceSize = 12, TagSize = 16;

    // Key material is split and mixed so it does not appear as one string in the binary.
    private static readonly byte[] PartA = [0x3f, 0x91, 0x0c, 0xd7, 0x58, 0xa2, 0x6e, 0x14, 0xc9, 0x07, 0xbb, 0x42, 0x9d, 0x31, 0xe5, 0x7a];
    private static readonly byte[] PartB = [0xd4, 0x2b, 0x86, 0x5f, 0x10, 0xec, 0x93, 0x48, 0x7c, 0xa1, 0x06, 0xfe, 0x35, 0xc8, 0x69, 0x0b];

    private static byte[] Key()
    {
        var seed = new byte[PartA.Length + PartB.Length];
        for (var i = 0; i < PartA.Length; i++)
        {
            seed[i] = (byte)(PartA[i] ^ PartB[PartB.Length - 1 - i]);
            seed[PartA.Length + i] = (byte)(PartB[i] + PartA[i]);
        }
        return SHA256.HashData([.. seed, .. "Industrial Duck Panic / ORE"u8]);
    }

    private static readonly JsonSerializerOptions Json = new() { WriteIndented = false };

    public static byte[] Encode(IdpConfig config)
    {
        var plain = JsonSerializer.SerializeToUtf8Bytes(config, Json);
        var nonce = RandomNumberGenerator.GetBytes(NonceSize);
        var cipher = new byte[plain.Length];
        var tag = new byte[TagSize];
        using (var aes = new AesGcm(Key(), TagSize))
            aes.Encrypt(nonce, plain, cipher, tag, Magic);
        return [.. Magic, .. nonce, .. tag, .. cipher];
    }

    public static IdpConfig Decode(byte[] data)
    {
        if (data.Length < Magic.Length + NonceSize + TagSize || !data.AsSpan(0, Magic.Length).SequenceEqual(Magic))
            throw new ConfigException("This file is not an IDP.config file.");

        var nonce = data.AsSpan(Magic.Length, NonceSize);
        var tag = data.AsSpan(Magic.Length + NonceSize, TagSize);
        var cipher = data.AsSpan(Magic.Length + NonceSize + TagSize);
        var plain = new byte[cipher.Length];
        try
        {
            using var aes = new AesGcm(Key(), TagSize);
            aes.Decrypt(nonce, cipher, tag, plain, Magic);
        }
        catch (CryptographicException)
        {
            throw new ConfigException("The IDP.config file is damaged or was edited by hand. Ask for a new copy.");
        }

        try
        {
            return JsonSerializer.Deserialize<IdpConfig>(plain, Json) ?? throw new ConfigException("The IDP.config file is empty.");
        }
        catch (JsonException)
        {
            throw new ConfigException("The IDP.config file content is not valid.");
        }
    }
}

public static class IdpConfigValidator
{
    /// <summary>Returns the list of problems; empty when the config can be used.</summary>
    public static List<string> Validate(IdpConfig c)
    {
        var errors = new List<string>();
        if (c.FormatVersion > IdpConfig.CurrentFormatVersion)
            errors.Add("This IDP.config was made by a newer version of the app. Update Industrial Duck Panic.");
        if (!Discord.WebhookUrl.IsValid(c.WebhookUrl))
            errors.Add("The webhook URL is not a valid Discord webhook URL.");
        if (c.OwnerId <= 0 || string.IsNullOrWhiteSpace(c.OwnerName))
            errors.Add("The corporation or alliance is missing.");
        if (c.Ping.Mode == PingMode.Role && !PingSettings.IsValidRoleId(c.Ping.RoleId))
            errors.Add("The Discord role id must be the number copied from Discord (15 to 22 digits).");
        foreach (var kind in Enum.GetValues<AlertKind>())
        {
            var text = c.Messages.Get(kind);
            if (string.IsNullOrWhiteSpace(text)) errors.Add($"The {kind} message is empty.");
            else if (text.Length > 1800) errors.Add($"The {kind} message is longer than 1800 characters.");
        }
        return errors;
    }

    /// <summary>True when the character belongs to the corporation or alliance the config was made for.</summary>
    public static bool IsAuthorized(IdpConfig c, int corporationId, int? allianceId) => c.OwnerType switch
    {
        OwnerType.Corporation => c.OwnerId == corporationId,
        OwnerType.Alliance => allianceId is not null && c.OwnerId == allianceId,
        _ => false,
    };
}
