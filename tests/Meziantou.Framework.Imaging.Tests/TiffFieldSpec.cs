using System.Buffers.Binary;

namespace Meziantou.Framework.Imaging.Tests;

/// <summary>One directory entry assembled by <see cref="TiffFileBuilder"/>.</summary>
/// <param name="Tag">The tag.</param>
/// <param name="Type">The field type.</param>
/// <param name="Values">The unsigned values, or the raw bytes for BYTE and UNDEFINED fields.</param>
internal sealed record TiffFieldSpec(ushort Tag, ushort Type, ulong[] Values)
{
    public static TiffFieldSpec Short(ushort tag, ushort value) => new(tag, 3, [value]);

    public static TiffFieldSpec Shorts(ushort tag, ushort[] values) => new(tag, 3, [.. values.Select(static value => (ulong)value)]);

    public static TiffFieldSpec Long(ushort tag, uint value) => new(tag, 4, [value]);

    public static TiffFieldSpec Bytes(ushort tag, ushort type, byte[] value) => new(tag, type, [.. value.Select(static b => (ulong)b)]);

    public static TiffFieldSpec Rational(ushort tag, uint numerator, uint denominator) => new(tag, 5, [numerator, denominator]);

    /// <summary>An override of the declared value count, for malformed files.</summary>
    public ulong? CountOverride { get; init; }

    public ulong GetCount() => CountOverride ?? (ulong)(Type == 5 ? Values.Length / 2 : Values.Length);

    public byte[] Serialize(bool bigEndian, bool bigTiff)
    {
        var size = Type switch
        {
            1 or 2 or 6 or 7 => 1,
            3 or 8 => 2,

            // RATIONAL and SRATIONAL store two 32-bit components per value
            4 or 5 or 9 or 10 or 11 or 13 => 4,
            _ => 8,
        };

        var result = new byte[size * Values.Length];
        for (var i = 0; i < Values.Length; i++)
        {
            var destination = result.AsSpan(i * size);
            switch (size)
            {
                case 1:
                    destination[0] = (byte)Values[i];
                    break;

                case 2:
                    if (bigEndian)
                    {
                        BinaryPrimitives.WriteUInt16BigEndian(destination, (ushort)Values[i]);
                    }
                    else
                    {
                        BinaryPrimitives.WriteUInt16LittleEndian(destination, (ushort)Values[i]);
                    }

                    break;

                case 4:
                    if (bigEndian)
                    {
                        BinaryPrimitives.WriteUInt32BigEndian(destination, (uint)Values[i]);
                    }
                    else
                    {
                        BinaryPrimitives.WriteUInt32LittleEndian(destination, (uint)Values[i]);
                    }

                    break;

                default:
                    if (bigEndian)
                    {
                        BinaryPrimitives.WriteUInt64BigEndian(destination, Values[i]);
                    }
                    else
                    {
                        BinaryPrimitives.WriteUInt64LittleEndian(destination, Values[i]);
                    }

                    break;
            }
        }

        _ = bigTiff;
        return result;
    }
}
