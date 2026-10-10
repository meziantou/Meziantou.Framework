using System.Buffers;
using System.Buffers.Binary;
using System.IO.Compression;
using Meziantou.Framework.Imaging.Internals;

namespace Meziantou.Framework.Imaging.Tests.Codecs;

/// <summary>
/// Builders of small synthetic PNG/APNG, GIF and JPEG structures for structure-parser unit tests (no fixture files). The
/// pixel payloads are plausible but are never decoded by these tests; chunk CRCs use the library CRC, which the golden
/// corpus checks independently (zlib-produced fixture CRCs) and <see cref="InputLayerTests"/> against published vectors.
/// </summary>
internal static class SyntheticImages
{
    public static byte[] Png(int width, int height, byte colorType = 6, byte bitDepth = 8) => new PngBuilder().Header(width, height, bitDepth, colorType).ImageData(width, height, colorType, bitDepth).End().ToArray();

    public static byte[] Apng(int width, int height, int frames, uint plays = 0)
    {
        var builder = new PngBuilder().Header(width, height).AnimationControl((uint)frames, plays).FrameControl(width, height).ImageData(width, height);
        for (var i = 1; i < frames; i++)
        {
            builder.FrameControl(width, height).FrameData(width, height);
        }

        return builder.End().ToArray();
    }

    public static byte[] Gif(int width, int height, int images, ushort? loop = null, bool transparent = false)
    {
        var builder = new GifBuilder().Header(width, height);
        if (loop is not null)
        {
            builder.Loop(loop.Value);
        }

        for (var i = 0; i < images; i++)
        {
            builder.GraphicControl(disposal: 1, delay: 10, transparent: transparent).Image(0, 0, width, height);
        }

        return builder.Trailer().ToArray();
    }

    public static byte[] Jpeg(byte frameMarker, int components, int precision = 8, int width = 8, int height = 8)
    {
        var builder = new JpegBuilder().StartOfImage().Jfif().QuantizationTable().Frame(frameMarker, precision, width, height, components).HuffmanTable();

        // A progressive frame starts with a DC scan (Ss = Se = 0), a sequential scan covers the 64 coefficients
        return builder.Scan(components, end: frameMarker == 0xC2 ? (byte)0 : (byte)63).EntropyData([0x12, 0xFF, 0x00, 0x34, 0xFF, 0xD0, 0x56]).EndOfImage().ToArray();
    }

    public static byte[] Zlib(ReadOnlySpan<byte> data)
    {
        using var output = new MemoryStream();
        using (var zlib = new ZLibStream(output, CompressionLevel.Optimal, leaveOpen: true))
        {
            zlib.Write(data);
        }

        return output.ToArray();
    }

    internal sealed class PngBuilder
    {
        private readonly ArrayBufferWriter<byte> _output = new();

        private void Append(ReadOnlySpan<byte> data) => _output.Write(data);
        private uint _sequence;

        public PngBuilder(bool signature = true)
        {
            if (signature)
            {
                Append(PngCodec.Signature);
            }
        }

        public long Length => _output.WrittenCount;

        public PngBuilder Chunk(string type, ReadOnlySpan<byte> data, bool corruptCrc = false)
        {
            Span<byte> header = stackalloc byte[8];
            BinaryPrimitives.WriteUInt32BigEndian(header, (uint)data.Length);
            Encoding.ASCII.GetBytes(type, header[4..]);
            Append(header);
            Append(data);
            var crc = Crc32.Finish(Crc32.Update(Crc32.Update(Crc32.Initial, header[4..]), data));
            Span<byte> crcBytes = stackalloc byte[4];
            BinaryPrimitives.WriteUInt32BigEndian(crcBytes, corruptCrc ? ~crc : crc);
            Append(crcBytes);
            return this;
        }

        public PngBuilder Header(int width, int height, byte bitDepth = 8, byte colorType = 6, byte interlace = 0)
        {
            Span<byte> data = stackalloc byte[13];
            BinaryPrimitives.WriteInt32BigEndian(data, width);
            BinaryPrimitives.WriteInt32BigEndian(data[4..], height);
            data[8] = bitDepth;
            data[9] = colorType;
            data[12] = interlace;
            return Chunk("IHDR", data);
        }

        public PngBuilder ImageData(int width, int height, byte colorType = 6, byte bitDepth = 8)
            => Chunk("IDAT", Zlib(new byte[(1 + GetRowBytes(width, colorType, bitDepth)) * height]));

        public PngBuilder AnimationControl(uint frames, uint plays)
        {
            Span<byte> data = stackalloc byte[8];
            BinaryPrimitives.WriteUInt32BigEndian(data, frames);
            BinaryPrimitives.WriteUInt32BigEndian(data[4..], plays);
            return Chunk("acTL", data);
        }

        public PngBuilder FrameControl(int width, int height, int x = 0, int y = 0, byte dispose = 0, byte blend = 0, uint? sequence = null)
        {
            Span<byte> data = stackalloc byte[26];
            BinaryPrimitives.WriteUInt32BigEndian(data, sequence ?? _sequence++);
            BinaryPrimitives.WriteInt32BigEndian(data[4..], width);
            BinaryPrimitives.WriteInt32BigEndian(data[8..], height);
            BinaryPrimitives.WriteInt32BigEndian(data[12..], x);
            BinaryPrimitives.WriteInt32BigEndian(data[16..], y);
            BinaryPrimitives.WriteUInt16BigEndian(data[20..], 1);
            BinaryPrimitives.WriteUInt16BigEndian(data[22..], 10);
            data[24] = dispose;
            data[25] = blend;
            return Chunk("fcTL", data);
        }

        public PngBuilder FrameData(int width, int height, uint? sequence = null)
        {
            var payload = Zlib(new byte[(1 + (width * 4)) * height]);
            var data = new byte[4 + payload.Length];
            BinaryPrimitives.WriteUInt32BigEndian(data, sequence ?? _sequence++);
            payload.CopyTo(data, 4);
            return Chunk("fdAT", data);
        }

        public PngBuilder Text(string keyword, string value) => Chunk("tEXt", [.. Encoding.Latin1.GetBytes(keyword), 0, .. Encoding.Latin1.GetBytes(value)]);

        public PngBuilder CompressedText(string keyword, ReadOnlySpan<byte> value) => Chunk("zTXt", [.. Encoding.Latin1.GetBytes(keyword), 0, 0, .. Zlib(value)]);

        public PngBuilder End() => Chunk("IEND", []);

        public PngBuilder Raw(ReadOnlySpan<byte> bytes)
        {
            Append(bytes);
            return this;
        }

        public byte[] ToArray() => _output.WrittenSpan.ToArray();

        private static int GetRowBytes(int width, byte colorType, byte bitDepth)
        {
            var channels = colorType switch { 0 or 3 => 1, 2 => 3, 4 => 2, _ => 4 };
            return ((width * channels * bitDepth) + 7) / 8;
        }
    }

    internal sealed class GifBuilder
    {
        private readonly ArrayBufferWriter<byte> _output = new();

        private void Append(ReadOnlySpan<byte> data) => _output.Write(data);

        public GifBuilder Header(int width, int height, string version = "89a", bool globalColorTable = true)
        {
            Append(Encoding.ASCII.GetBytes("GIF" + version));
            Span<byte> descriptor = stackalloc byte[7];
            BinaryPrimitives.WriteUInt16LittleEndian(descriptor, (ushort)width);
            BinaryPrimitives.WriteUInt16LittleEndian(descriptor[2..], (ushort)height);
            descriptor[4] = globalColorTable ? (byte)0x80 : (byte)0; // 2 colors
            Append(descriptor);
            if (globalColorTable)
            {
                Append([0, 0, 0, 255, 255, 255]);
            }

            return this;
        }

        public GifBuilder Loop(ushort count)
        {
            Append([0x21, 0xFF, 11, .. "NETSCAPE2.0"u8, 3, 1, (byte)count, (byte)(count >> 8), 0]);
            return this;
        }

        public GifBuilder GraphicControl(int disposal, ushort delay, bool transparent = false)
        {
            Append([0x21, 0xF9, 4, (byte)((disposal << 2) | (transparent ? 1 : 0)), (byte)delay, (byte)(delay >> 8), 0, 0]);
            return this;
        }

        public GifBuilder Comment(string text)
        {
            var bytes = Encoding.Latin1.GetBytes(text);
            Append([0x21, 0xFE]);
            foreach (var chunk in bytes.Chunk(255))
            {
                Append([(byte)chunk.Length]);
                Append(chunk);
            }

            Append([0]);
            return this;
        }

        public GifBuilder Extension(byte label, ReadOnlySpan<byte> block)
        {
            Append([0x21, label, (byte)block.Length, .. block, 0]);
            return this;
        }

        public GifBuilder Image(int left, int top, int width, int height, byte codeSize = 2)
        {
            Span<byte> descriptor = stackalloc byte[10];
            descriptor[0] = 0x2C;
            BinaryPrimitives.WriteUInt16LittleEndian(descriptor[1..], (ushort)left);
            BinaryPrimitives.WriteUInt16LittleEndian(descriptor[3..], (ushort)top);
            BinaryPrimitives.WriteUInt16LittleEndian(descriptor[5..], (ushort)width);
            BinaryPrimitives.WriteUInt16LittleEndian(descriptor[7..], (ushort)height);
            Append(descriptor);
            Append([codeSize]);

            // LZW: a clear code before every index keeps the code size at 3 bits; then the end code
            var codes = new List<int>();
            for (var i = 0; i < width * height; i++)
            {
                codes.Add(4);
                codes.Add(i & 1);
            }

            codes.Add(5);
            var packed = new List<byte>();
            int buffer = 0, bits = 0;
            foreach (var code in codes)
            {
                buffer |= code << bits;
                bits += 3;
                while (bits >= 8)
                {
                    packed.Add((byte)buffer);
                    buffer >>= 8;
                    bits -= 8;
                }
            }

            if (bits > 0)
            {
                packed.Add((byte)buffer);
            }

            foreach (var chunk in packed.Chunk(255))
            {
                Append([(byte)chunk.Length]);
                Append(chunk);
            }

            Append([0]);
            return this;
        }

        public GifBuilder Raw(ReadOnlySpan<byte> bytes)
        {
            Append(bytes);
            return this;
        }

        public GifBuilder Trailer()
        {
            Append([0x3B]);
            return this;
        }

        public byte[] ToArray() => _output.WrittenSpan.ToArray();
    }

    internal sealed class JpegBuilder
    {
        private readonly ArrayBufferWriter<byte> _output = new();

        private void Append(ReadOnlySpan<byte> data) => _output.Write(data);

        public JpegBuilder StartOfImage()
        {
            Append([0xFF, 0xD8]);
            return this;
        }

        public JpegBuilder Segment(byte marker, ReadOnlySpan<byte> payload)
        {
            Append([0xFF, marker, (byte)((payload.Length + 2) >> 8), (byte)(payload.Length + 2)]);
            Append(payload);
            return this;
        }

        public JpegBuilder Jfif(byte units = 1, ushort x = 72, ushort y = 72) => Segment(0xE0, [.. "JFIF\0"u8, 1, 2, units, (byte)(x >> 8), (byte)x, (byte)(y >> 8), (byte)y, 0, 0]);

        public JpegBuilder QuantizationTable() => Segment(0xDB, [0, .. Enumerable.Repeat((byte)1, 64)]);

        public JpegBuilder HuffmanTable() => Segment(0xC4, [0x00, 0, 1, .. new byte[14], 0]);

        public JpegBuilder Frame(byte marker, int precision, int width, int height, int components, byte sampling = 0x11)
        {
            var payload = new List<byte> { (byte)precision, (byte)(height >> 8), (byte)height, (byte)(width >> 8), (byte)width, (byte)components };
            for (var i = 0; i < components; i++)
            {
                payload.AddRange([(byte)(i + 1), i == 0 ? sampling : (byte)0x11, 0]);
            }

            return Segment(marker, [.. payload]);
        }

        /// <param name="components">The number of components (consecutive identifiers).</param>
        /// <param name="firstComponent">The identifier of the first component.</param>
        /// <param name="start">Ss: 0 for sequential and progressive DC scans.</param>
        /// <param name="end">Se: 63 for sequential scans, 0 for progressive DC scans.</param>
        public JpegBuilder Scan(int components, byte firstComponent = 1, byte start = 0, byte end = 63)
        {
            var payload = new List<byte> { (byte)components };
            for (var i = 0; i < components; i++)
            {
                payload.AddRange([(byte)(firstComponent + i), 0]);
            }

            payload.AddRange([start, end, 0]);
            return Segment(0xDA, [.. payload]);
        }

        public JpegBuilder EntropyData(ReadOnlySpan<byte> data)
        {
            Append(data);
            return this;
        }

        public JpegBuilder Raw(ReadOnlySpan<byte> bytes) => EntropyData(bytes);

        public JpegBuilder EndOfImage()
        {
            Append([0xFF, 0xD9]);
            return this;
        }

        public byte[] ToArray() => _output.WrittenSpan.ToArray();
    }
}
