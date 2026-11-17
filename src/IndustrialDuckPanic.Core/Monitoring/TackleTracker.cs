namespace IndustrialDuckPanic.Core.Monitoring;

/// <summary>
/// Turns repeated tackle log lines into tackle episodes.
/// One episode = one Discord message. It ends after <see cref="Timeout"/> without a new line, or on dock/death.
/// Acknowledging only silences the alert light for the current episode.
/// </summary>
public sealed class TackleTracker(TimeSpan timeout)
{
    public TimeSpan Timeout { get; } = timeout;

    public bool InEpisode { get; private set; }
    public bool Acknowledged { get; private set; }
    public DateTime LastSeenUtc { get; private set; }
    public string? LastAttacker { get; private set; }

    /// <summary>True while the TACKLED light should blink.</summary>
    public bool IsAlerting => InEpisode && !Acknowledged;

    /// <summary>
    /// Call when a tackle line is read. Uses the read time, not the log time, so a wrong PC clock does not matter.
    /// Returns true when this line starts a new episode (send the Discord message).
    /// </summary>
    public bool OnTackle(string attacker, DateTime nowUtc)
    {
        LastAttacker = attacker;
        LastSeenUtc = nowUtc;
        if (InEpisode) return false;
        InEpisode = true;
        Acknowledged = false;
        return true;
    }

    /// <summary>Returns true when the episode just ended by timeout.</summary>
    public bool Tick(DateTime nowUtc)
    {
        if (!InEpisode || nowUtc - LastSeenUtc < Timeout) return false;
        Reset();
        return true;
    }

    public void Acknowledge()
    {
        if (InEpisode) Acknowledged = true;
    }

    /// <summary>Docked, destroyed or disconnected.</summary>
    public void Reset()
    {
        InEpisode = false;
        Acknowledged = false;
    }
}
