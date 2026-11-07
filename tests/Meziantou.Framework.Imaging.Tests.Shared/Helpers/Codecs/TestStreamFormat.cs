using System.Buffers;
using System.Buffers.Binary;
using System.Text;
using Meziantou.Framework.Imaging.Internals;

namespace Meziantou.Framework.Imaging.Tests.Codecs;

/// <summary>
/// A test-only streamed container decoded by <see cref="TestStreamCodec"/> and produced by <see cref="TestStreamEncoder"/>
/// (lifecycle tests). Unlike <see cref="TestRawCodec"/>, it supports unknown frame counts (an end record carries the
/// count), separate posters and delta frames that require a compositor, so it exercises sequential readers and writers
/// exactly as APNG/GIF will.
/// </summary>
/// <remarks>
/// Layout (little-endian): 8-byte <see cref="Signature"/>; an 18-byte header (width i32, height i32, pixel format u8, image
/// format u8, flags u8 — 1 animated, 2 poster, 4 unsupported feature —, reserved u8, declared frame count u16 — 0 unknown —,
/// total plays u16 — 0 infinite —, comment length u16) and the Latin-1 <c>Comment</c> text; then records: <c>P</c> poster
/// rows, <c>F</c> duration (numerator u32, denominator u32) and rows, <c>D</c> duration and rows XORed with the previous
/// displayed frame, <c>E</c> frame count u32 (end of input).
/// </remarks>
internal static class TestStreamFormat
{
    public const int HeaderLength = 18;

    public const byte FlagAnimated = 1;

    public const byte FlagPoster = 2;

    public const byte FlagUnsupported = 4;

    public static ReadOnlySpan<byte> Signature => [0x8B, (byte)'M', (byte)'Z', (byte)'S', (byte)'T', (byte)'R', (byte)'M', 0x0A];

    /// <summary>Writes the signature, header and comment.</summary>
    public static void WriteHeader(IBufferWriterLike output, Size size, PixelFormat pixelFormat, ImageFormat format, byte flags, int declaredFrames, int? totalPlays, string? comment)
    {
        output.Write(Signature);
        Span<byte> header = stackalloc byte[HeaderLength];
        BinaryPrimitives.WriteInt32LittleEndian(header, size.Width);
        BinaryPrimitives.WriteInt32LittleEndian(header[4..], size.Height);
        header[8] = (byte)pixelFormat;
        header[9] = (byte)format;
        header[10] = flags;
        header[11] = 0;
        BinaryPrimitives.WriteUInt16LittleEndian(header[12..], (ushort)declaredFrames);
        BinaryPrimitives.WriteUInt16LittleEndian(header[14..], (ushort)(totalPlays ?? 0));
        var text = comment is null ? [] : Encoding.Latin1.GetBytes(comment);
        BinaryPrimitives.WriteUInt16LittleEndian(header[16..], (ushort)text.Length);
        output.Write(header);
        output.Write(text);
    }

    public static void WriteFrameRecord(IBufferWriterLike output, byte tag, FrameDuration duration)
    {
        Span<byte> record = stackalloc byte[9];
        record[0] = tag;
        BinaryPrimitives.WriteUInt32LittleEndian(record[1..], checked((uint)duration.Numerator));
        BinaryPrimitives.WriteUInt32LittleEndian(record[5..], checked((uint)duration.Denominator));
        output.Write(tag == (byte)'P' ? record[..1] : record);
    }

    public static void WriteEnd(IBufferWriterLike output, int frameCount)
    {
        Span<byte> record = stackalloc byte[5];
        record[0] = (byte)'E';
        BinaryPrimitives.WriteUInt32LittleEndian(record[1..], (uint)frameCount);
        output.Write(record);
    }

    /// <summary>Encodes a complete input directly (independently of the writer), for reader tests.</summary>
    /// <param name="width">The width.</param>
    /// <param name="height">The height.</param>
    /// <param name="pixelFormat">The pixel layout of the rows.</param>
    /// <param name="frames">The displayed frames: duration and tightly packed pixels.</param>
    /// <param name="poster">The separate poster pixels, or <see langword="null"/>.</param>
    /// <param name="declaredFrames">The declared frame count (0 = unknown), or <see langword="null"/> for the actual count.</param>
    /// <param name="totalPlays">The total plays (0 = infinite).</param>
    /// <param name="comment">The comment text, or <see langword="null"/>.</param>
    /// <param name="delta">Whether frames after the first are encoded as deltas (XOR with the previous displayed frame).</param>
    /// <param name="animated">Whether the header declares an animation even with a single frame.</param>
    /// <param name="format">The format reported by the decoder.</param>
    /// <param name="endCount">The count written in the end record, or <see langword="null"/> for the actual count; -1 omits the end record.</param>
    /// <returns>The encoded bytes.</returns>
    public static byte[] Encode(int width, int height, PixelFormat pixelFormat, IReadOnlyList<(FrameDuration Duration, byte[] Pixels)> frames, byte[]? poster = null, int? declaredFrames = null, int totalPlays = 0, string? comment = "test stream", bool delta = false, bool animated = false, ImageFormat format = ImageFormat.Gif, int? endCount = null)
    {
        var output = new ArrayBufferWriterLike();
        var flags = (byte)((animated || frames.Count > 1 || poster is not null ? FlagAnimated : 0) | (poster is null ? 0 : FlagPoster));
        WriteHeader(output, new Size(width, height), pixelFormat, format, flags, declaredFrames ?? frames.Count, totalPlays == 0 ? null : totalPlays, comment);
        if (poster is not null)
        {
            WriteFrameRecord(output, (byte)'P', FrameDuration.Zero);
            output.Write(poster);
        }

        byte[]? previous = null;
        foreach (var (duration, pixels) in frames)
        {
            if (pixels.Length != width * height * PixelFormats.GetBytesPerPixel(pixelFormat))
                throw new ArgumentException("The frame has the wrong size.", nameof(frames));

            if (delta && previous is not null)
            {
                WriteFrameRecord(output, (byte)'D', duration);
                var xored = new byte[pixels.Length];
                for (var i = 0; i < xored.Length; i++)
                {
                    xored[i] = (byte)(pixels[i] ^ previous[i]);
                }

                output.Write(xored);
            }
            else
            {
                WriteFrameRecord(output, (byte)'F', duration);
                output.Write(pixels);
            }

            previous = pixels;
        }

        if (endCount != -1)
        {
            WriteEnd(output, endCount ?? frames.Count);
        }

        return output.ToArray();
    }

    /// <summary>Creates frame pixels following a deterministic pattern (alpha opaque).</summary>
    public static byte[] Pattern(int width, int height, PixelFormat format, int seed) => TestRawImage.Pattern(width, height, format, seed);

    /// <summary>Creates frames with distinct patterns and durations.</summary>
    public static (FrameDuration Duration, byte[] Pixels)[] Frames(int width, int height, PixelFormat format, int count)
        => [.. Enumerable.Range(0, count).Select(i => (new FrameDuration(i + 1, 30), Pattern(width, height, format, i + 1)))];

    /// <summary>The minimal output abstraction shared by the direct encoder and the session (the library buffer and a test array).</summary>
    internal interface IBufferWriterLike
    {
        void Write(ReadOnlySpan<byte> data);
    }

    internal sealed class ArrayBufferWriterLike : IBufferWriterLike
    {
        private readonly ArrayBufferWriter<byte> _buffer = new();

        public void Write(ReadOnlySpan<byte> data) => _buffer.Write(data);

        public byte[] ToArray() => _buffer.WrittenSpan.ToArray();
    }

    internal sealed class OutputBufferWriter(ImageOutputBuffer output) : IBufferWriterLike
    {
        public void Write(ReadOnlySpan<byte> data) => output.Write(data);
    }
}
