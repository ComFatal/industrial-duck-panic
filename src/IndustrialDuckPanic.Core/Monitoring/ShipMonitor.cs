namespace IndustrialDuckPanic.Core.Monitoring;

public sealed record ShipSnapshot(bool Online, int SystemId, bool Docked, long ShipItemId, int ShipTypeId);

public enum ShipEventKind { Docked, Undocked, ShipChanged, SystemChanged, Destroyed }

public sealed record ShipEvent(ShipEventKind Kind, ShipSnapshot Previous, ShipSnapshot Current);

/// <summary>
/// Compares successive ESI snapshots. A ship is considered destroyed when the character was flying an
/// allowed ship in space and the next snapshot shows a capsule.
/// </summary>
public sealed class ShipMonitor(Func<int, bool> isAllowedShip)
{
    public ShipSnapshot? Last { get; private set; }

    public IReadOnlyList<ShipEvent> Update(ShipSnapshot current)
    {
        var events = new List<ShipEvent>();
        var prev = Last;
        Last = current;
        if (prev is null) return events;

        if (prev.ShipItemId != current.ShipItemId)
        {
            var destroyed = !prev.Docked
                            && isAllowedShip(prev.ShipTypeId)
                            && AppConstants.CapsuleTypeIds.Contains(current.ShipTypeId);
            events.Add(new ShipEvent(destroyed ? ShipEventKind.Destroyed : ShipEventKind.ShipChanged, prev, current));
        }

        if (prev.SystemId != current.SystemId)
            events.Add(new ShipEvent(ShipEventKind.SystemChanged, prev, current));

        if (prev.Docked != current.Docked)
            events.Add(new ShipEvent(current.Docked ? ShipEventKind.Docked : ShipEventKind.Undocked, prev, current));

        return events;
    }

    public void Reset() => Last = null;
}
