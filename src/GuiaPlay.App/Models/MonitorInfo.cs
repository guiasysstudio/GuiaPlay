using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace GuiaPlay.App.Models;

public sealed record MonitorInfo(
    nint Handle,
    string Id,
    string? PersistentId,
    string? FriendlyName,
    bool IdentityReliable,
    int Left,
    int Top,
    int Width,
    int Height,
    int WorkLeft,
    int WorkTop,
    int WorkWidth,
    int WorkHeight,
    bool IsPrimary,
    uint DpiX,
    uint DpiY)
{
    public int Number { get; init; }

    public string? CustomName { get; init; }

    public string DeviceName => Id.Replace("\\\\.\\", string.Empty);
    public string ShortLabel => string.IsNullOrWhiteSpace(CustomName)
        ? $"Tela {Number} — {DeviceName}{(IsPrimary ? " (principal)" : string.Empty)}"
        : $"{CustomName} — Tela {Number}{(IsPrimary ? " (principal)" : string.Empty)}";
    public string Details => $"{DeviceName} · {Width} × {Height} · posição {Left}, {Top} · escala {Math.Round(DpiX / 96d * 100)}%";
    public string Label => $"{ShortLabel} — {Details}";

    public string? PersistenceKey => IdentityReliable ? PersistentId : null;
}

public sealed class MonitorChoice : INotifyPropertyChanged
{
    private bool _isSelected;

    public required MonitorInfo Monitor { get; init; }

    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (_isSelected == value)
            {
                return;
            }

            _isSelected = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsSelected)));
        }
    }

    public string Label => Monitor.Label;
    public string ShortLabel => Monitor.ShortLabel;
    public string Details => Monitor.Details;

    public event PropertyChangedEventHandler? PropertyChanged;
}
