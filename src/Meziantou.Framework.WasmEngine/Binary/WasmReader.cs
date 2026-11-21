using System.Runtime.InteropServices;
using System.Text;

namespace Meziantou.Framework.WasmEngine;

/// <summary>A malformed or an invalid module (https://webassembly.github.io/spec/core/binary/index.html).</summary>
#pragma warning disable CA1032, CA1064 // Internal: converted to a WasmCompileException
internal sealed class WasmDecodeException(string message) : Exception(message);
#pragma warning restore CA1032, CA1064

/// <summary>Reads the binary format (https://webassembly.github.io/spec/core/binary/values.html).</summary>
[StructLayout(LayoutKind.Auto)]
internal struct WasmReader
{
    private static readonly UTF8Encoding StrictUtf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    private readonly byte[] _bytes;

    public WasmReader(byte[] bytes, int position, int end)
    {
        _bytes = bytes;
        Position = position;
        End = end;
    }

    public int Position { get; set; }

    public int End { get; }

    public readonly byte[] Bytes => _bytes;

    public readonly bool AtEnd => Position >= End;

    public byte ReadByte()
    {
        if (Position >= End)
            throw new WasmDecodeException("unexpected end");

        return _bytes[Position++];
    }

    public readonly byte PeekByte()
    {
        if (Position >= End)
            throw new WasmDecodeException("unexpected end");

        return _bytes[Position];
    }

    public ReadOnlySpan<byte> ReadBytes(int count)
    {
        if (count < 0 || count > End - Position)
            throw new WasmDecodeException("unexpected end");

        var result = _bytes.AsSpan(Position, count);
        Position += count;
        return result;
    }

    public uint ReadU32()
    {
        uint result = 0;
        var shift = 0;
        while (true)
        {
            var b = ReadByte();
            if (shift == 28)
            {
                // The last byte: 4 bits of the value, the other bits must be zero (and no continuation)
                if ((b & 0x80) != 0)
                    throw new WasmDecodeException("integer representation too long");

                if ((b & 0x70) != 0)
                    throw new WasmDecodeException("integer too large");

                return result | ((uint)b << 28);
            }

            result |= (uint)(b & 0x7F) << shift;
            if ((b & 0x80) == 0)
                return result;

            shift += 7;
        }
    }

    public ulong ReadU64()
    {
        ulong result = 0;
        var shift = 0;
        while (true)
        {
            var b = ReadByte();
            if (shift == 63)
            {
                if ((b & 0x80) != 0)
                    throw new WasmDecodeException("integer representation too long");

                if ((b & 0x7E) != 0)
                    throw new WasmDecodeException("integer too large");

                return result | ((ulong)b << 63);
            }

            result |= (ulong)(b & 0x7F) << shift;
            if ((b & 0x80) == 0)
                return result;

            shift += 7;
        }
    }

    public int ReadS32()
    {
        long result = 0;
        var shift = 0;
        while (true)
        {
            var b = ReadByte();
            if (shift == 28)
            {
                if ((b & 0x80) != 0)
                    throw new WasmDecodeException("integer representation too long");

                // The unused bits must be the sign extension of bit 3
                var high = b & 0x70;
                if (high != 0 && high != 0x70 || ((b & 0x08) == 0) != (high == 0))
                    throw new WasmDecodeException("integer too large");

                result |= (long)(b & 0x0F) << 28;
                return (int)(result << 32 >> 32);
            }

            result |= (long)(b & 0x7F) << shift;
            shift += 7;
            if ((b & 0x80) == 0)
            {
                if ((b & 0x40) != 0)
                {
                    result |= -1L << shift;
                }

                return (int)result;
            }
        }
    }

    public long ReadS64()
    {
        long result = 0;
        var shift = 0;
        while (true)
        {
            var b = ReadByte();
            if (shift == 63)
            {
                if ((b & 0x80) != 0)
                    throw new WasmDecodeException("integer representation too long");

                if (b != 0 && b != 0x7F)
                    throw new WasmDecodeException("integer too large");

                return result | ((long)b << 63);
            }

            result |= (long)(b & 0x7F) << shift;
            shift += 7;
            if ((b & 0x80) == 0)
            {
                if (shift < 64 && (b & 0x40) != 0)
                {
                    result |= -1L << shift;
                }

                return result;
            }
        }
    }

    /// <summary>Reads a signed 33-bit integer (the type index or the abstract heap type of a heap type or a block type).</summary>
    public long ReadS33()
    {
        long result = 0;
        var shift = 0;
        while (true)
        {
            var b = ReadByte();
            if (shift == 28)
            {
                if ((b & 0x80) != 0)
                    throw new WasmDecodeException("integer representation too long");

                // 5 bits of the value (bit 4 is the sign), the other 2 bits are its sign extension
                var high = b & 0x60;
                var sign = (b & 0x10) != 0;
                if (sign ? high != 0x60 : high != 0)
                    throw new WasmDecodeException("integer too large");

                result |= (long)(b & 0x1F) << 28;
                return result << 31 >> 31;
            }

            result |= (long)(b & 0x7F) << shift;
            shift += 7;
            if ((b & 0x80) == 0)
            {
                if ((b & 0x40) != 0)
                {
                    result |= -1L << shift;
                }

                return result;
            }
        }
    }

    public uint ReadF32Bits()
    {
        var bytes = ReadBytes(4);
        return System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(bytes);
    }

    public ulong ReadF64Bits()
    {
        var bytes = ReadBytes(8);
        return System.Buffers.Binary.BinaryPrimitives.ReadUInt64LittleEndian(bytes);
    }

    public string ReadName()
    {
        var length = ReadU32();
        var bytes = ReadBytes(checked((int)Math.Min(length, int.MaxValue)));
        try
        {
            return StrictUtf8.GetString(bytes);
        }
        catch (DecoderFallbackException)
        {
            throw new WasmDecodeException("malformed UTF-8 encoding");
        }
    }

    /// <summary>Reads the count of a vector: it cannot be larger than the remaining bytes (each element has at least one byte).</summary>
    public int ReadCount()
    {
        var count = ReadU32();
        if (count > (uint)(End - Position))
            throw new WasmDecodeException("unexpected end of section or function");

        return (int)count;
    }
}
