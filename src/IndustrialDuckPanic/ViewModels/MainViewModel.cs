using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Windows.Threading;
using IndustrialDuckPanic.Core;
using IndustrialDuckPanic.Core.Config;
using IndustrialDuckPanic.Core.Discord;
using IndustrialDuckPanic.Core.Esi;
using IndustrialDuckPanic.Core.Logs;
using IndustrialDuckPanic.Core.Monitoring;

namespace IndustrialDuckPanic.ViewModels;

/// <summary>
/// Drives the whole app: EVE login, ESI polling (location, ship, online), gamelog reading (tackles, jumps),
/// and Discord messages. Runs on the UI thread; awaits return to it.
/// </summary>
public sealed class MainViewModel : ObservableObject
{
    private static readonly TimeSpan EsiInterval = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan OnlineInterval = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan WebhookCheckInterval = TimeSpan.FromMinutes(10);

    private readonly HttpClient _http;
    private readonly EsiAuth _auth;
    private readonly EsiClient _esi;
    private readonly DiscordWebhook _discord;
    private readonly LocalSettingsStore _store;
    private readonly TackleTracker _tackle = new(AppConstants.TackleTimeout);
    private readonly ShipMonitor _shipMonitor;
    private readonly DispatcherTimer _timer;
    private readonly DispatcherTimer _toastTimer;

    private EsiTokens? _tokens;
    private CancellationTokenSource? _loopCts;
    private CancellationTokenSource? _loginCts;
    private GameLogWatcher? _logs;
    private DateTime _lastOnlineCheck = DateTime.MinValue;
    private DateTime _lastWebhookCheck = DateTime.MinValue;
    private bool _online = true;
    private bool _demo;

    public LocalSettings Settings { get; }

    public MainViewModel()
    {
        _http = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
        _http.DefaultRequestHeaders.UserAgent.ParseAdd(AppConstants.UserAgent);
        _auth = new EsiAuth(_http);
        _esi = new EsiClient(_http);
        _discord = new DiscordWebhook(_http);
        _store = LocalSettingsStore.Default();
        Settings = _store.Load();
        _shipMonitor = new ShipMonitor(IsAllowedType);

        SetAllowedShips(Settings.AllowedShips is { Count: > 0 } cached ? cached : AppConstants.FallbackAllowedShips);
        _config = Settings.Config;

        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _timer.Tick += (_, _) => OnTick();
        _toastTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(6) };
        _toastTimer.Tick += (_, _) => { _toastTimer.Stop(); Toast = null; };
    }

    // ---------------------------------------------------------------- character

    private bool _isConnected;
    public bool IsConnected { get => _isConnected; private set { if (Set(ref _isConnected, value)) Raise(nameof(ShowShipError)); } }

    private bool _isLoggingIn;
    public bool IsLoggingIn { get => _isLoggingIn; private set => Set(ref _isLoggingIn, value); }

    private int _characterId;
    public int CharacterId { get => _characterId; private set { if (Set(ref _characterId, value)) Raise(nameof(PortraitUrl)); } }

    private string _characterName = "";
    public string CharacterName { get => _characterName; private set => Set(ref _characterName, value); }

    public string? PortraitUrl => CharacterId > 0 ? $"https://images.evetech.net/characters/{CharacterId}/portrait?size=128" : null;

    private int _corporationId;
    private int? _allianceId;

    private string? _corporationName;
    public string? CorporationName { get => _corporationName; private set => Set(ref _corporationName, value); }

    private string? _allianceName;
    public string? AllianceName { get => _allianceName; private set => Set(ref _allianceName, value); }

    public ObservableCollection<OwnerChoice> OwnerChoices { get; } = [];

    // ---------------------------------------------------------------- ship & location

    private int _shipTypeId;
    public int ShipTypeId
    {
        get => _shipTypeId;
        private set
        {
            if (!Set(ref _shipTypeId, value)) return;
            Raise(nameof(IsAllowedShip)); Raise(nameof(IsRorqual)); Raise(nameof(ShowShipError));
        }
    }

    private string _shipTypeName = "—";
    public string ShipTypeName { get => _shipTypeName; private set => Set(ref _shipTypeName, value); }

    private string _systemName = "—";
    public string SystemName { get => _systemName; private set => Set(ref _systemName, value); }

    private string _securityText = "";
    public string SecurityText { get => _securityText; private set => Set(ref _securityText, value); }

    private bool? _isDocked;
    public bool? IsDocked { get => _isDocked; private set { if (Set(ref _isDocked, value)) Raise(nameof(LocationText)); } }

    private bool _isOnline;
    public bool IsOnline { get => _isOnline; private set { if (Set(ref _isOnline, value)) Raise(nameof(LocationText)); } }

    public string LocationText => !IsConnected ? "—" : !IsOnline ? "Offline" : IsDocked switch { true => "Docked", false => "In space", _ => "Unknown" };

    public bool IsAllowedShip => IsAllowedType(ShipTypeId);
    public bool IsRorqual => AppConstants.RorqualTypeIds.Contains(ShipTypeId);
    public bool ShowShipError => IsConnected && ShipTypeId != 0 && !IsAllowedShip && !ShowShipLost;

    private SwitchPosition _switch = SwitchPosition.Middle;
    public SwitchPosition Switch { get => _switch; private set => Set(ref _switch, value); }

    private bool _showShipLost;
    public bool ShowShipLost { get => _showShipLost; private set { if (Set(ref _showShipLost, value)) Raise(nameof(ShowShipError)); } }

    private string _shipLostText = "";
    public string ShipLostText { get => _shipLostText; private set => Set(ref _shipLostText, value); }

    // ---------------------------------------------------------------- tackle

    private bool _isTackled;
    public bool IsTackled { get => _isTackled; private set => Set(ref _isTackled, value); }

    private string? _tackleText;
    public string? TackleText { get => _tackleText; private set => Set(ref _tackleText, value); }

    // ---------------------------------------------------------------- allowed ships

    private Dictionary<int, string> _allowedShips = [];
    public ObservableCollection<ShipEntry> AllowedShips { get; } = [];

    private string _allowedShipsSource = "Built-in list";
    public string AllowedShipsSource { get => _allowedShipsSource; private set => Set(ref _allowedShipsSource, value); }

    private bool IsAllowedType(int typeId) => _allowedShips.ContainsKey(typeId);

    private void SetAllowedShips(IReadOnlyDictionary<int, string> ships)
    {
        _allowedShips = ships.ToDictionary();
        AllowedShips.Clear();
        foreach (var (id, name) in ships.OrderBy(s => AppConstants.RorqualTypeIds.Contains(s.Key)).ThenBy(s => s.Value))
            AllowedShips.Add(new ShipEntry(id, name, AppConstants.RorqualTypeIds.Contains(id)));
        Raise(nameof(IsAllowedShip)); Raise(nameof(ShowShipError));
    }

    // ---------------------------------------------------------------- discord config

    private IdpConfig? _config;
    public IdpConfig? Config
    {
        get => _config;
        private set { if (Set(ref _config, value)) { Raise(nameof(HasConfig)); Raise(nameof(ConfigSummary)); } }
    }

    public bool HasConfig => Config is not null;
    public string ConfigSummary => Config is null ? "No configuration loaded." : $"{Config.OwnerLabel} · made by {Config.CreatedBy} on {Config.CreatedAt.LocalDateTime:yyyy-MM-dd}";

    private bool _isConfigAuthorized;
    public bool IsConfigAuthorized { get => _isConfigAuthorized; private set => Set(ref _isConfigAuthorized, value); }

    // ---------------------------------------------------------------- health

    private Health _esiHealth;
    public Health EsiHealth { get => _esiHealth; private set { if (Set(ref _esiHealth, value)) Raise(nameof(OverallHealth)); } }

    private string _esiDetail = "Not connected";
    public string EsiDetail { get => _esiDetail; private set => Set(ref _esiDetail, value); }

    private Health _webhookHealth;
    public Health WebhookHealth { get => _webhookHealth; private set { if (Set(ref _webhookHealth, value)) Raise(nameof(OverallHealth)); } }

    private string _webhookDetail = "No configuration loaded";
    public string WebhookDetail { get => _webhookDetail; private set => Set(ref _webhookDetail, value); }

    private Health _gamelogHealth;
    public Health GamelogHealth { get => _gamelogHealth; private set => Set(ref _gamelogHealth, value); }

    private string _gamelogDetail = "Waiting for character";
    public string GamelogDetail { get => _gamelogDetail; private set => Set(ref _gamelogDetail, value); }

    public Health OverallHealth => EsiHealth == Health.Ok && WebhookHealth == Health.Ok ? Health.Ok : Health.Error;

    // ---------------------------------------------------------------- toast

    private string? _toast;
    public string? Toast { get => _toast; private set => Set(ref _toast, value); }

    private bool _toastIsError;
    public bool ToastIsError { get => _toastIsError; private set => Set(ref _toastIsError, value); }

    public void ShowToast(string text, bool error = false)
    {
        Toast = text;
        ToastIsError = error;
        _toastTimer.Stop();
        _toastTimer.Start();
    }

    // ================================================================ lifecycle

    public async Task StartAsync()
    {
        _timer.Start();
        _ = RefreshAllowedShipsAsync();

        if (Settings.CharacterId is { } id && Settings.RefreshToken is not null)
        {
            CharacterId = id;
            CharacterName = Settings.CharacterName ?? "";
            IsConnected = true;
            StartLoop();
        }
        else
        {
            UpdateConfigState();
        }
        await Task.CompletedTask;
    }

    /// <summary>
    /// Offline demo for screenshots and UI checks (--demo [rorqual|tackled|wrong|docked|lost|error]).
    /// No network, no Discord, nothing saved.
    /// </summary>
    public void StartDemo(string scenario)
    {
        _demo = true;
        _timer.Start();
        IsConnected = true;
        CharacterId = 0;
        CharacterName = "Demo Duck";
        CorporationName = "Duck Mining Corp [DUCK]";
        AllianceName = "Demo Alliance <DEMO>";
        SystemName = "1DQ1-A";
        SecurityText = "-0.4";
        IsOnline = true;
        ShipTypeId = scenario == "wrong" ? 32880 : 28352;
        ShipTypeName = scenario == "wrong" ? "Venture" : "Rorqual";
        IsDocked = scenario == "docked";
        Switch = scenario switch { "docked" => SwitchPosition.Off, "error" => SwitchPosition.Middle, _ => SwitchPosition.On };
        EsiHealth = scenario == "error" ? Health.Error : Health.Ok;
        EsiDetail = "Demo mode";
        WebhookHealth = Health.Ok;
        WebhookDetail = "Demo mode: nothing is sent";
        GamelogHealth = Health.Ok;
        GamelogDetail = "Demo mode";
        if (scenario == "tackled")
        {
            _simulatedTackleUntil = DateTime.MaxValue;
            IsTackled = true;
            TackleText = "Warp scramble attempt from Gurista Dismantler";
        }
        if (scenario == "lost")
        {
            ShipTypeId = 670;
            ShipTypeName = "Capsule";
            ShipLostText = "Rorqual lost in 1DQ1-A";
            ShowShipLost = true;
        }
        Raise(nameof(LocationText));
    }

    public void Shutdown()
    {
        _timer.Stop();
        _loopCts?.Cancel();
        _loginCts?.Cancel();
        _store.Save(Settings);
    }

    public void SaveSettings() => _store.Save(Settings);

    private async Task RefreshAllowedShipsAsync()
    {
        try
        {
            var ships = await _esi.GetAllowedShipsAsync(CancellationToken.None);
            if (ships.Count == 0) return;
            SetAllowedShips(ships);
            AllowedShipsSource = "Live list from EVE (ESI)";
            Settings.AllowedShips = ships.ToDictionary();
            _store.Save(Settings);
        }
        catch (Exception)
        {
            AllowedShipsSource = Settings.AllowedShips is { Count: > 0 } ? "Saved list (EVE unreachable)" : "Built-in list (EVE unreachable)";
        }
    }

    // ================================================================ login

    public async Task LoginAsync()
    {
        if (IsLoggingIn) return;
        IsLoggingIn = true;
        _loginCts = new CancellationTokenSource(TimeSpan.FromMinutes(5));
        try
        {
            var tokens = await _auth.LoginAsync(url => Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }), _loginCts.Token);
            _loopCts?.Cancel();
            ResetCharacterState();
            _tokens = tokens;
            CharacterId = tokens.CharacterId;
            CharacterName = tokens.CharacterName;
            Settings.CharacterId = tokens.CharacterId;
            Settings.CharacterName = tokens.CharacterName;
            Settings.RefreshToken = tokens.RefreshToken;
            _store.Save(Settings);
            IsConnected = true;
            StartLoop();
            ShowToast($"{tokens.CharacterName} connected.");
        }
        catch (OperationCanceledException)
        {
            ShowToast("EVE login cancelled.", error: true);
        }
        catch (System.Net.HttpListenerException)
        {
            ShowToast($"Port {AppConstants.CallbackPort} is used by another program. Close it and try again.", error: true);
        }
        catch (Exception ex)
        {
            ShowToast(ex.Message, error: true);
        }
        finally
        {
            IsLoggingIn = false;
            _loginCts?.Dispose();
            _loginCts = null;
        }
    }

    public void CancelLogin() => _loginCts?.Cancel();

    public void Logout(string? reason = null)
    {
        _loopCts?.Cancel();
        _tokens = null;
        Settings.CharacterId = null;
        Settings.CharacterName = null;
        Settings.RefreshToken = null;
        _store.Save(Settings);
        ResetCharacterState();
        IsConnected = false;
        CharacterId = 0;
        CharacterName = "";
        EsiHealth = Health.Unknown;
        EsiDetail = "Not connected";
        Raise(nameof(LocationText));
        UpdateConfigState();
        if (reason is not null) ShowToast(reason, error: true);
    }

    private void ResetCharacterState()
    {
        _shipMonitor.Reset();
        _tackle.Reset();
        _logs = null;
        _corporationId = 0;
        _allianceId = null;
        CorporationName = null;
        AllianceName = null;
        OwnerChoices.Clear();
        ShipTypeId = 0;
        ShipTypeName = "—";
        SystemName = "—";
        SecurityText = "";
        IsDocked = null;
        IsOnline = false;
        IsTackled = false;
        ShowShipLost = false;
        Switch = SwitchPosition.Middle;
        GamelogHealth = Health.Unknown;
        GamelogDetail = "Waiting for character";
    }

    // ================================================================ ESI loop

    private void StartLoop()
    {
        _loopCts?.Cancel();
        _loopCts = new CancellationTokenSource();
        _ = EsiLoopAsync(_loopCts.Token);
    }

    private async Task<string> EnsureTokenAsync(CancellationToken ct)
    {
        if (_tokens is not null && !_tokens.IsExpiring(DateTimeOffset.UtcNow)) return _tokens.AccessToken;
        var refresh = _tokens?.RefreshToken ?? Settings.RefreshToken ?? throw new EsiAuthException("Not logged in.", 401);
        _tokens = await _auth.RefreshAsync(refresh, ct);
        if (_tokens.RefreshToken != refresh)
        {
            Settings.RefreshToken = _tokens.RefreshToken;
            _store.Save(Settings);
        }
        return _tokens.AccessToken;
    }

    private async Task EsiLoopAsync(CancellationToken ct)
    {
        EsiDetail = "Connecting…";
        while (!ct.IsCancellationRequested)
        {
            try
            {
                var token = await EnsureTokenAsync(ct);
                if (_corporationId == 0) await LoadAffiliationAsync(ct);

                if (DateTime.UtcNow - _lastOnlineCheck >= OnlineInterval)
                {
                    _online = (await _esi.GetOnlineAsync(CharacterId, token, ct)).Online;
                    _lastOnlineCheck = DateTime.UtcNow;
                }

                var locationTask = _esi.GetLocationAsync(CharacterId, token, ct);
                var shipTask = _esi.GetShipAsync(CharacterId, token, ct);
                await Task.WhenAll(locationTask, shipTask);
                if (ct.IsCancellationRequested) break;
                await ApplySnapshotAsync(locationTask.Result, shipTask.Result, ct);

                EsiHealth = Health.Ok;
                EsiDetail = _online ? "Connected, reading location and ship" : "Connected, character is offline";
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                break;
            }
            catch (EsiAuthException ex) when (ex.IsInvalidGrant)
            {
                Logout("Your EVE login is no longer valid. Connect your character again.");
                break;
            }
            catch (Exception ex)
            {
                EsiHealth = Health.Error;
                EsiDetail = ex is HttpRequestException or TaskCanceledException ? "EVE servers unreachable, retrying…" : ex.Message;
                Switch = SwitchPosition.Middle;
            }

            try { await Task.Delay(EsiInterval, ct); }
            catch (OperationCanceledException) { break; }
        }
    }

    private async Task LoadAffiliationAsync(CancellationToken ct)
    {
        var info = await _esi.GetCharacterAsync(CharacterId, ct);
        var corp = await _esi.GetCorporationAsync(info.CorporationId, ct);
        _corporationId = info.CorporationId;
        _allianceId = info.AllianceId;
        CorporationName = corp.Ticker is null ? corp.Name : $"{corp.Name} [{corp.Ticker}]";

        OwnerChoices.Clear();
        OwnerChoices.Add(new OwnerChoice(OwnerType.Corporation, info.CorporationId, corp.Name));
        if (info.AllianceId is { } allianceId)
        {
            var alliance = await _esi.GetAllianceAsync(allianceId, ct);
            AllianceName = alliance.Ticker is null ? alliance.Name : $"{alliance.Name} <{alliance.Ticker}>";
            OwnerChoices.Add(new OwnerChoice(OwnerType.Alliance, allianceId, alliance.Name));
        }
        else
        {
            AllianceName = null;
        }
        UpdateConfigState();
    }

    private async Task ApplySnapshotAsync(EsiLocation location, EsiShip ship, CancellationToken ct)
    {
        var system = await _esi.GetSystemAsync(location.SolarSystemId, ct);
        var type = await _esi.GetTypeAsync(ship.ShipTypeId, ct);
        var previousSystem = SystemName;

        IsOnline = _online;
        SystemName = system.Name;
        SecurityText = Math.Round(system.SecurityStatus, 1).ToString("0.0", System.Globalization.CultureInfo.InvariantCulture);
        ShipTypeId = ship.ShipTypeId;
        ShipTypeName = type.Name;
        IsDocked = location.IsDocked;
        Switch = !_online ? SwitchPosition.Middle : location.IsDocked ? SwitchPosition.Off : SwitchPosition.On;

        var events = _shipMonitor.Update(new ShipSnapshot(_online, location.SolarSystemId, location.IsDocked, ship.ShipItemId, ship.ShipTypeId));
        foreach (var ev in events)
        {
            switch (ev.Kind)
            {
                case ShipEventKind.Destroyed:
                    var lostShip = (await _esi.GetTypeAsync(ev.Previous.ShipTypeId, ct)).Name;
                    var lostIn = ev.Previous.SystemId == ev.Current.SystemId ? system.Name : previousSystem;
                    EndTackle();
                    ShipLostText = $"{lostShip} lost in {lostIn}";
                    ShowShipLost = true;
                    _ = SendAsync(AlertKind.Destroyed, shipName: lostShip, systemName: lostIn);
                    break;
                case ShipEventKind.Docked:
                    EndTackle();
                    break;
                case ShipEventKind.ShipChanged when IsAllowedType(ev.Current.ShipTypeId):
                    ShowShipLost = false;
                    break;
            }
        }
    }

    // ================================================================ gamelogs & timers

    private void OnTick()
    {
        var now = DateTime.UtcNow;
        if (IsConnected && !_demo) PollGamelogs(now);

        if (_tackle.Tick(now)) EndTackle();
        IsTackled = _tackle.IsAlerting || now < _simulatedTackleUntil;

        if (Config is not null && IsConfigAuthorized && now - _lastWebhookCheck >= WebhookCheckInterval)
        {
            _lastWebhookCheck = now;
            _ = CheckWebhookAsync();
        }
    }

    private void PollGamelogs(DateTime now)
    {
        if (string.IsNullOrEmpty(CharacterName)) return;
        _logs ??= new GameLogWatcher(Settings.GamelogFolder ?? GameLogWatcher.DefaultFolder, CharacterId, CharacterName);

        foreach (var ev in _logs.Poll(now))
        {
            switch (ev)
            {
                case TackleEvent t when CanAlertTackle:
                    TackleText = $"{t.Kind} from {t.Attacker}";
                    if (_tackle.OnTackle(t.Attacker, now))
                        _ = SendAsync(AlertKind.Tackled, attacker: t.Attacker);
                    break;
                case JumpEvent j:
                    SystemName = j.ToSystem;
                    break;
            }
        }

        if (!_logs.FolderExists)
        {
            GamelogHealth = Health.Error;
            GamelogDetail = $"EVE log folder not found: {_logs.Folder}";
        }
        else if (_logs.CurrentFile is null)
        {
            GamelogHealth = Health.Unknown;
            GamelogDetail = "Waiting for a game session of this character";
        }
        else
        {
            GamelogHealth = Health.Ok;
            GamelogDetail = $"Reading {Path.GetFileName(_logs.CurrentFile)}";
        }
    }

    /// <summary>Alert unless we know the ship is docked or is not an allowed hull.</summary>
    private bool CanAlertTackle => IsConnected && IsDocked != true && (ShipTypeId == 0 || IsAllowedShip);

    private void EndTackle()
    {
        _tackle.Reset();
        IsTackled = false;
        TackleText = null;
    }

    public void SilenceTackle()
    {
        _tackle.Acknowledge();
        IsTackled = _tackle.IsAlerting;
    }

    private DateTime _simulatedTackleUntil = DateTime.MinValue;

    /// <summary>
    /// Lights the TACKLED alert for a few seconds to check it works. Kept apart from the real tracker so a real
    /// tackle during the test still sends its Discord message.
    /// </summary>
    public void SimulateTackle()
    {
        _simulatedTackleUntil = DateTime.UtcNow.AddSeconds(10);
        IsTackled = true;
        ShowToast("Test: TACKLED light on for 10 seconds. Nothing is sent to Discord.");
    }

    // ================================================================ actions

    public bool CanUseAlerts => IsConnected && Config is not null && IsConfigAuthorized;

    public async Task PanicAsync()
    {
        if (!IsRorqual) { ShowToast("PANIC is only for the Rorqual.", error: true); return; }
        await SendAsync(AlertKind.Panic);
    }

    public async Task ImOkAsync()
    {
        if (await SendAsync(AlertKind.ImOk))
        {
            EndTackle();
            ShowShipLost = false;
        }
    }

    public Task<bool> SendTestAsync() => SendAsync(AlertKind.Test);

    public async Task<bool> SendAsync(AlertKind kind, string? shipName = null, string? systemName = null, string? attacker = null)
    {
        if (Config is null)
        {
            ShowToast("No Discord configuration. Open Discord and load an IDP.config file.", error: true);
            return false;
        }
        if (!IsConfigAuthorized)
        {
            ShowToast("This Discord configuration is not for your corporation or alliance.", error: true);
            return false;
        }

        var text = MessageTemplates.Render(Config.Messages.Get(kind),
            new MessageContext(CharacterName, systemName ?? NullIfDash(SystemName), shipName ?? NullIfDash(ShipTypeName), attacker));

        // Alerts matter: retry network failures a few times.
        var attempts = kind == AlertKind.Test ? 1 : 3;
        for (var i = 1; i <= attempts; i++)
        {
            try
            {
                await _discord.SendAsync(Config.WebhookUrl, text, Config.Ping, CancellationToken.None);
                WebhookHealth = Health.Ok;
                WebhookDetail = $"Last message sent at {DateTime.Now:HH:mm:ss}";
                ShowToast(kind switch
                {
                    AlertKind.Test => "Test message sent to Discord.",
                    AlertKind.Tackled => "Tackle alert sent to Discord.",
                    AlertKind.Panic => "PANIC alert sent to Discord.",
                    AlertKind.Destroyed => "Ship loss sent to Discord.",
                    _ => "\"I'm OK\" sent to Discord.",
                });
                return true;
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && i < attempts)
            {
                await Task.Delay(TimeSpan.FromSeconds(3));
            }
            catch (Exception ex)
            {
                WebhookHealth = Health.Error;
                WebhookDetail = ex is DiscordException ? ex.Message : "Discord unreachable.";
                ShowToast($"Discord message failed: {WebhookDetail}", error: true);
                return false;
            }
        }
        return false;
    }

    private static string? NullIfDash(string s) => s == "—" ? null : s;

    private async Task CheckWebhookAsync()
    {
        if (Config is null) return;
        try
        {
            var ok = await _discord.CheckAsync(Config.WebhookUrl, CancellationToken.None);
            WebhookHealth = ok ? Health.Ok : Health.Error;
            WebhookDetail = ok ? $"Webhook reachable ({Config.OwnerLabel})" : "Discord does not know this webhook anymore. Ask for a new IDP.config.";
        }
        catch (Exception)
        {
            WebhookHealth = Health.Error;
            WebhookDetail = "Discord unreachable, retrying later.";
        }
    }

    // ================================================================ configuration

    private void UpdateConfigState()
    {
        Config = Settings.Config;
        if (Config is null)
        {
            IsConfigAuthorized = false;
            WebhookHealth = Health.Error;
            WebhookDetail = "No configuration loaded";
            return;
        }
        if (_corporationId == 0)
        {
            IsConfigAuthorized = false;
            WebhookHealth = Health.Unknown;
            WebhookDetail = "Waiting for character to check the corporation/alliance";
            return;
        }
        IsConfigAuthorized = IdpConfigValidator.IsAuthorized(Config, _corporationId, _allianceId);
        if (!IsConfigAuthorized)
        {
            WebhookHealth = Health.Error;
            WebhookDetail = $"Not authorized: the configuration is for {Config.OwnerLabel}.";
            return;
        }
        _lastWebhookCheck = DateTime.UtcNow;
        _ = CheckWebhookAsync();
    }

    /// <summary>Reads, checks and activates an IDP.config file. Returns (ok, message for the user).</summary>
    public (bool Ok, string Message) LoadConfigFile(string path)
    {
        IdpConfig config;
        try
        {
            config = IdpConfigCodec.Decode(File.ReadAllBytes(path));
        }
        catch (ConfigException ex) { return (false, ex.Message); }
        catch (IOException ex) { return (false, $"Can't read the file: {ex.Message}"); }

        var errors = IdpConfigValidator.Validate(config);
        if (errors.Count > 0) return (false, string.Join("\n", errors));

        if (_corporationId == 0)
            return (false, "Your corporation is not known yet. Wait a few seconds after connecting and try again.");

        if (!IdpConfigValidator.IsAuthorized(config, _corporationId, _allianceId))
        {
            var mine = AllianceName is null ? $"corporation {CorporationName}" : $"corporation {CorporationName}, alliance {AllianceName}";
            return (false, $"Not authorized: this configuration is for {config.OwnerLabel}, and {CharacterName} is in {mine}.");
        }

        Settings.Config = config;
        _store.Save(Settings);
        UpdateConfigState();
        return (true, $"Configuration loaded for {config.OwnerLabel}.");
    }

    /// <summary>Creates a new IDP.config, writes it to <paramref name="path"/> and activates it.</summary>
    public (bool Ok, string Message) CreateConfigFile(string path, string webhookUrl, MessageSet messages, PingSettings ping, OwnerChoice owner)
    {
        var config = new IdpConfig
        {
            WebhookUrl = webhookUrl.Trim(),
            OwnerType = owner.Type,
            OwnerId = owner.Id,
            OwnerName = owner.Name,
            Ping = ping,
            Messages = messages,
            CreatedBy = CharacterName,
            CreatedAt = DateTimeOffset.UtcNow,
        };
        var errors = IdpConfigValidator.Validate(config);
        if (errors.Count > 0) return (false, string.Join("\n", errors));

        try { File.WriteAllBytes(path, IdpConfigCodec.Encode(config)); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return (false, $"Can't write the file: {ex.Message}"); }

        Settings.Config = config;
        _store.Save(Settings);
        UpdateConfigState();
        return (true, $"Saved {Path.GetFileName(path)} for {owner.Label}. Share this file with your members.");
    }

    public void SetGamelogFolder(string? folder)
    {
        Settings.GamelogFolder = folder;
        _store.Save(Settings);
        _logs = null;
    }
}
