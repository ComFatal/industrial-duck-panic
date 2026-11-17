using System.Text.Json.Serialization;

namespace IndustrialDuckPanic.Core.Discord;

public enum AlertKind { Test, Tackled, Panic, Destroyed, ImOk }

/// <summary>The Discord message texts. Placeholders: %personnage_name%, %system%, %ship%, %attacker%.</summary>
public sealed class MessageSet
{
    [JsonPropertyName("test")] public string Test { get; set; } = "Industrial Duck Panic test message from %personnage_name%. The webhook works.";
    [JsonPropertyName("tackled")] public string Tackled { get; set; } = "%personnage_name% tackled in %system% on a %ship%";
    [JsonPropertyName("panic")] public string Panic { get; set; } = "%personnage_name% in %system% engaged PANIC";
    [JsonPropertyName("destroyed")] public string Destroyed { get; set; } = "%personnage_name% lost his %ship% in %system%";
    [JsonPropertyName("ok")] public string ImOk { get; set; } = "%personnage_name% is alive and safe in %system%";

    public string Get(AlertKind kind) => kind switch
    {
        AlertKind.Test => Test,
        AlertKind.Tackled => Tackled,
        AlertKind.Panic => Panic,
        AlertKind.Destroyed => Destroyed,
        AlertKind.ImOk => ImOk,
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };

    public MessageSet Clone() => (MessageSet)MemberwiseClone();
}

public sealed record MessageContext(string CharacterName, string? SystemName, string? ShipName, string? Attacker = null);

public static class MessageTemplates
{
    public const string PlaceholderHelp = "%personnage_name%  %system%  %ship%  %attacker%";

    public static string Render(string template, MessageContext ctx) =>
        template
            .Replace("%personnage_name%", ctx.CharacterName, StringComparison.OrdinalIgnoreCase)
            .Replace("%character%", ctx.CharacterName, StringComparison.OrdinalIgnoreCase)
            .Replace("%system%", string.IsNullOrWhiteSpace(ctx.SystemName) ? "unknown system" : ctx.SystemName, StringComparison.OrdinalIgnoreCase)
            .Replace("%ship%", string.IsNullOrWhiteSpace(ctx.ShipName) ? "unknown ship" : ctx.ShipName, StringComparison.OrdinalIgnoreCase)
            .Replace("%attacker%", string.IsNullOrWhiteSpace(ctx.Attacker) ? "unknown" : ctx.Attacker, StringComparison.OrdinalIgnoreCase);
}
