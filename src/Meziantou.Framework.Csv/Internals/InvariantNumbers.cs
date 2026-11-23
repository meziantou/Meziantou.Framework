using System.Numerics;
using System.Runtime.CompilerServices;

namespace Meziantou.Framework.Csv.Internals;

/// <summary>The values whose text has no character that can require quotes in CSV data: numbers and dates written with the invariant culture, and Boolean values.</summary>
internal static class InvariantNumbers
{
    /// <summary>
    /// The characters of the integers and the decimals in the default format of the invariant culture, of the dates and times in the round-trip
    /// format (<c>O</c>) or the constant format of <see cref="TimeSpan"/> (<c>c</c>), and of <c>true</c> and <c>false</c>.
    /// </summary>
    public const string Characters = "0123456789+-.:TZaeflrstu";

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool IsInvariant(IFormatProvider provider) => ReferenceEquals(provider, CultureInfo.InvariantCulture) || ReferenceEquals(provider, NumberFormatInfo.InvariantInfo);

    /// <summary>Gets a value indicating whether the default format of <typeparamref name="T"/> writes digits and a minus sign with the invariant culture.</summary>
    /// <remarks>
    /// The type tests fold to a constant for each value type, but they are too much IL for the JIT to inline the method without the attribute when it
    /// has no profile data (Native AOT, tiered compilation off): the calls were then about 8% of the time to write formatted values.
    /// </remarks>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool IsIntegerOrDecimal<T>()
    {
        return typeof(T) == typeof(byte) || typeof(T) == typeof(sbyte) || typeof(T) == typeof(short) || typeof(T) == typeof(ushort) ||
            typeof(T) == typeof(int) || typeof(T) == typeof(uint) || typeof(T) == typeof(long) || typeof(T) == typeof(ulong) ||
            typeof(T) == typeof(Int128) || typeof(T) == typeof(UInt128) || typeof(T) == typeof(nint) || typeof(T) == typeof(nuint) ||
            typeof(T) == typeof(BigInteger) || typeof(T) == typeof(decimal);
    }

    /// <summary>Gets a value indicating whether a value of type <typeparamref name="T"/> formatted with <paramref name="format"/> and <paramref name="provider"/> only has <see cref="Characters"/>.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool IsInvariantNumber<T>(ReadOnlySpan<char> format, IFormatProvider provider)
    {
        if (IsIntegerOrDecimal<T>())
            return format.IsEmpty && IsInvariant(provider);

        if (typeof(T) == typeof(DateTime) || typeof(T) == typeof(DateTimeOffset) || typeof(T) == typeof(DateOnly) || typeof(T) == typeof(TimeOnly))
            return format is "O" or "o";

        if (typeof(T) == typeof(TimeSpan))
            return format is "c";

        return false;
    }
}
