using System.Buffers.Binary;
using System.Runtime.InteropServices;

namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// The ZSTD compression of a chunk buffer (compression notes, section 13): a 24-byte prefix, then a Zstandard frame whose
/// content is one or two segments (the bytes of the 2-byte samples, then those of the 4-byte samples), each byte-shuffled,
/// optionally with a delta of the samples of each piece.
/// </summary>
/// <remarks>
/// A buffer is a sequence of pieces (<see cref="Piece"/>): the samples of a channel on a line, in the order of the
/// uncompressed data. The sorted buffer holds the pieces of 2-byte samples, in that order, then those of 4-byte samples.
/// </remarks>
internal sealed class ExrZstd : IDisposable
{
    private const int PrefixLength = 24;

    private static ReadOnlySpan<byte> Magic => "zstd-exr"u8;

    private readonly ZstdDecoder _zstd = new();
    private readonly AllocationScope _scope;
    private PooledBuffer? _inner;
    private PooledBuffer? _sorted;

    public ExrZstd(AllocationScope scope)
    {
        _scope = scope;
    }

    /// <summary>The samples of a channel on a line: where they are in the uncompressed data, their number and their size (2 or 4 bytes).</summary>
    [StructLayout(LayoutKind.Auto)]
    public readonly record struct Piece(int Offset, int Count, int ElementSize)
    {
        public int Length => Count * ElementSize;
    }

    /// <summary>Decompresses a buffer that is not stored raw (section 13.1).</summary>
    /// <param name="stored">The stored bytes.</param>
    /// <param name="destination">Receives the uncompressed buffer.</param>
    /// <param name="pieces">The pieces of the buffer, in the order of <paramref name="destination"/>; they cover it exactly.</param>
    /// <exception cref="InvalidImageContentException">The data are malformed.</exception>
    public void Decompress(ReadOnlySpan<byte> stored, Span<byte> destination, ReadOnlySpan<Piece> pieces)
    {
        if (stored.Length < PrefixLength || !stored.StartsWith(Magic))
            throw ExrFormat.Invalid("the ZSTD data of a chunk do not start with their prefix.");

        var format = BinaryPrimitives.ReadUInt32LittleEndian(stored[8..]);
        var flags = BinaryPrimitives.ReadUInt32LittleEndian(stored[12..]);
        var length = BinaryPrimitives.ReadUInt64LittleEndian(stored[16..]);
        if (format is not (1 or 2) || (format == 1 && flags != 0) || (flags & ~1u) != 0)
            throw ExrFormat.Invalid(string.Create(CultureInfo.InvariantCulture, $"the ZSTD data of a chunk have the format {format} and the flags {flags}."));

        if (length != (ulong)(stored.Length - PrefixLength))
            throw ExrFormat.Invalid("the ZSTD data of a chunk do not have the length their prefix gives.");

        // Section 13.2: the segments of the 2-byte and of the 4-byte samples
        var halfLength = 0;
        foreach (var piece in pieces)
        {
            if (piece.ElementSize == 2)
            {
                halfLength += piece.Length;
            }
        }

        var total = destination.Length;
        var segmentCount = halfLength > 0 && halfLength < total ? 2 : 1;
        var innerLength = total + (8 * segmentCount);
        var inner = Rent(ref _inner, innerLength)[..innerLength];
        try
        {
            _zstd.Decompress(stored[PrefixLength..], inner);
        }
        catch (InvalidDataException exception)
        {
            throw new InvalidImageContentException("The OpenEXR file is invalid: the ZSTD data of a chunk are malformed: " + exception.Message, ImageFormat.OpenExr, exception);
        }

        var sorted = Rent(ref _sorted, total)[..total];
        var position = 0;
        var sortedPosition = 0;
        for (var segment = 0; segment < segmentCount; segment++)
        {
            var expected = segmentCount == 2 ? (segment == 0 ? halfLength : total - halfLength) : total;
            var elementSize = segmentCount == 2 ? (segment == 0 ? 2 : 4) : (halfLength > 0 ? 2 : 4);
            if (BinaryPrimitives.ReadUInt64LittleEndian(inner[position..]) != (ulong)expected)
                throw ExrFormat.Invalid("a segment of the ZSTD data of a chunk does not have the expected length.");

            position += 8;
            Unshuffle(inner.Slice(position, expected), sorted.Slice(sortedPosition, expected), elementSize);
            position += expected;
            sortedPosition += expected;
        }

        // Section 13.3: the delta restarts at every piece; then the pieces go back to their place
        var delta = format == 2 && (flags & 1) != 0;
        var offset = 0;
        for (var size = 2; size <= 4; size += 2)
        {
            foreach (var piece in pieces)
            {
                if (piece.ElementSize != size)
                    continue;

                var samples = sorted.Slice(offset, piece.Length);
                if (delta)
                {
                    UndoDelta(samples, size);
                }

                samples.CopyTo(destination.Slice(piece.Offset, piece.Length));
                offset += piece.Length;
            }
        }
    }

    public void Dispose()
    {
        _inner?.Dispose();
        _inner = null;
        _sorted?.Dispose();
        _sorted = null;
    }

    private static void Unshuffle(ReadOnlySpan<byte> planes, Span<byte> elements, int elementSize)
    {
        var count = planes.Length / elementSize;
        for (var plane = 0; plane < elementSize; plane++)
        {
            var source = planes.Slice(plane * count, count);
            for (var i = 0; i < count; i++)
            {
                elements[(i * elementSize) + plane] = source[i];
            }
        }
    }

    private static void UndoDelta(Span<byte> samples, int size)
    {
        if (size == 2)
        {
            var previous = (ushort)0;
            for (var i = 0; i < samples.Length; i += 2)
            {
                previous = (ushort)(previous + BinaryPrimitives.ReadUInt16LittleEndian(samples[i..]));
                BinaryPrimitives.WriteUInt16LittleEndian(samples[i..], previous);
            }

            return;
        }

        var last = 0u;
        for (var i = 0; i < samples.Length; i += 4)
        {
            last += BinaryPrimitives.ReadUInt32LittleEndian(samples[i..]);
            BinaryPrimitives.WriteUInt32LittleEndian(samples[i..], last);
        }
    }

    private Span<byte> Rent(ref PooledBuffer? buffer, int length)
    {
        if (buffer is null || buffer.Length < length)
        {
            buffer?.Dispose();
            buffer = null;
            buffer = _scope.Rent(Math.Max(length, 1), AllocationKind.DecoderState, clear: false);
        }

        return buffer.Span;
    }
}
