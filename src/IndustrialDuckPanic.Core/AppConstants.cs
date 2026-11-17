namespace IndustrialDuckPanic.Core;

public static class AppConstants
{
    public const string AppName = "Industrial Duck Panic";
    public const string AppId = "IndustrialDuckPanic";

    /// <summary>EVE developer application (PKCE, no secret).</summary>
    public const string EsiClientId = "54d94b217b1245e58a7a6ba1d73327d6";
    public const int CallbackPort = 8642;
    public static readonly string CallbackUrl = $"http://localhost:{CallbackPort}/callback";

    /// <summary>Smallest set of scopes the app needs.</summary>
    public static readonly string[] EsiScopes =
    [
        "esi-location.read_location.v1",
        "esi-location.read_ship_type.v1",
        "esi-location.read_online.v1",
    ];

    public const string UserAgent = "IndustrialDuckPanic/1.0 (+desktop app)";

    /// <summary>ORE corporation (Outer Ring Excavations), used for the Discord avatar.</summary>
    public const int OreCorporationId = 1000129;
    public static readonly string DiscordAvatarUrl = $"https://images.evetech.net/corporations/{OreCorporationId}/logo?size=128";

    /// <summary>ESI inventory groups whose ships may use the app.</summary>
    public const int GroupIndustrialCommandShip = 941;
    public const int GroupCapitalIndustrialShip = 883;
    public static readonly int[] AllowedShipGroups = [GroupIndustrialCommandShip, GroupCapitalIndustrialShip];

    /// <summary>Fallback when ESI can't be reached: Orca, Orca ODE, Porpoise, Rorqual, Rorqual ODE.</summary>
    public static readonly IReadOnlyDictionary<int, string> FallbackAllowedShips = new Dictionary<int, string>
    {
        [42244] = "Porpoise",
        [28606] = "Orca",
        [33685] = "Orca ORE Development Edition",
        [28352] = "Rorqual",
        [33687] = "Rorqual ORE Development Edition",
    };

    /// <summary>Ships that carry the PANIC module.</summary>
    public static readonly HashSet<int> RorqualTypeIds = [28352, 33687];

    /// <summary>Capsule type ids (standard + Genolution variant).</summary>
    public static readonly HashSet<int> CapsuleTypeIds = [670, 33328];

    /// <summary>Seconds without a new tackle line before the alert ends by itself.</summary>
    public static readonly TimeSpan TackleTimeout = TimeSpan.FromSeconds(60);

    /// <summary>Velopack update source (GitHub repository URL). Empty = updates disabled.</summary>
    public const string UpdateRepositoryUrl = "";
}
