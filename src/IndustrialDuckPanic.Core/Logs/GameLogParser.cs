using System.Globalization;
using System.Net;
using System.Text.RegularExpressions;

namespace IndustrialDuckPanic.Core.Logs;

public abstract record GameLogEvent(DateTime TimestampUtc);

/// <summary>Someone tries to warp scramble or warp disrupt the player.</summary>
public sealed record TackleEvent(DateTime TimestampUtc, string Kind, string Attacker) : GameLogEvent(TimestampUtc);

/// <summary>The player jumped through a stargate.</summary>
public sealed record JumpEvent(DateTime TimestampUtc, string FromSystem, string ToSystem) : GameLogEvent(TimestampUtc);

public static partial class GameLogParser
{
    // [ 2026.11.17 18:22:31 ] (combat) Warp scramble attempt from Pilot to you!
    [GeneratedRegex(@"^\[\s*(?<ts>\d{4}\.\d{2}\.\d{2} \d{2}:\d{2}:\d{2})\s*\]\s*\((?<cat>[^)]+)\)\s*(?<msg>.*)$")]
    private static partial Regex LineRx();

    [GeneratedRegex(@"^(?<kind>Warp (?:scramble|disruption) attempt)\s+from\s+(?<from>.+?)\s+to\s+you\s*!?\s*$", RegexOptions.IgnoreCase)]
    private static partial Regex TackleRx();

    [GeneratedRegex(@"^Jumping from\s+(?<from>.+?)\s+to\s+(?<to>.+?)\s*$", RegexOptions.IgnoreCase)]
    private static partial Regex JumpRx();

    [GeneratedRegex(@"<[^>]*>")]
    private static partial Regex TagRx();

    [GeneratedRegex(@"\s+")]
    private static partial Regex SpaceRx();

    [GeneratedRegex(@"^\s*Listener:\s*(?<name>.+?)\s*$")]
    private static partial Regex ListenerRx();

    /// <summary>Removes the EVE markup (color, font, b, a href) and squashes spaces.</summary>
    public static string StripMarkup(string text) =>
        SpaceRx().Replace(WebUtility.HtmlDecode(TagRx().Replace(text, " ")), " ").Trim();

    public static GameLogEvent? Parse(string line)
    {
        var m = LineRx().Match(line.TrimEnd('\r', '\n'));
        if (!m.Success) return null;
        if (!DateTime.TryParseExact(m.Groups["ts"].Value, "yyyy.MM.dd HH:mm:ss", CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var ts))
            return null;

        var category = m.Groups["cat"].Value.Trim();
        var msg = StripMarkup(m.Groups["msg"].Value);

        if (category.Equals("combat", StringComparison.OrdinalIgnoreCase))
        {
            var t = TackleRx().Match(msg);
            if (t.Success)
                return new TackleEvent(ts, t.Groups["kind"].Value, t.Groups["from"].Value.Trim(' ', '-'));
        }

        var j = JumpRx().Match(msg);
        if (j.Success)
            return new JumpEvent(ts, j.Groups["from"].Value, j.Groups["to"].Value);

        return null;
    }

    /// <summary>Reads the character name from the gamelog header ("Listener: Name").</summary>
    public static string? ReadListener(IEnumerable<string> headerLines)
    {
        foreach (var line in headerLines.Take(10))
        {
            var m = ListenerRx().Match(line);
            if (m.Success) return m.Groups["name"].Value;
        }
        return null;
    }
}
