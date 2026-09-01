using System.ComponentModel;
using System.Runtime.CompilerServices;
using BandwidthTester.Core;

namespace BandwidthTester.AvaloniaGui;

/// <summary>Editable row for one header field, used by the header-struct editor grid.</summary>
public sealed class HeaderFieldRowViewModel : INotifyPropertyChanged
{
    private string _name = "field";
    private HeaderFieldType _type = HeaderFieldType.UInt32;
    private int _size = 4;
    private HeaderFieldAuto _auto = HeaderFieldAuto.None;
    private string _value = "0";

    public event PropertyChangedEventHandler? PropertyChanged;

    public string Name
    {
        get => _name;
        set { _name = value; Raise(); }
    }

    public HeaderFieldType Type
    {
        get => _type;
        set
        {
            _type = value;
            int fixedSize = HeaderFieldDefinition.FixedSizeFor(value);
            if (fixedSize >= 0)
                Size = fixedSize; // also raises PropertyChanged(Size)
            Raise();
        }
    }

    /// <summary>Only meaningful (and editable in the UI) when <see cref="Type"/> is <see cref="HeaderFieldType.Bytes"/>.</summary>
    public int Size
    {
        get => _size;
        set { _size = value; Raise(); }
    }

    public HeaderFieldAuto Auto
    {
        get => _auto;
        set { _auto = value; Raise(); Raise(nameof(IsValueEditable)); }
    }

    /// <summary>Fixed value text; ignored (but kept) when <see cref="Auto"/> is not None.</summary>
    public string Value
    {
        get => _value;
        set { _value = value; Raise(); }
    }

    public bool IsValueEditable => Auto == HeaderFieldAuto.None;

    // Instance properties (not static) so plain {Binding TypeOptions}/{Binding AutoOptions}
    // resolves from each row's DataContext without needing an x:Static markup extension.
    private static readonly Array TypeOptionsShared = Enum.GetValues(typeof(HeaderFieldType));
    private static readonly Array AutoOptionsShared = Enum.GetValues(typeof(HeaderFieldAuto));

    /// <summary>Bound as the ItemsSource for the Type ComboBox in the header field editor.</summary>
    public Array TypeOptions => TypeOptionsShared;

    /// <summary>Bound as the ItemsSource for the Auto ComboBox in the header field editor.</summary>
    public Array AutoOptions => AutoOptionsShared;

    public static HeaderFieldRowViewModel FromDefinition(HeaderFieldDefinition def)
    {
        // def.Size may still be 0 for a fixed-size type if the definition was never
        // Validate()-d yet (e.g. a freshly constructed default profile) - resolve it
        // from the type so the editor always shows the real byte size.
        int fixedSize = HeaderFieldDefinition.FixedSizeFor(def.Type);
        return new HeaderFieldRowViewModel
        {
            _name = def.Name,
            _type = def.Type,
            _size = fixedSize >= 0 ? fixedSize : def.Size,
            _auto = def.Auto,
            _value = def.Value
        };
    }

    public HeaderFieldDefinition ToDefinition() => new()
    {
        Name = Name,
        Type = Type,
        Size = Size,
        Auto = Auto,
        Value = Value
    };

    private void Raise([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
