using System.Buffers.Binary;
using System.IO.Hashing;
using System.Security.Cryptography;

namespace Meziantou.Framework.Compression.Internals.Lzma;

/// <summary>The constants and the helpers of the XZ format (https://tukaani.org/xz/xz-file-format.txt).</summary>
internal static class XzFormat
{
    public const int StreamHeaderSize = 12;
    public const int StreamFooterSize = 12;
    public const ulong FilterDelta = 0x03;
    public const ulong FilterX86 = 0x04;
    public const ulong FilterPowerPC = 0x05;
    public const ulong FilterIA64 = 0x06;
    public const ulong FilterArm = 0x07;
    public const ulong FilterArmThumb = 0x08;
    public const ulong FilterSparc = 0x09;
    public const ulong FilterArm64 = 0x0A;
    public const ulong FilterRiscV = 0x0B;
    public const ulong FilterLzma2 = 0x21;

    /// <summary>Gets the magic bytes of the stream header.</summary>
    public static ReadOnlySpan<byte> HeaderMagic => [0xFD, (byte)'7', (byte)'z', (byte)'X', (byte)'Z', 0x00];

    /// <summary>Gets the magic bytes of the stream footer.</summary>
    public static ReadOnlySpan<byte> FooterMagic => [(byte)'Y', (byte)'Z'];

    /// <summary>Gets the size of the check of a check type (0 to 15).</summary>
    public static int GetCheckSize(int checkType) => checkType switch
    {
        0 => 0,
        <= 3 => 4,
        <= 6 => 8,
        <= 9 => 16,
        <= 12 => 32,
        _ => 64,
    };

    /// <summary>Writes a variable-length integer (7 bits per byte, little-endian).</summary>
    public static int WriteVarInt(Span<byte> destination, ulong value)
    {
        var i = 0;
        while (value >= 0x80)
        {
            destination[i++] = (byte)(value | 0x80);
            value >>= 7;
        }

        destination[i++] = (byte)value;
        return i;
    }

    /// <summary>Reads a variable-length integer of at most 9 bytes, or throws an <see cref="InvalidDataException"/>.</summary>
    public static ulong ReadVarInt(ReadOnlySpan<byte> source, ref int position)
    {
        ulong value = 0;
        for (var i = 0; i < 9; i++)
        {
            if (position >= source.Length)
                throw new InvalidDataException("The XZ data is corrupt: a number is truncated.");

            var b = source[position++];
            value |= (ulong)(b & 0x7F) << (7 * i);
            if ((b & 0x80) == 0)
            {
                if (b == 0 && i > 0)
                    throw new InvalidDataException("The XZ data is corrupt: a number is not in its shortest form.");

                return value;
            }
        }

        throw new InvalidDataException("The XZ data is corrupt: a number is too large.");
    }

    /// <summary>Reads a variable-length integer from a buffer, and appends its bytes to <paramref name="crc"/>.</summary>
    public static ulong ReadVarInt(LzmaInputBuffer input, Crc32 crc)
    {
        ulong value = 0;
        Span<byte> one = stackalloc byte[1];
        for (var i = 0; i < 9; i++)
        {
            var b = input.ReadByteOrThrow();
            one[0] = b;
            crc.Append(one);
            value |= (ulong)(b & 0x7F) << (7 * i);
            if ((b & 0x80) == 0)
            {
                if (b == 0 && i > 0)
                    throw new InvalidDataException("The XZ data is corrupt: a number is not in its shortest form.");

                return value;
            }
        }

        throw new InvalidDataException("The XZ data is corrupt: a number is too large.");
    }

    /// <summary>Computes the check of the data of a block.</summary>
    internal sealed class Check : IDisposable
    {
        private readonly int _type;
        private readonly Crc32? _crc32;
        private readonly Crc64? _crc64;
        private readonly IncrementalHash? _sha256;

        public Check(int type)
        {
            _type = type;
            switch (type)
            {
                case 1:
                    _crc32 = new Crc32();
                    break;
                case 4:
                    _crc64 = Crc64Xz.Create();
                    break;
                case 10:
                    _sha256 = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
                    break;
            }
        }

        /// <summary>Gets a value indicating whether the check type is supported (otherwise, the check is skipped).</summary>
        public bool IsSupported => _type is 0 or 1 or 4 or 10;

        public int Size => GetCheckSize(_type);

        public void Append(ReadOnlySpan<byte> data)
        {
            _crc32?.Append(data);
            _crc64?.Append(data);
            _sha256?.AppendData(data);
        }

        /// <summary>Writes the check of the data, and resets it for the next block.</summary>
        public void GetValueAndReset(Span<byte> destination)
        {
            if (_crc32 is not null)
            {
                BinaryPrimitives.WriteUInt32LittleEndian(destination, _crc32.GetCurrentHashAsUInt32());
                _crc32.Reset();
            }
            else if (_crc64 is not null)
            {
                BinaryPrimitives.WriteUInt64LittleEndian(destination, _crc64.GetCurrentHashAsUInt64());
                _crc64.Reset();
            }
            else if (_sha256 is not null)
            {
                _sha256.GetHashAndReset(destination);
            }
        }

        public void Dispose() => _sha256?.Dispose();
    }
}
