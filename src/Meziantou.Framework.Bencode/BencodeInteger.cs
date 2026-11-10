using System.Numerics;

namespace Meziantou.Framework.Bencode;

/// <summary>A bencode integer. Bencode does not bound integers, so a value can be larger than a <see cref="long"/>.</summary>
public sealed class BencodeInteger : BencodeValue, IEquatable<BencodeInteger>
{
    // A value that fits in a long is stored in _value only, so the common case does not allocate a BigInteger
    private readonly long _value;
    private readonly BigInteger? _bigValue;

    public BencodeInteger(long value)
    {
        _value = value;
    }

    public BencodeInteger(BigInteger value)
    {
        if (value >= long.MinValue && value <= long.MaxValue)
        {
            _value = (long)value;
        }
        else
        {
            _bigValue = value;
        }
    }

    public override BencodeValueKind Kind => BencodeValueKind.Integer;

    /// <summary>Gets the value as a <see cref="long"/>.</summary>
    /// <exception cref="InvalidOperationException">The value does not fit in a <see cref="long"/>. Use <see cref="BigIntegerValue"/> or <see cref="TryGetInt64"/>.</exception>
    public long Value => _bigValue is null ? _value : throw new InvalidOperationException("The bencode integer does not fit in an Int64. Use BigIntegerValue instead.");

    /// <summary>Gets the value, whatever its size.</summary>
    public BigInteger BigIntegerValue => _bigValue ?? _value;

    /// <summary>Gets a value indicating whether the value fits in a <see cref="long"/>.</summary>
    public bool IsInt64 => _bigValue is null;

    /// <summary>Gets the value as a <see cref="long"/> when it fits in one.</summary>
    public bool TryGetInt64(out long value)
    {
        value = _value;
        return _bigValue is null;
    }

    public bool Equals([NotNullWhen(true)] BencodeInteger? other) => other is not null && _value == other._value && Nullable.Equals(_bigValue, other._bigValue);

    public override bool Equals([NotNullWhen(true)] object? obj) => obj is BencodeInteger other && Equals(other);

    public override int GetHashCode() => _bigValue?.GetHashCode() ?? _value.GetHashCode();

    public override string ToString() => _bigValue?.ToString(CultureInfo.InvariantCulture) ?? _value.ToString(CultureInfo.InvariantCulture);

    public override void WriteTo(BencodeWriter writer, bool canonical)
    {
        ArgumentNullException.ThrowIfNull(writer);
        if (_bigValue is { } bigValue)
        {
            writer.WriteInteger(bigValue);
        }
        else
        {
            writer.WriteInteger(_value);
        }
    }
}
