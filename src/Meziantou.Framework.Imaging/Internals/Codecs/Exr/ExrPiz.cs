using System.Buffers.Binary;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;

namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// The PIZ compression (compression notes, section 5): the samples of each channel as planes of 16-bit words, the values
/// that occur replaced by their rank (a bitmap of the values is stored), a wavelet on each plane, and a Huffman code over
/// all the words.
/// </summary>
/// <remarks>The buffers are rented once from the scope of the owner, for the largest chunk of the image.</remarks>
internal sealed class ExrPiz : IDisposable
{
    private const int BitmapLength = 8192;

    private readonly PooledBuffer _words;
    private readonly PooledBuffer _table;
    private readonly PooledBuffer _bitmap;
    private readonly ExrHuffman _huffman;

    // The entries of the table past those of the values are zero up to this one: the table is rented without being cleared
    private int _tableDirtyEnd = 65536;

    public ExrPiz(AllocationScope scope, int maxDataLength, bool encoder)
    {
        _words = scope.Rent(Math.Max(maxDataLength, 2), AllocationKind.DecoderState, clear: false);
        try
        {
            _table = scope.Rent(65536 * sizeof(ushort), AllocationKind.DecoderState, clear: false);
            _bitmap = scope.Rent(BitmapLength, AllocationKind.DecoderState);
            _huffman = new ExrHuffman(scope, encoder);
        }
        catch
        {
            Dispose();
            throw;
        }
    }

    private Span<ushort> Table => unsafe(MemoryMarshal.Cast<byte, ushort>(_table.Span));

    /// <summary>Decompresses a chunk.</summary>
    /// <param name="stored">The stored data of the chunk.</param>
    /// <param name="destination">Receives the uncompressed data of the chunk.</param>
    /// <param name="layout">The geometry of the chunk.</param>
    public void Decompress(ReadOnlySpan<byte> stored, Span<byte> destination, in ExrChunkLayout layout)
    {
        if (stored.Length < 4)
            throw ExrFormat.Invalid("a PIZ chunk is truncated.");

        var first = BinaryPrimitives.ReadUInt16LittleEndian(stored);
        var last = BinaryPrimitives.ReadUInt16LittleEndian(stored[2..]);
        var position = 4;
        var bitmap = _bitmap.Span;
        bitmap.Clear();
        if (first <= last)
        {
            if (last >= BitmapLength)
                throw ExrFormat.Invalid(string.Create(CultureInfo.InvariantCulture, $"the bitmap of a PIZ chunk ends at the byte {last}; at most {BitmapLength - 1} is allowed."));

            var count = last - first + 1;
            if (count > stored.Length - position)
                throw ExrFormat.Invalid("the bitmap of a PIZ chunk is truncated.");

            stored.Slice(position, count).CopyTo(bitmap[first..]);
            position += count;
        }

        if (stored.Length - position < 4)
            throw ExrFormat.Invalid("a PIZ chunk is truncated.");

        var streamLength = BinaryPrimitives.ReadUInt32LittleEndian(stored[position..]);
        position += 4;
        if (streamLength > (uint)(stored.Length - position))
            throw ExrFormat.Invalid("the Huffman data of a PIZ chunk extend past the end of the chunk.");

        // The reverse lookup table: the k-th value that occurs (0 always does)
        var table = Table;
        var maxIndex = BuildReverseTable(bitmap, table, _tableDirtyEnd);
        _tableDirtyEnd = maxIndex + 1;
        var words = unsafe(MemoryMarshal.Cast<byte, ushort>(_words.Span[..destination.Length]));
        _huffman.Decode(stored.Slice(position, (int)streamLength), words);

        var fourteenBits = maxIndex < (1 << 14);
        var offset = 0;
        for (var c = 0; c < layout.Channels.Count; c++)
        {
            var nx = layout.GetSampleCount(c);
            var ny = layout.GetLineCount(c);
            var w = layout.Channels[c].BytesPerSample / 2;
            for (var j = 0; j < w; j++)
            {
                ExrWavelet.Decode(words, offset + j, nx, w, ny, w * nx, fourteenBits);
            }

            offset += nx * ny * w;
        }

        CopyPlanes(words, default, destination, table, layout, toPlanes: false);
    }

    /// <summary>Compresses a chunk.</summary>
    /// <param name="source">The uncompressed data of the chunk.</param>
    /// <param name="destination">Receives the stored data.</param>
    /// <param name="layout">The geometry of the chunk.</param>
    /// <returns>The length of the stored data, or -1 when it does not fit <paramref name="destination"/>.</returns>
    public int Compress(ReadOnlySpan<byte> source, Span<byte> destination, in ExrChunkLayout layout)
    {
        var words = unsafe(MemoryMarshal.Cast<byte, ushort>(_words.Span[..source.Length]));
        CopyPlanes(words, source, default, default, layout, toPlanes: true);

        // The values that occur are marked in the table (each word a store, not a read-modify-write of a bitmap byte that
        // the next word may share), then the bitmap is built from the marks and the table gets the rank of each value
        var table = Table;
        _tableDirtyEnd = table.Length;
        table.Clear();
        table[0] = 1;
        foreach (var word in words)
        {
            table[word] = 1;
        }

        var bitmap = _bitmap.Span;
        var rank = 0;
        for (var i = 0; i < bitmap.Length; i++)
        {
            var marks = table.Slice(i * 8, 8);
            var bits = ~Vector128.Equals(Vector128.Create<ushort>(marks), Vector128<ushort>.Zero).ExtractMostSignificantBits() & 0xFF;
            bitmap[i] = (byte)bits;
            while (bits != 0)
            {
                marks[BitOperations.TrailingZeroCount(bits)] = (ushort)rank++;
                bits &= bits - 1;
            }
        }

        bitmap[0] &= 0xFE;
        var first = bitmap.IndexOfAnyExcept((byte)0);
        var last = bitmap.LastIndexOfAnyExcept((byte)0);

        var maxIndex = rank - 1;
        for (var i = 0; i < words.Length; i++)
        {
            words[i] = table[words[i]];
        }

        var fourteenBits = maxIndex < (1 << 14);
        var offset = 0;
        for (var c = 0; c < layout.Channels.Count; c++)
        {
            var nx = layout.GetSampleCount(c);
            var ny = layout.GetLineCount(c);
            var w = layout.Channels[c].BytesPerSample / 2;
            for (var j = 0; j < w; j++)
            {
                ExrWavelet.Encode(words, offset + j, nx, w, ny, w * nx, fourteenBits);
            }

            offset += nx * ny * w;
        }

        var bitmapLength = first < 0 ? 0 : last - first + 1;
        var headerLength = 4 + bitmapLength + 4;
        if (destination.Length < headerLength)
            return -1;

        BinaryPrimitives.WriteUInt16LittleEndian(destination, (ushort)(first < 0 ? BitmapLength - 1 : first));
        BinaryPrimitives.WriteUInt16LittleEndian(destination[2..], (ushort)(first < 0 ? 0 : last));
        if (bitmapLength > 0)
        {
            bitmap.Slice(first, bitmapLength).CopyTo(destination[4..]);
        }

        var streamLength = _huffman.Encode(words, destination[headerLength..]);
        if (streamLength < 0)
            return -1;

        BinaryPrimitives.WriteUInt32LittleEndian(destination[(4 + bitmapLength)..], (uint)streamLength);
        return headerLength + streamLength;
    }

    public void Dispose()
    {
        _words?.Dispose();
        _table?.Dispose();
        _bitmap?.Dispose();
        _huffman?.Dispose();
    }

    /// <summary>
    /// Fills the table with the values that occur in increasing order (0 first, which always does) and zeros after them,
    /// up to <paramref name="dirtyEnd"/> (the entries after it are zero).
    /// </summary>
    /// <returns>The index of the last value.</returns>
    private static int BuildReverseTable(ReadOnlySpan<byte> bitmap, Span<ushort> table, int dirtyEnd)
    {
        table[0] = 0;
        var k = 1;
        for (var i = 0; i < bitmap.Length; i++)
        {
            uint bits = bitmap[i];
            if (i == 0)
            {
                bits &= 0xFE;
            }

            while (bits != 0)
            {
                table[k++] = (ushort)((i * 8) + BitOperations.TrailingZeroCount(bits));
                bits &= bits - 1;
            }
        }

        if (k < dirtyEnd)
        {
            table[k..dirtyEnd].Clear();
        }

        return k - 1;
    }

    /// <summary>
    /// Copies the samples between the layout of a chunk and the planes of words of each channel (section 5.1); to the layout,
    /// each word is replaced by its entry of <paramref name="table"/>.
    /// </summary>
    private static void CopyPlanes(Span<ushort> words, ReadOnlySpan<byte> input, Span<byte> output, ReadOnlySpan<ushort> table, in ExrChunkLayout layout, bool toPlanes)
    {
        var channelCount = layout.Channels.Count;
        Span<int> planeOffsets = channelCount <= 64 ? stackalloc int[channelCount] : new int[channelCount];
        Span<int> rows = channelCount <= 64 ? stackalloc int[channelCount] : new int[channelCount];
        rows.Clear();
        var offset = 0;
        for (var c = 0; c < channelCount; c++)
        {
            planeOffsets[c] = offset;
            offset += layout.GetSampleCount(c) * layout.GetLineCount(c) * (layout.Channels[c].BytesPerSample / 2);
        }

        var position = 0;
        for (var line = 0; line < layout.Lines; line++)
        {
            for (var c = 0; c < channelCount; c++)
            {
                if (!layout.HasLine(c, line))
                    continue;

                var rowWords = layout.GetSampleCount(c) * (layout.Channels[c].BytesPerSample / 2);
                var plane = words.Slice(planeOffsets[c] + (rows[c] * rowWords), rowWords);
                if (toPlanes)
                {
                    var bytes = input.Slice(position, rowWords * 2);
                    if (BitConverter.IsLittleEndian)
                    {
                        var samples = unsafe(MemoryMarshal.Cast<byte, ushort>(bytes));
                        samples.CopyTo(plane);
                    }
                    else
                    {
                        for (var i = 0; i < plane.Length; i++)
                        {
                            plane[i] = BinaryPrimitives.ReadUInt16LittleEndian(bytes[(2 * i)..]);
                        }
                    }
                }
                else if (BitConverter.IsLittleEndian)
                {
                    var samples = unsafe(MemoryMarshal.Cast<byte, ushort>(output.Slice(position, rowWords * 2)));
                    for (var i = 0; i < samples.Length; i++)
                    {
                        samples[i] = table[plane[i]];
                    }
                }
                else
                {
                    var bytes = output.Slice(position, rowWords * 2);
                    for (var i = 0; i < plane.Length; i++)
                    {
                        BinaryPrimitives.WriteUInt16LittleEndian(bytes[(2 * i)..], table[plane[i]]);
                    }
                }

                rows[c]++;
                position += rowWords * 2;
            }
        }
    }
}
