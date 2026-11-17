using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace IndustrialDuckPanic.ViewModels;

public abstract class ObservableObject : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    protected void Raise([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    protected bool Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        Raise(name);
        return true;
    }
}

public enum Health { Unknown, Ok, Error }

public enum SwitchPosition { Middle, Off, On }

public sealed record ShipEntry(int TypeId, string Name, bool HasPanic)
{
    public string IconUrl => $"https://images.evetech.net/types/{TypeId}/icon?size=64";
}

public sealed record OwnerChoice(Core.Config.OwnerType Type, int Id, string Name)
{
    public string Label => $"{(Type == Core.Config.OwnerType.Alliance ? "Alliance" : "Corporation")}: {Name}";
}
