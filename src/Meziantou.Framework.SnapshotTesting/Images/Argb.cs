namespace Meziantou.Framework.SnapshotTesting;

internal readonly struct Argb : IEquatable<Argb>
{
    private readonly uint _value;

    public Argb(uint value)
    {
        _value = value;
    }

    public Argb(byte a, byte r, byte g, byte b)
        : this((uint)(a << 24 | r << 16 | g << 8 | b))
    {
    }

    public byte A => (byte)(_value >> 24);
    public byte R => (byte)(_value >> 16);
    public byte G => (byte)(_value >> 8);
    public byte B => (byte)_value;

    internal uint PackedValue => _value;

    public bool Equals(Argb other) => _value == other._value;
    public override bool Equals([NotNullWhen(true)] object? obj) => obj is Argb other && Equals(other);
    public override int GetHashCode() => _value.GetHashCode();
    public override string ToString() => $"#{_value:X8}";

    public static bool operator ==(Argb left, Argb right) => left.Equals(right);
    public static bool operator !=(Argb left, Argb right) => !(left == right);
}
