namespace Meziantou.Framework.Assertions;

/// <summary>
/// Wraps a value so it can be used as a <see cref="Dictionary{TKey, TValue}"/> key even when the value is null,
/// which <see cref="Dictionary{TKey, TValue}"/> does not allow.
/// </summary>
/// <remarks>
/// The <see cref="IEquatable{T}"/> implementation uses <see cref="EqualityComparer{T}.Default"/> so a dictionary
/// built without an explicit comparer stays on the devirtualized path. Use <see cref="NullableKeyComparer{T}"/>
/// to compare with a caller-supplied comparer instead.
/// </remarks>
internal readonly struct NullableKey<T>(T value) : IEquatable<NullableKey<T>>
{
    public T Value { get; } = value;

    public bool Equals(NullableKey<T> other) => EqualityComparer<T>.Default.Equals(Value, other.Value);

    public override bool Equals([NotNullWhen(true)] object? obj) => obj is NullableKey<T> other && Equals(other);

    public override int GetHashCode() => Value is null ? 0 : EqualityComparer<T>.Default.GetHashCode(Value);
}
