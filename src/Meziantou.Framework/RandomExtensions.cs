#pragma warning disable CA5394 // Random is insecure
using System.Runtime.CompilerServices;

namespace Meziantou.Framework;

/// <summary>
/// Provides extension methods for <see cref="Random"/> to generate various types of random values.
/// </summary>
public static class RandomExtensions
{
    // List<T>, Collection<T> or ImmutableArray<T> implement both IList<T> and IReadOnlyList<T>, and HashSet<T>
    // implements both ICollection<T> and IReadOnlyCollection<T>. The priorities pick the overload with the
    // fastest access instead of reporting an ambiguous call (CS0121).

    /// <summary>Returns a random element from an array.</summary>
    [OverloadResolutionPriority(3)]
    public static T NextFromList<T>(this Random random, T[] array)
    {
        ArgumentNullException.ThrowIfNull(random);
        ArgumentNullException.ThrowIfNull(array);

        if (array.Length is 0)
            throw new ArgumentException("Array is empty.", nameof(array));

        var index = random.NextInt32(0, array.Length);
        return array[index];
    }

    /// <summary>Returns a random element from a list.</summary>
    [OverloadResolutionPriority(2)]
    public static T NextFromList<T>(this Random random, IList<T> list)
    {
        ArgumentNullException.ThrowIfNull(random);
        ArgumentNullException.ThrowIfNull(list);

        if (list.Count is 0)
            throw new ArgumentException("List is empty.", nameof(list));

        var index = random.NextInt32(0, list.Count);
        return list[index];
    }

    /// <summary>Returns a random element from a collection.</summary>
    /// <remarks>When the collection is not a list, the element is found by enumerating the collection.</remarks>
    public static T NextFromList<T>(this Random random, ICollection<T> list)
    {
        ArgumentNullException.ThrowIfNull(random);
        ArgumentNullException.ThrowIfNull(list);

        if (list.Count is 0)
            throw new ArgumentException("List is empty.", nameof(list));

        var index = random.NextInt32(0, list.Count);
        return list.ElementAt(index);
    }

    /// <summary>Returns a random element from a span.</summary>
    public static T NextFromList<T>(this Random random, ReadOnlySpan<T> list)
    {
        ArgumentNullException.ThrowIfNull(random);

        if (list.Length is 0)
            throw new ArgumentException("List is empty.", nameof(list));

        var index = random.NextInt32(0, list.Length);
        return list[index];
    }

    /// <summary>Returns a random element from a memory.</summary>
    public static T NextFromList<T>(this Random random, ReadOnlyMemory<T> list)
    {
        ArgumentNullException.ThrowIfNull(random);

        if (list.Length is 0)
            throw new ArgumentException("List is empty.", nameof(list));

        var index = random.NextInt32(0, list.Length);
        return list.Span[index];
    }

    /// <summary>Returns a random element from a read-only list.</summary>
    [OverloadResolutionPriority(1)]
    public static T NextFromList<T>(this Random random, IReadOnlyList<T> list)
    {
        ArgumentNullException.ThrowIfNull(random);
        ArgumentNullException.ThrowIfNull(list);

        if (list.Count is 0)
            throw new ArgumentException("List is empty.", nameof(list));

        var index = random.NextInt32(0, list.Count);
        return list[index];
    }

    /// <summary>Returns a random element from a read-only collection.</summary>
    /// <remarks>When the collection is not a list, the element is found by enumerating the collection.</remarks>
    [OverloadResolutionPriority(-1)]
    public static T NextFromList<T>(this Random random, IReadOnlyCollection<T> list)
    {
        ArgumentNullException.ThrowIfNull(random);
        ArgumentNullException.ThrowIfNull(list);

        if (list.Count is 0)
            throw new ArgumentException("List is empty.", nameof(list));

        var index = random.NextInt32(0, list.Count);
        if (list is IReadOnlyList<T> readOnlyList)
            return readOnlyList[index];

        return list.ElementAt(index);
    }

    /// <summary>Returns a random boolean value.</summary>
    public static bool NextBoolean(this Random random)
    {
        ArgumentNullException.ThrowIfNull(random);

        return random.Next(0, 2) != 0;
    }

    /// <summary>Returns a random byte value, from <see cref="byte.MinValue"/> to <see cref="byte.MaxValue"/>, both included.</summary>
    public static byte NextByte(this Random random)
    {
        ArgumentNullException.ThrowIfNull(random);

        return (byte)random.Next(byte.MinValue, byte.MaxValue + 1);
    }

    /// <summary>Returns a random byte value greater than or equal to <paramref name="min"/> and less than <paramref name="max"/>.</summary>
    public static byte NextByte(this Random random, byte min, byte max)
    {
        ArgumentNullException.ThrowIfNull(random);

        return (byte)random.Next(min, max);
    }

    /// <summary>Returns a random signed byte value, from <see cref="sbyte.MinValue"/> to <see cref="sbyte.MaxValue"/>, both included.</summary>
    public static sbyte NextSByte(this Random random)
    {
        ArgumentNullException.ThrowIfNull(random);

        return (sbyte)random.Next(sbyte.MinValue, sbyte.MaxValue + 1);
    }

    /// <summary>Returns a random signed byte value greater than or equal to <paramref name="min"/> and less than <paramref name="max"/>.</summary>
    public static sbyte NextSByte(this Random random, sbyte min, sbyte max)
    {
        ArgumentNullException.ThrowIfNull(random);

        return (sbyte)random.Next(min, max);
    }

    /// <summary>Returns a random <see cref="DateTime"/> value greater than or equal to <paramref name="min"/> and less than <paramref name="max"/>.</summary>
    /// <remarks>The result has the <see cref="DateTime.Kind"/> of <paramref name="min"/>.</remarks>
    public static DateTime NextDateTime(this Random random, DateTime min, DateTime max)
    {
        ArgumentNullException.ThrowIfNull(random);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(min.Ticks, max.Ticks, nameof(min));

        // Draw the ticks as an integer: scaling the range by a double loses precision for ranges longer
        // than 2^53 ticks (about 28 years), and could round up to max.
        return min.AddTicks(random.NextInt64(0, max.Ticks - min.Ticks));
    }

    /// <summary>Returns a random double value greater than or equal to <paramref name="min"/> and less than <paramref name="max"/>.</summary>
    public static double NextDouble(this Random random, double min = 0D, double max = 1D)
    {
        ArgumentNullException.ThrowIfNull(random);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(min, max);

        if (min == max)
            return min;

        // Interpolate instead of scaling (max - min), which overflows to infinity for ranges wider than double.MaxValue
        var ratio = random.NextDouble();
        var result = (min * (1D - ratio)) + (max * ratio);

        // Rounding can reach max, which is excluded
        return result < max ? result : Math.BitDecrement(max);
    }

    /// <summary>Returns a random 16-bit signed integer, from <see cref="short.MinValue"/> to <see cref="short.MaxValue"/>, both included.</summary>
    public static short NextInt16(this Random random)
    {
        ArgumentNullException.ThrowIfNull(random);

        return (short)random.Next(short.MinValue, short.MaxValue + 1);
    }

    /// <summary>Returns a random 16-bit signed integer greater than or equal to <paramref name="min"/> and less than <paramref name="max"/>.</summary>
    public static short NextInt16(this Random random, short min, short max)
    {
        ArgumentNullException.ThrowIfNull(random);

        return (short)random.Next(min, max);
    }

    /// <summary>Returns a random 32-bit signed integer within the specified range.</summary>
    public static int NextInt32(this Random random, int min = 0, int max = int.MaxValue)
    {
        ArgumentNullException.ThrowIfNull(random);

        return random.Next(min, max);
    }

    /// <summary>Returns a random 64-bit signed integer greater than or equal to <paramref name="min"/> and less than <paramref name="max"/>.</summary>
    /// <remarks>
    /// <see cref="Random.NextInt64()"/>, <see cref="Random.NextInt64(long)"/> and <see cref="Random.NextInt64(long, long)"/> are instance
    /// methods, so <c>random.NextInt64(...)</c> never calls this extension method. This method calls <see cref="Random.NextInt64(long, long)"/>.
    /// </remarks>
    [Obsolete("Use Random.NextInt64. The instance methods of Random take precedence over this extension method.")]
    public static long NextInt64(this Random random, long min = 0L, long max = long.MaxValue)
    {
        ArgumentNullException.ThrowIfNull(random);

        return random.NextInt64(min, max);
    }

    /// <summary>Returns a random single-precision floating point value greater than or equal to <paramref name="min"/> and less than <paramref name="max"/>.</summary>
    /// <remarks><see cref="Random.NextSingle()"/> is an instance method, so <c>random.NextSingle()</c> calls it instead of this extension method. It returns the same range.</remarks>
    public static float NextSingle(this Random random, float min = 0f, float max = 1f)
    {
        ArgumentNullException.ThrowIfNull(random);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(min, max);

        if (min == max)
            return min;

        var result = (float)random.NextDouble(min, max);

        // Converting to float can round up to max, which is excluded
        return result < max ? result : MathF.BitDecrement(max);
    }

    /// <summary>Returns a random 16-bit unsigned integer, from <see cref="ushort.MinValue"/> to <see cref="ushort.MaxValue"/>, both included.</summary>
    public static ushort NextUInt16(this Random random)
    {
        ArgumentNullException.ThrowIfNull(random);

        return (ushort)random.Next(ushort.MinValue, ushort.MaxValue + 1);
    }

    /// <summary>Returns a random 16-bit unsigned integer greater than or equal to <paramref name="min"/> and less than <paramref name="max"/>.</summary>
    public static ushort NextUInt16(this Random random, ushort min, ushort max)
    {
        ArgumentNullException.ThrowIfNull(random);

        return (ushort)random.Next(min, max);
    }

    /// <summary>Returns a random 32-bit unsigned integer, from <see cref="uint.MinValue"/> to <see cref="uint.MaxValue"/>, both included.</summary>
    public static uint NextUInt32(this Random random)
    {
        ArgumentNullException.ThrowIfNull(random);

        return (uint)random.NextInt64(uint.MinValue, (long)uint.MaxValue + 1);
    }

    /// <summary>Returns a random 32-bit unsigned integer greater than or equal to <paramref name="min"/> and less than <paramref name="max"/>.</summary>
    public static uint NextUInt32(this Random random, uint min, uint max)
    {
        ArgumentNullException.ThrowIfNull(random);

        return (uint)random.NextInt64(min, max);
    }

    /// <summary>Returns a random 64-bit unsigned integer, from <see cref="ulong.MinValue"/> to <see cref="ulong.MaxValue"/>, both included.</summary>
    public static ulong NextUInt64(this Random random)
    {
        ArgumentNullException.ThrowIfNull(random);

        Span<byte> buffer = stackalloc byte[sizeof(ulong)];
        random.NextBytes(buffer);
        return BitConverter.ToUInt64(buffer);
    }

    /// <summary>Returns a random 64-bit unsigned integer greater than or equal to <paramref name="min"/> and less than <paramref name="max"/>.</summary>
    public static ulong NextUInt64(this Random random, ulong min, ulong max)
    {
        ArgumentNullException.ThrowIfNull(random);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(min, max);

        if (min == max)
            return min;

        Span<byte> buffer = stackalloc byte[sizeof(ulong)];
        random.NextBytes(buffer);
        var value = BitConverter.ToUInt64(buffer);

        // Scale with a 128-bit intermediate. Multiplying in 64 bits overflows for any non-tiny
        // range, and the wrapped product divided by ulong.MaxValue collapses to min.
        return min + (ulong)(((UInt128)value * (max - min)) >> 64);
    }

    /// <summary>Returns a random decimal value greater than or equal to <paramref name="min"/> and less than <paramref name="max"/>.</summary>
    /// <remarks>The value is derived from <see cref="Random.NextDouble"/>, so it carries the granularity of a double rather than that of a decimal.</remarks>
    public static decimal NextDecimal(this Random random, decimal min = decimal.MinValue, decimal max = decimal.MaxValue)
    {
        ArgumentNullException.ThrowIfNull(random);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(min, max);

        if (min == max)
            return min;

        // Interpolate instead of scaling (max - min): the default range is twice decimal.MaxValue,
        // so computing the difference overflows. Both terms below stay within the decimal range.
        var ratio = (decimal)random.NextDouble();
        return (min * (1m - ratio)) + (max * ratio);
    }

    /// <summary>Returns a random string of the specified length using characters from the provided character set.</summary>
    public static string NextString(this Random random, int length, string chars)
    {
        return NextString(random, length, length, chars);
    }

    /// <summary>Returns a random string with length between the specified range using characters from the provided character set.</summary>
    public static string NextString(this Random random, int minLength, int maxLength, string chars)
    {
        ArgumentNullException.ThrowIfNull(random);
        ArgumentNullException.ThrowIfNull(chars);

        var length = minLength + random.Next(0, maxLength - minLength + 1); // length of the string

        var max = chars.Length; // number of available characters
        var sb = new StringBuilder(length);
        for (var i = 0; i < length; i++)
        {
            sb.Append(chars[random.Next(0, max)]);
        }

        return sb.ToString();
    }
}
