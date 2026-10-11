using System.Buffers.Binary;
using System.Runtime.InteropServices;
using System.Text;

namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// Reads and validates the structure of an animated cursor without decoding a pixel: the RIFF chunk list, the
/// <c>anih</c> header, the <c>rate</c> and <c>seq </c> tables, the frame list, the title and the author, then the icon
/// directory of every frame a step shows. Identification and decoding share this one walk.
/// </summary>
/// <remarks>
/// <para>
/// Chunks may come in any order (the tables are sometimes written after the frame list), so the walk first locates them,
/// then reads the header, then the tables whose sizes depend on it. Every range is checked against its container before it
/// is read, declared counts are checked against <see cref="ImageResourceLimits.MaxFrames"/> before a table is rented, and
/// each chunk header and each table is read once, so a small file cannot cause more reads than it has bytes.
/// </para>
/// <para>
/// The <c>seq </c> chunk is authoritative: it is used whenever it is present, whatever the sequence flag of the header
/// says. Without it, step <c>i</c> shows frame <c>i</c>. The geometry fields of the header are not used: the embedded icon
/// payloads are authoritative, as in an icon file. Frames that no step shows are bounds-checked as chunks and otherwise
/// left alone.
/// </para>
/// <para>
/// A frame is an icon or cursor file, which may hold several representations. The first step fixes the canvas with the
/// default rule of icon files (the largest representation, then the deepest); every other frame is displayed through its
/// deepest representation of exactly that size, because every frame of an animation has the canvas size.
/// </para>
/// </remarks>
internal static class AniStructureReader
{
    /// <summary>Reads the structure.</summary>
    /// <param name="source">The input.</param>
    /// <param name="context">The operation context.</param>
    /// <returns>The structure. The caller disposes it.</returns>
    /// <exception cref="InvalidImageContentException">The container, the header, a table or a displayed frame is malformed or truncated.</exception>
    /// <exception cref="UnsupportedImageFeatureException">The frames are raw bitmaps, a frame has no representation of the canvas size, or a frame payload is not supported.</exception>
    /// <exception cref="ImageResourceLimitException">A declared count or a size exceeds a configured limit.</exception>
    public static AniStructure Read(RandomAccessSource source, ImageCodecContext context)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(context);
        var chunks = ReadChunkList(source);
        if (chunks.Header is not { } headerRange)
            throw AniFormat.Invalid("The animated cursor has no 'anih' header chunk.");

        var (frameCount, stepCount, displayRate) = ReadHeader(source, context, headerRange);
        PooledBuffer? rates = null;
        PooledBuffer? sequence = null;
        try
        {
            if (chunks.Rate is { } rateRange)
            {
                rates = ReadTable(source, rateRange, stepCount, "rate");
            }

            if (chunks.Sequence is { } sequenceRange)
            {
                sequence = ReadTable(source, sequenceRange, stepCount, "seq ");
                ValidateSequence(sequence.Span, frameCount);
            }
            else if (stepCount > frameCount)
            {
                throw AniFormat.Invalid(string.Create(CultureInfo.InvariantCulture, $"The animated cursor declares {stepCount} steps for {frameCount} frames without a 'seq ' chunk: a step has no frame to show."));
            }

            if (chunks.Frames is not { } frameListRange)
                throw AniFormat.Invalid("The animated cursor has no 'fram' list.");

            var frames = ReadFrameList(source, frameListRange, frameCount);
            var metadata = new DecodedMetadataBuilder(context.Tracker, ImageFormat.Ani);
            if (chunks.Info is { } infoRange)
            {
                ReadInfo(source, infoRange, metadata);
            }

            var structure = new AniStructure(frames, stepCount, displayRate, rates, sequence, metadata.Metadata);
            ResolveFrames(source, context, structure);
            return structure;
        }
        catch
        {
            rates?.Dispose();
            sequence?.Dispose();
            throw;
        }
    }

    /// <summary>Walks the top-level chunks and records where the ones the format defines are.</summary>
    private static ChunkList ReadChunkList(RandomAccessSource source)
    {
        Span<byte> buffer = stackalloc byte[AniFormat.RiffHeaderLength];
        source.Read(0, buffer);
        if (!AniFormat.MatchesSignature(buffer))
            throw AniFormat.Invalid("The data does not start with the 'RIFF' and 'ACON' codes of an animated cursor.");

        var riffSize = BinaryPrimitives.ReadUInt32LittleEndian(buffer[4..]);
        if (riffSize < 4)
            throw AniFormat.Invalid(string.Create(CultureInfo.InvariantCulture, $"The RIFF header declares {riffSize} bytes, fewer than its 4-byte form type."));

        var end = 8 + (long)riffSize;
        if (end > source.Length)
            throw AniFormat.Invalid(string.Create(CultureInfo.InvariantCulture, $"The RIFF header declares a {end}-byte file but the input has {source.Length} bytes: the animated cursor is truncated."));

        var result = default(ChunkList);
        var position = (long)AniFormat.RiffHeaderLength;
        Span<byte> listType = stackalloc byte[4];
        while (position < end)
        {
            var chunk = ReadChunkHeader(source, ref position, end, buffer, "the RIFF container");
            var fourCC = buffer[..4];
            if (fourCC.SequenceEqual(AniFormat.Header))
            {
                SetOnce(ref result.Header, chunk, "'anih' chunk");
            }
            else if (fourCC.SequenceEqual(AniFormat.Rate))
            {
                SetOnce(ref result.Rate, chunk, "'rate' chunk");
            }
            else if (fourCC.SequenceEqual(AniFormat.Sequence))
            {
                SetOnce(ref result.Sequence, chunk, "'seq ' chunk");
            }
            else if (fourCC.SequenceEqual(AniFormat.List))
            {
                if (chunk.Length < listType.Length)
                    throw AniFormat.Invalid(string.Create(CultureInfo.InvariantCulture, $"The 'LIST' chunk at offset {chunk.Offset - AniFormat.ChunkHeaderLength} declares {chunk.Length} bytes, fewer than its 4-byte list type."));

                source.Read(chunk.Offset, listType);
                var content = new ChunkRange(chunk.Offset + listType.Length, chunk.Length - listType.Length);
                if (listType.SequenceEqual(AniFormat.Frames))
                {
                    SetOnce(ref result.Frames, content, "'fram' list");
                }
                else if (listType.SequenceEqual(AniFormat.Info))
                {
                    // Like the other metadata of the library, the first occurrence wins
                    result.Info ??= content;
                }
            }
        }

        return result;
    }

    /// <summary>Reads one chunk header and moves <paramref name="position"/> past the chunk and its pad byte.</summary>
    /// <param name="source">The input.</param>
    /// <param name="position">The offset of the chunk header; on return, the offset of the next chunk.</param>
    /// <param name="end">The end of the container.</param>
    /// <param name="header">An 8-byte buffer at least; it receives the four-character code and the size.</param>
    /// <param name="container">The container, for messages.</param>
    /// <returns>The range of the chunk data.</returns>
    private static ChunkRange ReadChunkHeader(RandomAccessSource source, ref long position, long end, Span<byte> header, string container)
    {
        if (end - position < AniFormat.ChunkHeaderLength)
            throw AniFormat.Invalid(string.Create(CultureInfo.InvariantCulture, $"{end - position} byte(s) follow the last chunk of {container} at offset {position}; a chunk header is {AniFormat.ChunkHeaderLength} bytes."));

        source.Read(position, header[..AniFormat.ChunkHeaderLength]);
        var size = BinaryPrimitives.ReadUInt32LittleEndian(header[4..]);
        var dataOffset = position + AniFormat.ChunkHeaderLength;
        if (size > end - dataOffset)
            throw AniFormat.Invalid(string.Create(CultureInfo.InvariantCulture, $"The '{FormatFourCC(header[..4])}' chunk at offset {position} declares {size} bytes, past the end of {container}."));

        position = dataOffset + size;

        // Chunks are word-aligned; a missing pad byte is only tolerated at the very end of the container
        if ((size & 1) != 0 && position < end)
        {
            position++;
        }

        return new ChunkRange(dataOffset, size);
    }

    private static void SetOnce(ref ChunkRange? slot, ChunkRange value, string name)
    {
        if (slot is not null)
            throw AniFormat.Invalid($"The animated cursor has more than one {name}.");

        slot = value;
    }

    private static (int FrameCount, int StepCount, uint DisplayRate) ReadHeader(RandomAccessSource source, ImageCodecContext context, ChunkRange range)
    {
        if (range.Length != AniFormat.HeaderLength)
            throw AniFormat.Invalid(string.Create(CultureInfo.InvariantCulture, $"The 'anih' chunk has {range.Length} bytes; {AniFormat.HeaderLength} are expected."));

        Span<byte> header = stackalloc byte[AniFormat.HeaderLength];
        source.Read(range.Offset, header);
        var declaredLength = BinaryPrimitives.ReadUInt32LittleEndian(header);
        if (declaredLength != AniFormat.HeaderLength)
            throw AniFormat.Invalid(string.Create(CultureInfo.InvariantCulture, $"The 'anih' header declares a length of {declaredLength}; {AniFormat.HeaderLength} is expected."));

        var frameCount = BinaryPrimitives.ReadUInt32LittleEndian(header[4..]);
        var stepCount = BinaryPrimitives.ReadUInt32LittleEndian(header[8..]);
        var displayRate = BinaryPrimitives.ReadUInt32LittleEndian(header[28..]);
        var flags = BinaryPrimitives.ReadUInt32LittleEndian(header[32..]);
        if (frameCount == 0)
            throw AniFormat.Invalid("The 'anih' header declares no frame.");

        if (stepCount == 0)
            throw AniFormat.Invalid("The 'anih' header declares no step.");

        // Every step becomes a displayed frame; checked before any table sized by these counts is read
        var limit = context.Limits.MaxFrames;
        if (frameCount > limit)
            throw new ImageResourceLimitException(ImageResourceLimitKind.Frames, limit, frameCount);

        if (stepCount > limit)
            throw new ImageResourceLimitException(ImageResourceLimitKind.Frames, limit, stepCount);

        if ((flags & AniFormat.IconFlag) == 0)
            throw AniFormat.Unsupported("The frames of the animated cursor are raw bitmaps (the icon flag of the 'anih' header is not set); only frames stored as icon or cursor files are supported.", "Raw bitmap frames");

        return ((int)frameCount, (int)stepCount, displayRate);
    }

    private static PooledBuffer ReadTable(RandomAccessSource source, ChunkRange range, int stepCount, string name)
    {
        var expected = (long)stepCount * AniFormat.TableEntryLength;
        if (range.Length != expected)
            throw AniFormat.Invalid(string.Create(CultureInfo.InvariantCulture, $"The '{name}' chunk has {range.Length} bytes; {expected} are expected for {stepCount} steps."));

        return source.ReadToBuffer(range.Offset, range.Length);
    }

    private static void ValidateSequence(ReadOnlySpan<byte> sequence, int frameCount)
    {
        for (var step = 0; step < sequence.Length / AniFormat.TableEntryLength; step++)
        {
            var index = BinaryPrimitives.ReadUInt32LittleEndian(sequence[(step * AniFormat.TableEntryLength)..]);
            if (index >= frameCount)
                throw AniFormat.Invalid(string.Create(CultureInfo.InvariantCulture, $"The step {step} of the 'seq ' chunk shows the frame {index}, but the animated cursor has {frameCount} frames."));
        }
    }

    private static AniFrame[] ReadFrameList(RandomAccessSource source, ChunkRange range, int frameCount)
    {
        var frames = new List<AniFrame>();
        var position = range.Offset;
        var end = range.Offset + range.Length;
        Span<byte> header = stackalloc byte[AniFormat.ChunkHeaderLength];
        while (position < end)
        {
            var chunk = ReadChunkHeader(source, ref position, end, header, "the 'fram' list");
            if (!header[..4].SequenceEqual(AniFormat.Icon))
                continue;

            if (frames.Count == frameCount)
                throw AniFormat.Invalid(string.Create(CultureInfo.InvariantCulture, $"The 'fram' list stores more 'icon' chunks than the {frameCount} frames the 'anih' header declares."));

            frames.Add(new AniFrame(frames.Count, chunk.Offset, chunk.Length));
        }

        if (frames.Count != frameCount)
            throw AniFormat.Invalid(string.Create(CultureInfo.InvariantCulture, $"The 'fram' list stores {frames.Count} 'icon' chunks, but the 'anih' header declares {frameCount} frames."));

        return [.. frames];
    }

    /// <summary>Reads the title and the author: NUL-terminated strings, read as Latin-1 like the other legacy text of the library.</summary>
    private static void ReadInfo(RandomAccessSource source, ChunkRange range, DecodedMetadataBuilder metadata)
    {
        var position = range.Offset;
        var end = range.Offset + range.Length;
        var hasTitle = false;
        var hasAuthor = false;
        Span<byte> header = stackalloc byte[AniFormat.ChunkHeaderLength];
        while (position < end)
        {
            var chunk = ReadChunkHeader(source, ref position, end, header, "the 'INFO' list");
            string keyword;
            if (header[..4].SequenceEqual(AniFormat.Title) && !hasTitle)
            {
                hasTitle = true;
                keyword = AniFormat.TitleKeyword;
            }
            else if (header[..4].SequenceEqual(AniFormat.Author) && !hasAuthor)
            {
                hasAuthor = true;
                keyword = AniFormat.AuthorKeyword;
            }
            else
            {
                continue;
            }

            metadata.Charge(chunk.Length);
            using var data = source.ReadToBuffer(chunk.Offset, chunk.Length);
            var text = data.Span;
            var terminator = text.IndexOf((byte)0);
            metadata.AddText(keyword, Encoding.Latin1.GetString(terminator < 0 ? text : text[..terminator]));
        }
    }

    /// <summary>Resolves the icon directory of every frame a step shows, in the order the steps first show them.</summary>
    private static void ResolveFrames(RandomAccessSource source, ImageCodecContext context, AniStructure structure)
    {
        var hasSixteenBitFrame = false;
        for (var step = 0; step < structure.StepCount; step++)
        {
            var frame = structure.GetFrame(step);
            if (frame.Selected is not null)
                continue;

            context.CancellationToken.ThrowIfCancellationRequested();
            IcoRepresentation[] representations;
            try
            {
                // The offsets of the embedded file start at its first byte and cannot reach outside its chunk
                frame.Source = source.Slice(frame.Offset, frame.Length, ImageFormat.Ani);
                (frame.ContainerFormat, representations) = IcoDirectory.Read(frame.Source, context);
            }
            catch (ImageException exception) when (exception is InvalidImageContentException or UnsupportedImageFeatureException)
            {
                throw Rewrap(frame.Index, exception);
            }

            if (step == 0)
            {
                var selected = IcoDirectory.SelectDefault(representations);
                frame.Selected = selected;
                structure.Canvas = selected.Size;
                structure.ColorModel = selected.ColorModel;
            }
            else
            {
                frame.Selected = SelectBySize(representations, structure.Canvas)
                    ?? throw AniFormat.Unsupported(string.Create(CultureInfo.InvariantCulture, $"The frame {frame.Index} of the animated cursor has no {structure.Canvas.Width}x{structure.Canvas.Height} representation, the size of the first displayed frame; every frame of an animation has the canvas size."), "Frame size");
            }

            hasSixteenBitFrame |= PixelFormats.GetBitsPerComponent(frame.Selected.PixelFormat) > 8;
            structure.BitsPerComponent = Math.Max(structure.BitsPerComponent, frame.Selected.BitsPerComponent);
            structure.MayHaveTransparency |= frame.Selected.MayHaveTransparency;
        }

        structure.PixelFormat = DefaultPixelFormats.ForAni(hasSixteenBitFrame);
    }

    /// <summary>Selects the representation of exactly <paramref name="size"/> with the most bits per stored pixel (the first one on a tie).</summary>
    private static IcoRepresentation? SelectBySize(IcoRepresentation[] representations, Size size)
    {
        IcoRepresentation? best = null;
        foreach (var representation in representations)
        {
            if (representation.Size == size && (best is null || representation.BitsPerPixel > best.BitsPerPixel))
            {
                best = representation;
            }
        }

        return best;
    }

    /// <summary>Reports an error of an embedded icon or cursor file as an error of the animated cursor, naming the frame.</summary>
    internal static Exception Rewrap(int frameIndex, ImageException exception) => exception switch
    {
        UnsupportedImageFeatureException unsupported => AniFormat.Unsupported(string.Create(CultureInfo.InvariantCulture, $"The frame {frameIndex} of the animated cursor is not supported: {unsupported.Message}"), unsupported.Feature ?? "Frame payload"),
        _ => AniFormat.Invalid(string.Create(CultureInfo.InvariantCulture, $"The frame {frameIndex} of the animated cursor is malformed: {exception.Message}"), exception),
    };

    private static string FormatFourCC(ReadOnlySpan<byte> fourCC)
    {
        Span<char> text = stackalloc char[4];
        for (var i = 0; i < text.Length; i++)
        {
            text[i] = fourCC[i] is >= 0x20 and < 0x7F ? (char)fourCC[i] : '?';
        }

        return new string(text);
    }

    [StructLayout(LayoutKind.Auto)]
    private readonly record struct ChunkRange(long Offset, long Length);

    [StructLayout(LayoutKind.Auto)]
    private struct ChunkList
    {
        public ChunkRange? Header;
        public ChunkRange? Rate;
        public ChunkRange? Sequence;
        public ChunkRange? Frames;
        public ChunkRange? Info;
    }
}
