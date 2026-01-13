using System.Runtime.InteropServices;

namespace Meziantou.Framework;

[StructLayout(LayoutKind.Auto)]
public readonly struct Optional<T> : IEquatable<Optional<T>>
{
    public Optional()
    {
        HasValue = false;
        Value = default!;
    }

    public Optional(T value)
    {
        HasValue = true;
        Value = value;
    }

    [MemberNotNullWhen(true, nameof(Value))]
    public bool HasValue { get; }

    public T Value { get; }

    public override string? ToString()
    {
        return HasValue ? Value?.ToString() : string.Empty;
    }

    public void Deconstruct(out bool hasValue, out T value)
    {
        hasValue = HasValue;
        value = Value;
    }

    public bool Equals(Optional<T> other)
    {
        if (HasValue != other.HasValue)
            return false;
        if (!HasValue)
            return true;
        return EqualityComparer<T>.Default.Equals(Value, other.Value);
    }

    public override bool Equals(object? obj)
    {
        return obj is Optional<T> other && Equals(other);
    }

    public override int GetHashCode()
    {
        if (!HasValue)
            return 0;

        return EqualityComparer<T>.Default.GetHashCode(Value);
    }

    public static bool operator ==(Optional<T> left, Optional<T> right) => left.Equals(right);

    public static bool operator !=(Optional<T> left, Optional<T> right) => !(left == right);
}
