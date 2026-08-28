using System.Runtime.InteropServices;

namespace Meziantou.Framework.Unix.ControlGroups;

/// <summary>Holds the value of a cgroup interface file together with the reason a value may be absent.</summary>
/// <typeparam name="T">The type of the parsed value.</typeparam>
/// <remarks>
/// A cgroup getter has four possible outcomes, and reporting them all as <see langword="null"/> makes them
/// indistinguishable: a caller cannot tell "the memory controller is not enabled" from "memory is unlimited".
/// <see cref="State"/> keeps them apart. The default value of this type is <see cref="CGroupValueState.Unavailable"/>.
/// </remarks>
[StructLayout(LayoutKind.Auto)]
public readonly struct CGroupValue<T> : IEquatable<CGroupValue<T>>
{
    private readonly T? _value;

    private CGroupValue(CGroupValueState state, T? value, string? rawValue)
    {
        State = state;
        _value = value;
        RawValue = rawValue;
    }

    /// <summary>Gets what the interface file holds.</summary>
    public CGroupValueState State { get; }

    /// <summary>Gets the content of the interface file with leading and trailing whitespace removed, or <see langword="null"/> when the file does not exist.</summary>
    public string? RawValue { get; }

    /// <summary>Gets a value indicating whether the interface file holds a value.</summary>
    [MemberNotNullWhen(returnValue: true, nameof(RawValue))]
    public bool IsConfigured => State is CGroupValueState.Configured;

    /// <summary>Gets the value held by the interface file.</summary>
    /// <exception cref="InvalidOperationException">The interface file does not hold a value. Use <see cref="State"/> or <see cref="TryGetValue"/> to handle that case.</exception>
    public T Value => IsConfigured ? _value! : throw new InvalidOperationException(GetNoValueMessage());

    /// <summary>Gets the value held by the interface file.</summary>
    /// <param name="value">The value when the interface file holds one; otherwise, the default value of <typeparamref name="T"/>.</param>
    /// <returns><see langword="true"/> when the interface file holds a value; otherwise, <see langword="false"/>.</returns>
    public bool TryGetValue([MaybeNullWhen(returnValue: false)] out T value)
    {
        if (IsConfigured)
        {
            value = _value!;
            return true;
        }

        value = default;
        return false;
    }

    /// <summary>Gets the value held by the interface file, or the default value of <typeparamref name="T"/> when it holds none.</summary>
    public T? GetValueOrDefault() => IsConfigured ? _value : default;

    /// <summary>Gets the value held by the interface file, or <paramref name="defaultValue"/> when it holds none.</summary>
    /// <param name="defaultValue">The value to return when the interface file holds no value.</param>
    public T GetValueOrDefault(T defaultValue) => IsConfigured ? _value! : defaultValue;

    internal static CGroupValue<T> Unavailable() => default;

    internal static CGroupValue<T> NotConfigured(string rawValue) => new(CGroupValueState.NotConfigured, value: default, rawValue);

    internal static CGroupValue<T> Configured(T value, string rawValue) => new(CGroupValueState.Configured, value, rawValue);

    internal static CGroupValue<T> Invalid(string rawValue) => new(CGroupValueState.Invalid, value: default, rawValue);

    private string GetNoValueMessage() => State switch
    {
        CGroupValueState.Unavailable => "The cgroup interface file does not exist. The controller is not enabled on the parent cgroup, or the kernel does not support the feature.",
        CGroupValueState.NotConfigured => "The cgroup interface file holds no limit.",
        _ => $"The cgroup interface file holds '{RawValue}', which is not a valid value.",
    };

    /// <inheritdoc />
    public bool Equals(CGroupValue<T> other)
    {
        return State == other.State
            && string.Equals(RawValue, other.RawValue, StringComparison.Ordinal)
            && EqualityComparer<T?>.Default.Equals(_value, other._value);
    }

    /// <inheritdoc />
    public override bool Equals([NotNullWhen(returnValue: true)] object? obj) => obj is CGroupValue<T> other && Equals(other);

    /// <inheritdoc />
    public override int GetHashCode() => HashCode.Combine(State, RawValue, _value);

    /// <summary>Determines whether two values are equal.</summary>
    /// <param name="left">The first value to compare.</param>
    /// <param name="right">The second value to compare.</param>
    public static bool operator ==(CGroupValue<T> left, CGroupValue<T> right) => left.Equals(right);

    /// <summary>Determines whether two values are different.</summary>
    /// <param name="left">The first value to compare.</param>
    /// <param name="right">The second value to compare.</param>
    public static bool operator !=(CGroupValue<T> left, CGroupValue<T> right) => !left.Equals(right);

    /// <summary>Returns a string representation of this value.</summary>
    public override string ToString() => State switch
    {
        CGroupValueState.Unavailable => nameof(CGroupValueState.Unavailable),
        CGroupValueState.Configured => $"{nameof(CGroupValueState.Configured)}: {_value}",
        _ => $"{State}: '{RawValue}'",
    };
}
