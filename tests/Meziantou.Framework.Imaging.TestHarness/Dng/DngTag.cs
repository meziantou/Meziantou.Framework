using System.Buffers.Binary;
using System.Text;

namespace Meziantou.Framework.Imaging.TestHarness.Dng;

/// <summary>A tag of a directory written by <see cref="DngFileBuilder"/>: its type, its count and its values, as numbers or bytes.</summary>
/// <param name="Tag">The tag.</param>
/// <param name="Type">The TIFF field type.</param>
/// <param name="Count">The number of values.</param>
/// <param name="Values">The values: bytes (BYTE, ASCII, UNDEFINED), or the numbers (the other types).</param>
public sealed record DngTag(ushort Tag, ushort Type, int Count, object Values)
{
    public static DngTag Scalar(ushort tag, uint value) => new(tag, 4, 1, new double[] { value });

    public static DngTag UInt32s(ushort tag, params uint[] values) => new(tag, 4, values.Length, values.Select(value => (double)value).ToArray());

    public static DngTag Shorts(ushort tag, params ushort[] values) => new(tag, 3, values.Length, values.Select(value => (double)value).ToArray());

    public static DngTag Bytes(ushort tag, byte[] values) => new(tag, 1, values.Length, values);

    public static DngTag Undefined(ushort tag, byte[] values) => new(tag, 7, values.Length, values);

    public static DngTag Ascii(ushort tag, string value)
    {
        var bytes = Encoding.UTF8.GetBytes(value + "\0");
        return new(tag, 2, bytes.Length, bytes);
    }

    public static DngTag Rationals(ushort tag, double[] values) => new(tag, 5, values.Length, values);

    public static DngTag SignedRationals(ushort tag, double[] values) => new(tag, 10, values.Length, values);

    public static DngTag Floats(ushort tag, float[] values) => new(tag, 11, values.Length, values.Select(value => (double)value).ToArray());

    public static DngTag Doubles(ushort tag, double[] values) => new(tag, 12, values.Length, values);

    /// <summary>Encodes the values in a byte order.</summary>
    public byte[] Encode(bool bigEndian)
    {
        if (Values is byte[] bytes)
            return bytes;

        var numbers = (double[])Values;
        var size = Type switch
        {
            3 or 8 => 2,
            4 or 9 or 11 => 4,
            5 or 10 or 12 => 8,
            _ => throw new InvalidOperationException("Unknown type."),
        };

        var result = new byte[numbers.Length * size];
        for (var i = 0; i < numbers.Length; i++)
        {
            var target = result.AsSpan(i * size);
            switch (Type)
            {
                case 3:
                    Write16(target, checked((ushort)numbers[i]));
                    break;
                case 8:
                    Write16(target, unchecked((ushort)checked((short)numbers[i])));
                    break;
                case 4:
                    Write32(target, checked((uint)numbers[i]));
                    break;
                case 9:
                    Write32(target, unchecked((uint)checked((int)numbers[i])));
                    break;
                case 11:
                    Write32(target, BitConverter.SingleToUInt32Bits((float)numbers[i]));
                    break;
                case 12:
                    Write64(target, BitConverter.DoubleToUInt64Bits(numbers[i]));
                    break;
                case 5:
                    Write32(target, checked((uint)Math.Round(numbers[i] * DngFileBuilder.RationalDenominator, MidpointRounding.AwayFromZero)));
                    Write32(target[4..], DngFileBuilder.RationalDenominator);
                    break;
                default:
                    Write32(target, unchecked((uint)checked((int)Math.Round(numbers[i] * DngFileBuilder.RationalDenominator, MidpointRounding.AwayFromZero))));
                    Write32(target[4..], DngFileBuilder.RationalDenominator);
                    break;
            }
        }

        return result;

        void Write16(Span<byte> target, ushort value)
        {
            if (bigEndian)
            {
                BinaryPrimitives.WriteUInt16BigEndian(target, value);
            }
            else
            {
                BinaryPrimitives.WriteUInt16LittleEndian(target, value);
            }
        }

        void Write32(Span<byte> target, uint value)
        {
            if (bigEndian)
            {
                BinaryPrimitives.WriteUInt32BigEndian(target, value);
            }
            else
            {
                BinaryPrimitives.WriteUInt32LittleEndian(target, value);
            }
        }

        void Write64(Span<byte> target, ulong value)
        {
            if (bigEndian)
            {
                BinaryPrimitives.WriteUInt64BigEndian(target, value);
            }
            else
            {
                BinaryPrimitives.WriteUInt64LittleEndian(target, value);
            }
        }
    }
}
