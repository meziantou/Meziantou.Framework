using System;
using System.Collections.Generic;
using System.Text;

namespace Meziantou.Framework
{
    public static class RandomExtensions
    {
        public static T NextFromArray<T>(this Random random, T[] array)
        {
            if (random == null)
                throw new ArgumentNullException(nameof(random));
            if (array == null)
                throw new ArgumentNullException(nameof(array));

            if (array.Length == 0)
                throw new ArgumentException("Array is empty.", nameof(array));

            var index = random.NextInt32(0, array.Length);
            return array[index];
        }

        public static T NextFromList<T>(this Random random, IList<T> list)
        {
            if (random == null)
                throw new ArgumentNullException(nameof(random));
            if (list == null)
                throw new ArgumentNullException(nameof(list));

            if (list.Count == 0)
                throw new ArgumentException("List is empty.", nameof(list));

            var index = random.NextInt32(0, list.Count);
            return list[index];
        }

        public static T NextFromList<T>(this Random random, IReadOnlyList<T> list)
        {
            if (random == null)
                throw new ArgumentNullException(nameof(random));
            if (list == null)
                throw new ArgumentNullException(nameof(list));

            if (list.Count == 0)
                throw new ArgumentException("List is empty.", nameof(list));

            var index = random.NextInt32(0, list.Count);
            return list[index];
        }

        public static bool NextBoolean(this Random random)
        {
            if (random == null)
                throw new ArgumentNullException(nameof(random));

            return random.Next(0, 2) != 0;
        }

        public static byte NextByte(this Random random, byte min = 0, byte max = byte.MaxValue)
        {
            if (random == null)
                throw new ArgumentNullException(nameof(random));

            return (byte)random.Next(min, max);
        }

        public static sbyte NextSByte(this Random random, sbyte min = 0, sbyte max = sbyte.MaxValue)
        {
            if (random == null)
                throw new ArgumentNullException(nameof(random));

            return (sbyte)random.Next(min, max);
        }

        public static byte[] NextBytes(this Random random, byte[] bytes)
        {
            if (random == null)
                throw new ArgumentNullException(nameof(random));

            random.NextBytes(bytes);

            return bytes;
        }

        public static DateTime NextDateTime(this Random random, DateTime min, DateTime max)
        {
            if (random == null)
                throw new ArgumentNullException(nameof(random));

            var diff = max.Ticks - min.Ticks;
            var range = (long)(diff * random.NextDouble());

            return min + new TimeSpan(range);
        }

        public static double NextDouble(this Random random, double min = 0D, double max = 1D)
        {
            if (random == null)
                throw new ArgumentNullException(nameof(random));

            return (random.NextDouble() * (max - min)) + min;
        }

        public static short NextInt16(this Random random, short min = 0, short max = short.MaxValue)
        {
            if (random == null)
                throw new ArgumentNullException(nameof(random));

            return (short)random.Next(min, max);
        }

        public static int NextInt32(this Random random, int min = 0, int max = int.MaxValue)
        {
            if (random == null)
                throw new ArgumentNullException(nameof(random));

            return random.Next(min, max);
        }

        public static long NextInt64(this Random random, long min = 0L, long max = long.MaxValue)
        {
            if (random == null)
                throw new ArgumentNullException(nameof(random));

            if (min == max)
            {
                return min;
            }

            return (long)((random.NextDouble() * (max - min)) + min);
        }

        public static float NextSingle(this Random random, float min = 0f, float max = 1f)
        {
            if (random == null)
                throw new ArgumentNullException(nameof(random));

            return (float)random.NextDouble(min, max);
        }

        public static ushort NextUInt16(this Random random, ushort min = 0, ushort max = ushort.MaxValue)
        {
            if (random == null)
                throw new ArgumentNullException(nameof(random));

            return (ushort)random.Next(min, max);
        }

        public static uint NextUInt32(this Random random, uint min = 0u, uint max = uint.MaxValue)
        {
            if (random == null)
                throw new ArgumentNullException(nameof(random));

            return (uint)random.NextInt64(min, max);
        }

        public static ulong NextUInt64(this Random random, ulong min = 0ul, ulong max = ulong.MaxValue)
        {
            if (random == null)
                throw new ArgumentNullException(nameof(random));

            var buffer = new byte[sizeof(long)];
            random.NextBytes(buffer);
            return (BitConverter.ToUInt64(buffer, 0) * (max - min) / ulong.MaxValue) + min;
        }

        public static decimal NextDecimal(this Random random, decimal min = decimal.MinValue, decimal max = decimal.MaxValue)
        {
            if (random == null)
                throw new ArgumentNullException(nameof(random));

            return (((decimal)random.NextDouble()) * (max - min)) + min;
        }

        public static string NextString(this Random random, int length, string chars)
        {
            return NextString(random, length, length, chars);
        }

        public static string NextString(this Random random, int minLength, int maxLength, string chars)
        {
            if (random == null)
                throw new ArgumentNullException(nameof(random));
            if (chars == null)
                throw new ArgumentNullException(nameof(chars));

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
}
