using System.Globalization;

namespace Meziantou.Framework.Bencode;

public sealed class BencodeInteger : BencodeValue, IEquatable<BencodeInteger>
{
    public BencodeInteger(long value)
    {
        Value = value;
    }

    public override BencodeValueKind Kind => BencodeValueKind.Integer;

    public long Value { get; }

    public bool Equals(BencodeInteger? other) => other is not null && Value == other.Value;

    public override bool Equals(object? obj) => obj is BencodeInteger other && Equals(other);

    public override int GetHashCode() => Value.GetHashCode();

    public override string ToString() => Value.ToString(CultureInfo.InvariantCulture);

    public override void WriteTo(BencodeWriter writer, bool canonical)
    {
        ArgumentNullException.ThrowIfNull(writer);
        writer.WriteInteger(Value);
    }
}
