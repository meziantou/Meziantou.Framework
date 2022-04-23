#pragma warning disable CA5394 // Random is insecure
using System.Text;

namespace Meziantou.Framework;

public static class RandomExtensions
{
    public static T NextFromList<T>(this Random random!!, T[] array!!)
    {
        if (array.Length == 0)
            throw new ArgumentException("Array is empty.", nameof(array));

        var index = random.NextInt32(0, array.Length);
        return array[index];
    }

    public static T NextFromList<T>(this Random random!!, IList<T> list!!)
    {
        if (list.Count == 0)
            throw new ArgumentException("List is empty.", nameof(list));

        var index = random.NextInt32(0, list.Count);
        return list[index];
    }

    public static T NextFromList<T>(this Random random!!, ICollection<T> list!!)
    {
        if (list.Count == 0)
            throw new ArgumentException("List is empty.", nameof(list));

        var index = random.NextInt32(0, list.Count);
        return list.ElementAt(index);
    }

    public static T NextFromList<T>(this Random random!!, ReadOnlySpan<T> list)
    {
        if (list.Length == 0)
            throw new ArgumentException("List is empty.", nameof(list));

        var index = random.NextInt32(0, list.Length);
        return list[index];
    }

    public static T NextFromList<T>(this Random random!!, ReadOnlyMemory<T> list)
    {
        if (list.Length == 0)
            throw new ArgumentException("List is empty.", nameof(list));

        var index = random.NextInt32(0, list.Length);
        return list.Span[index];
    }

    public static T NextFromList<T>(this Random random!!, IReadOnlyList<T> list!!)
    {
        if (list.Count == 0)
            throw new ArgumentException("List is empty.", nameof(list));

        var index = random.NextInt32(0, list.Count);
        return list[index];
    }

    public static T NextFromList<T>(this Random random!!, IReadOnlyCollection<T> list!!)
    {
        if (list.Count == 0)
            throw new ArgumentException("List is empty.", nameof(list));

        var index = random.NextInt32(0, list.Count);
        return list.ElementAt(index);
    }

    public static bool NextBoolean(this Random random!!)
    {
        return random.Next(0, 2) != 0;
    }

    public static byte NextByte(this Random random!!, byte min = 0, byte max = byte.MaxValue)
    {
        return (byte)random.Next(min, max);
    }

    public static sbyte NextSByte(this Random random!!, sbyte min = 0, sbyte max = sbyte.MaxValue)
    {
        return (sbyte)random.Next(min, max);
    }

    public static DateTime NextDateTime(this Random random!!, DateTime min, DateTime max)
    {
        var diff = max.Ticks - min.Ticks;
        var range = (long)(diff * random.NextDouble());

        return min + new TimeSpan(range);
    }

    public static double NextDouble(this Random random!!, double min = 0D, double max = 1D)
    {
        return (random.NextDouble() * (max - min)) + min;
    }

    public static short NextInt16(this Random random!!, short min = 0, short max = short.MaxValue)
    {
        return (short)random.Next(min, max);
    }

    public static int NextInt32(this Random random!!, int min = 0, int max = int.MaxValue)
    {
        return random.Next(min, max);
    }

    public static long NextInt64(this Random random!!, long min = 0L, long max = long.MaxValue)
    {
        if (min == max)
        {
            return min;
        }

        return (long)((random.NextDouble() * (max - min)) + min);
    }

    public static float NextSingle(this Random random!!, float min = 0f, float max = 1f)
    {
        return (float)random.NextDouble(min, max);
    }

    public static ushort NextUInt16(this Random random!!, ushort min = 0, ushort max = ushort.MaxValue)
    {
        return (ushort)random.Next(min, max);
    }

    public static uint NextUInt32(this Random random!!, uint min = 0u, uint max = uint.MaxValue)
    {
        return (uint)random.NextInt64(min, max);
    }

    public static ulong NextUInt64(this Random random!!, ulong min = 0ul, ulong max = ulong.MaxValue)
    {
        var buffer = new byte[sizeof(long)];
        random.NextBytes(buffer);
        return (BitConverter.ToUInt64(buffer, 0) * (max - min) / ulong.MaxValue) + min;
    }

    public static decimal NextDecimal(this Random random!!, decimal min = decimal.MinValue, decimal max = decimal.MaxValue)
    {
        return (((decimal)random.NextDouble()) * (max - min)) + min;
    }

    public static string NextString(this Random random, int length, string chars)
    {
        return NextString(random, length, length, chars);
    }

    public static string NextString(this Random random!!, int minLength, int maxLength, string chars!!)
    {
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
