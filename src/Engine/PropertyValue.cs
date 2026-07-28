namespace TabletopCore.Engine;

/// <summary>
/// The value type of a card's property bag: int, string, or bool.
/// <see cref="None"/> represents an absent property, so rules can probe
/// without null checks or exceptions.
/// </summary>
public readonly struct PropertyValue : IEquatable<PropertyValue>
{
    public enum ValueKind : byte { None, Int, String, Bool }

    private readonly int _intValue;
    private readonly string? _stringValue;

    public ValueKind Kind { get; }

    private PropertyValue(ValueKind kind, int intValue, string? stringValue)
    {
        Kind = kind;
        _intValue = intValue;
        _stringValue = stringValue;
    }

    public static readonly PropertyValue None = default;

    public static PropertyValue Of(int value) => new(ValueKind.Int, value, null);
    public static PropertyValue Of(bool value) => new(ValueKind.Bool, value ? 1 : 0, null);
    public static PropertyValue Of(string value)
        => new(ValueKind.String, 0, value ?? throw new ArgumentNullException(nameof(value)));

    public static implicit operator PropertyValue(int value) => Of(value);
    public static implicit operator PropertyValue(bool value) => Of(value);
    public static implicit operator PropertyValue(string value) => Of(value);

    public bool IsNone => Kind == ValueKind.None;

    public int AsInt => Kind == ValueKind.Int
        ? _intValue
        : throw new InvalidOperationException($"Property is {Kind}, not Int.");

    public bool AsBool => Kind == ValueKind.Bool
        ? _intValue != 0
        : throw new InvalidOperationException($"Property is {Kind}, not Bool.");

    public string AsString => Kind == ValueKind.String
        ? _stringValue!
        : throw new InvalidOperationException($"Property is {Kind}, not String.");

    public bool Equals(PropertyValue other) => Kind == other.Kind && Kind switch
    {
        ValueKind.None => true,
        ValueKind.String => string.Equals(_stringValue, other._stringValue, StringComparison.Ordinal),
        _ => _intValue == other._intValue,
    };

    public override bool Equals(object? obj) => obj is PropertyValue other && Equals(other);

    public override int GetHashCode() => Kind switch
    {
        ValueKind.None => 0,
        ValueKind.String => HashCode.Combine(Kind, _stringValue),
        _ => HashCode.Combine(Kind, _intValue),
    };

    public static bool operator ==(PropertyValue left, PropertyValue right) => left.Equals(right);
    public static bool operator !=(PropertyValue left, PropertyValue right) => !left.Equals(right);

    public override string ToString() => Kind switch
    {
        ValueKind.None => "(none)",
        ValueKind.Int => _intValue.ToString(System.Globalization.CultureInfo.InvariantCulture),
        ValueKind.Bool => _intValue != 0 ? "true" : "false",
        _ => _stringValue!,
    };
}
