using System.Buffers.Binary;
using System.IO.Hashing;

namespace Meziantou.Framework.WebPlatform.Rendering;

/// <summary>The disposal of the region of an APNG frame after it is displayed (https://w3c.github.io/png/#fcTL-chunk).</summary>
internal enum ApngDisposeOp : byte
{
    /// <summary>The region is left as is.</summary>
    None = 0,

    /// <summary>The region is cleared to transparent black.</summary>
    Background = 1,

    /// <summary>The region is reverted to its content before the frame was drawn.</summary>
    Previous = 2,
}

/// <summary>How an APNG frame is drawn in its region (https://w3c.github.io/png/#fcTL-chunk).</summary>
internal enum ApngBlendOp : byte
{
    /// <summary>The frame replaces the pixels of the region.</summary>
    Source = 0,

    /// <summary>The frame is composited over the region.</summary>
    Over = 1,
}

/// <summary>
/// A frame of an animated PNG image: its region, its timing, its disposal and blending operations, and its pixels as a
/// standalone PNG image (the chunks of the image with the size of the frame and the data of the frame).
/// </summary>
internal sealed record ApngFrame(int X, int Y, int Width, int Height, double Duration, ApngDisposeOp DisposeOp, ApngBlendOp BlendOp, byte[] Png);

/// <summary>
/// An animated PNG image (https://w3c.github.io/png/#apng-frame-based-animation), split into its frames: a decoder of still PNG
/// images decodes each frame (a PNG image made of the chunks of the image before its image data, the header with the size of
/// the frame, and the data of the frame), and composites them.
/// </summary>
[System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "MA0182:Avoid unused internal types", Justification = "Used by the rendering backends (InternalsVisibleTo)")]
internal sealed class ApngImage
{
    private ApngImage(List<ApngFrame> frames, int repetitionCount)
    {
        Frames = frames;
        RepetitionCount = repetitionCount;
    }

    /// <summary>The frames of the animation, in order.</summary>
    public IReadOnlyList<ApngFrame> Frames { get; }

    /// <summary>The number of repetitions after the first play (-1: forever, from <c>num_plays</c> 0).</summary>
    public int RepetitionCount { get; }

    /// <summary>
    /// Parses an animated PNG image. Returns null for a PNG image that is not animated, or whose animation chunks are invalid
    /// (missing, out of order, inconsistent): as in the browsers, the image is then displayed as a still image (its default image).
    /// </summary>
    public static ApngImage? TryParse(ReadOnlySpan<byte> bytes)
    {
        if (!bytes.StartsWith(ImageMetadata.PngSignature))
            return null;

        ReadOnlySpan<byte> header = default;
        var sharedChunks = new List<(int Offset, int Length)>(); // the whole chunks copied into each frame
        uint frameCount = 0;
        var plays = 0u;
        var hasAnimationControl = false;
        var seenImageData = false;
        var expectedSequence = 0u;
        var frames = new List<ApngFrame>();
        PendingFrame? pending = null;
        var defaultImageIsFrame = false;
        var offset = ImageMetadata.PngSignature.Length;
        while (offset + 12 <= bytes.Length)
        {
            var length = BinaryPrimitives.ReadUInt32BigEndian(bytes[offset..]);
            if (length > int.MaxValue - 12 || offset + 12 + (long)length > bytes.Length)
                break;

            var type = bytes.Slice(offset + 4, 4);
            var data = bytes.Slice(offset + 8, (int)length);
            var chunkLength = 12 + (int)length;
            if (type.SequenceEqual("IHDR"u8))
            {
                if (length != 13)
                    return null;

                header = data;
            }
            else if (type.SequenceEqual("acTL"u8))
            {
                // The animation control chunk must precede the image data
                if (seenImageData || length != 8 || hasAnimationControl)
                    return null;

                hasAnimationControl = true;
                frameCount = BinaryPrimitives.ReadUInt32BigEndian(data);
                plays = BinaryPrimitives.ReadUInt32BigEndian(data[4..]);
                if (frameCount == 0)
                    return null;
            }
            else if (type.SequenceEqual("fcTL"u8))
            {
                // As in the browsers, a frame control chunk before the animation control chunk is accepted
                if (length != 26 || header.IsEmpty || BinaryPrimitives.ReadUInt32BigEndian(data) != expectedSequence++)
                    return null;

                if (pending is { } previous && !TryAddFrame(frames, bytes, header, sharedChunks, previous))
                    return null;

                pending = new PendingFrame(data);
                if (!seenImageData)
                {
                    // The default image is the first frame: its region is the whole image
                    defaultImageIsFrame = true;
                    if (pending.X != 0 || pending.Y != 0 || pending.Width != BinaryPrimitives.ReadUInt32BigEndian(header) || pending.Height != BinaryPrimitives.ReadUInt32BigEndian(header[4..]))
                        return null;
                }
            }
            else if (type.SequenceEqual("IDAT"u8))
            {
                seenImageData = true;
                if (defaultImageIsFrame && pending is not null)
                {
                    pending.Data.Add(data.ToArray());
                }
            }
            else if (type.SequenceEqual("fdAT"u8))
            {
                if (pending is null || length < 4 || BinaryPrimitives.ReadUInt32BigEndian(data) != expectedSequence++ || !seenImageData)
                    return null;

                pending.Data.Add(data[4..].ToArray());
            }
            else if (type.SequenceEqual("IEND"u8))
            {
                break;
            }
            else if (!seenImageData)
            {
                sharedChunks.Add((offset, chunkLength));
            }

            offset += chunkLength;
        }

        if (!hasAnimationControl || !seenImageData || header.IsEmpty)
            return null;

        if (pending is { } last && !TryAddFrame(frames, bytes, header, sharedChunks, last))
            return null;

        // The number of frames must be the one of the animation control chunk
        if (frames.Count != frameCount)
            return null;

        return new ApngImage(frames, plays == 0 ? -1 : (int)Math.Min(plays - 1, int.MaxValue));
    }

    /// <summary>Adds a frame whose region is in the image, or returns false when the frame is invalid.</summary>
    private static bool TryAddFrame(List<ApngFrame> frames, ReadOnlySpan<byte> bytes, ReadOnlySpan<byte> header, List<(int Offset, int Length)> sharedChunks, PendingFrame frame)
    {
        var canvasWidth = BinaryPrimitives.ReadUInt32BigEndian(header);
        var canvasHeight = BinaryPrimitives.ReadUInt32BigEndian(header[4..]);
        if (frame.Width == 0 || frame.Height == 0 || frame.Data.Count == 0 || (ulong)frame.X + frame.Width > canvasWidth || (ulong)frame.Y + frame.Height > canvasHeight)
            return false;

        frames.Add(new ApngFrame((int)frame.X, (int)frame.Y, (int)frame.Width, (int)frame.Height, frame.Duration, frame.DisposeOp, frame.BlendOp, CreateFramePng(bytes, header, sharedChunks, frame)));
        return true;
    }

    /// <summary>Creates the PNG image of a frame: the signature, the header with the size of the frame, the shared chunks, the data, the end.</summary>
    private static byte[] CreateFramePng(ReadOnlySpan<byte> bytes, ReadOnlySpan<byte> header, List<(int Offset, int Length)> sharedChunks, PendingFrame frame)
    {
        using var output = new MemoryStream();
        output.Write(ImageMetadata.PngSignature);
        Span<byte> frameHeader = stackalloc byte[13];
        header.CopyTo(frameHeader);
        BinaryPrimitives.WriteUInt32BigEndian(frameHeader, frame.Width);
        BinaryPrimitives.WriteUInt32BigEndian(frameHeader[4..], frame.Height);
        WriteChunk(output, "IHDR"u8, frameHeader);
        foreach (var (chunkOffset, chunkLength) in sharedChunks)
        {
            output.Write(bytes.Slice(chunkOffset, chunkLength));
        }

        foreach (var data in frame.Data)
        {
            WriteChunk(output, "IDAT"u8, data);
        }

        WriteChunk(output, "IEND"u8, []);
        return output.ToArray();
    }

    private static void WriteChunk(Stream output, ReadOnlySpan<byte> type, ReadOnlySpan<byte> data)
    {
        Span<byte> buffer = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(buffer, (uint)data.Length);
        output.Write(buffer);
        output.Write(type);
        output.Write(data);
        var crc = new Crc32();
        crc.Append(type);
        crc.Append(data);
        BinaryPrimitives.WriteUInt32BigEndian(buffer, crc.GetCurrentHashAsUInt32());
        output.Write(buffer);
    }

    /// <summary>A frame control chunk (fcTL) and the data of the frame.</summary>
    private sealed class PendingFrame
    {
        public PendingFrame(ReadOnlySpan<byte> control)
        {
            Width = BinaryPrimitives.ReadUInt32BigEndian(control[4..]);
            Height = BinaryPrimitives.ReadUInt32BigEndian(control[8..]);
            X = BinaryPrimitives.ReadUInt32BigEndian(control[12..]);
            Y = BinaryPrimitives.ReadUInt32BigEndian(control[16..]);

            // The delay is a fraction of seconds (a denominator of 0 is 100)
            var numerator = BinaryPrimitives.ReadUInt16BigEndian(control[20..]);
            var denominator = BinaryPrimitives.ReadUInt16BigEndian(control[22..]);
            Duration = numerator * 1000.0 / (denominator == 0 ? 100 : denominator);
            DisposeOp = control[24] <= 2 ? (ApngDisposeOp)control[24] : ApngDisposeOp.None;
            BlendOp = control[25] <= 1 ? (ApngBlendOp)control[25] : ApngBlendOp.Source;
        }

        public uint Width { get; }

        public uint Height { get; }

        public uint X { get; }

        public uint Y { get; }

        public double Duration { get; }

        public ApngDisposeOp DisposeOp { get; }

        public ApngBlendOp BlendOp { get; }

        public List<byte[]> Data { get; } = [];
    }
}
