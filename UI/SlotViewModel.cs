using System.ComponentModel;
using System.Runtime.CompilerServices;
using SCS.Core;

namespace SCS.UI;

public sealed class SlotViewModel : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    public int Index { get; }
    public string Number => (Index + 1).ToString("00");

    private string _stratagem = "";
    private string _hotkey = "";
    private bool _isConflict;
    private bool _isFlashing;

    public SlotViewModel(int index) => Index = index;

    public string Stratagem
    {
        get => _stratagem;
        set
        {
            if (Set(ref _stratagem, value ?? ""))
            {
                OnChanged(nameof(Code));
                OnChanged(nameof(Category));
                OnChanged(nameof(IsEmpty));
                OnChanged(nameof(DisplayName));
            }
        }
    }

    public string Hotkey
    {
        get => _hotkey;
        set => Set(ref _hotkey, (value ?? "").Trim().ToLowerInvariant());
    }

    public bool IsConflict { get => _isConflict; set => Set(ref _isConflict, value); }
    public bool IsFlashing { get => _isFlashing; set => Set(ref _isFlashing, value); }

    public bool IsEmpty => Stratagems.Find(_stratagem) == null;
    public string DisplayName => IsEmpty ? "SELECT STRATAGEM" : _stratagem;
    public string Code => Stratagems.Find(_stratagem)?.Code ?? "";
    public string Category => Stratagems.Find(_stratagem)?.Category.ToUpperInvariant() ?? "EMPTY";

    private bool Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        OnChanged(name!);
        return true;
    }

    private void OnChanged(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

/// <summary>One line in the in-game overlay.</summary>
public sealed record OverlayRow(string Key, string Name, string Code);
