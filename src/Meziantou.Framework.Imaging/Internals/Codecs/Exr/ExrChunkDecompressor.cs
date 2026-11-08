namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// Turns the stored data of a chunk into its uncompressed data, whatever its compression: the state of the decompressors
/// (PIZ tables, scratch planes) is rented once, for the largest chunk of the image.
/// </summary>
internal sealed class ExrChunkDecompressor : IDisposable
{
    private readonly AllocationScope _scope;
    private readonly ExrCompressionMethod _compression;
    private readonly ExrPiz? _piz;
    private PooledBuffer? _scratch;
    private readonly ExrZstd? _zstd;
    private readonly ExrDwa? _dwa;
    private ExrZstd.Piece[] _pieces = [];

    public ExrChunkDecompressor(AllocationScope scope, ExrCompressionMethod compression, int maxDataLength)
    {
        _scope = scope;
        _compression = compression;
        try
        {
            if (compression == ExrCompressionMethod.Piz)
            {
                _piz = new ExrPiz(scope, maxDataLength, encoder: false);
            }
            else if (compression == ExrCompressionMethod.Zstd)
            {
                _zstd = new ExrZstd(scope);
            }
            else if (compression is ExrCompressionMethod.Dwaa or ExrCompressionMethod.Dwab)
            {
                _dwa = new ExrDwa(scope);
            }
            else if (compression != ExrCompressionMethod.None)
            {
                _scratch = scope.Rent(Math.Max(maxDataLength, 1), AllocationKind.DecoderState, clear: false);
            }
        }
        catch
        {
            Dispose();
            throw;
        }
    }

    /// <summary>Decompresses a chunk.</summary>
    /// <param name="stored">The stored data of the chunk.</param>
    /// <param name="destination">Receives the uncompressed data (its length is the size of the chunk the header gives).</param>
    /// <param name="layout">The geometry of the chunk.</param>
    /// <exception cref="InvalidImageContentException">The data are malformed.</exception>
    public void Decompress(ReadOnlySpan<byte> stored, Span<byte> destination, in ExrChunkLayout layout) => Decompress(stored, default, destination, layout);

    /// <summary>Decompresses a chunk whose stored data are in a buffer of the scope, which the zlib stream reads without a copy.</summary>
    /// <param name="stored">The stored data of the chunk.</param>
    /// <param name="destination">Receives the uncompressed data (its length is the size of the chunk the header gives).</param>
    /// <param name="layout">The geometry of the chunk.</param>
    /// <exception cref="InvalidImageContentException">The data are malformed.</exception>
    public void Decompress(ArraySegment<byte> stored, Span<byte> destination, in ExrChunkLayout layout) => Decompress(stored.AsSpan(), stored, destination, layout);

    private void Decompress(ReadOnlySpan<byte> stored, ArraySegment<byte> storedArray, Span<byte> destination, in ExrChunkLayout layout)
    {
        // Stored data of the size of the uncompressed data are the uncompressed data, whatever the compression (section 1.5)
        if (stored.Length == destination.Length)
        {
            stored.CopyTo(destination);
            return;
        }

        if (stored.IsEmpty)
            throw ExrFormat.Invalid("a chunk has no data.");

        switch (_compression)
        {
            case ExrCompressionMethod.None:
                throw ExrFormat.Invalid(string.Create(CultureInfo.InvariantCulture, $"an uncompressed chunk has {stored.Length} bytes; {destination.Length} are expected."));

            case ExrCompressionMethod.Rle or ExrCompressionMethod.Zips or ExrCompressionMethod.Zip:
                DecompressBytes(stored, storedArray, destination);
                break;

            case ExrCompressionMethod.Zstd:
                _zstd!.Decompress(stored, destination, GetPieces(layout));
                break;

            case ExrCompressionMethod.Dwaa or ExrCompressionMethod.Dwab:
                _dwa!.Decompress(stored, destination, layout);
                break;

            case ExrCompressionMethod.Piz:
                _piz!.Decompress(stored, destination, layout);
                break;

            case ExrCompressionMethod.Pxr24:
            {
                var scratch = _scratch!.Span[..ExrPxr24.GetInflatedLength(layout)];
                Inflate(stored, storedArray, scratch);
                ExrPxr24.Decode(scratch, destination, layout);
                break;
            }

            case ExrCompressionMethod.B44 or ExrCompressionMethod.B44A:
            {
                var scratch = _scratch!.Span[..destination.Length];
                ExrB44.Decode(stored, scratch, layout);
                layout.InterleavePlanes(scratch, destination);
                break;
            }

            default:
                throw new InvalidOperationException("The compression is not decoded.");
        }
    }

    /// <summary>Decompresses the sample count table of a deep chunk (section 1.6): <paramref name="width"/> entries per row.</summary>
    /// <param name="stored">The stored table.</param>
    /// <param name="destination">Receives the table.</param>
    /// <param name="width">The number of entries of a row.</param>
    /// <param name="tileLength">
    /// The size of the table of an unclipped tile: a writer stores the table of a clipped tile raw in a buffer of that size
    /// (the table first, then leftover bytes), which is read as raw too; 0 for scan lines.
    /// </param>
    /// <exception cref="InvalidImageContentException">The data are malformed.</exception>
    public void DecompressDeepTable(ReadOnlySpan<byte> stored, Span<byte> destination, int width, int tileLength)
    {
        if (stored.Length != destination.Length && (stored.Length == tileLength || (_compression == ExrCompressionMethod.None && stored.Length > destination.Length && tileLength > 0)))
        {
            stored[..destination.Length].CopyTo(destination);
            return;
        }

        if (DecompressDeepRaw(stored, destination))
            return;

        if (_compression != ExrCompressionMethod.Zstd)
        {
            DecompressBytes(stored, default, destination);
            return;
        }

        // Section 13.4: one 4-byte channel of the width of the chunk on each row
        var rows = destination.Length / 4 / Math.Max(width, 1);
        var pieces = EnsurePieces(rows);
        for (var row = 0; row < rows; row++)
        {
            pieces[row] = new ExrZstd.Piece(row * width * 4, width, 4);
        }

        _zstd!.Decompress(stored, destination, pieces[..rows]);
    }

    /// <summary>Decompresses the samples of a deep chunk (section 1.6): for each row, for each channel, the samples of the row.</summary>
    /// <param name="stored">The stored samples.</param>
    /// <param name="destination">Receives the samples.</param>
    /// <param name="channels">The channels, in name order.</param>
    /// <param name="rowTotals">The number of samples of each row.</param>
    /// <exception cref="InvalidImageContentException">The data are malformed.</exception>
    public void DecompressDeepSamples(ReadOnlySpan<byte> stored, Span<byte> destination, IReadOnlyList<ExrChannel> channels, ReadOnlySpan<int> rowTotals)
    {
        if (destination.IsEmpty)
        {
            if (!stored.IsEmpty)
                throw ExrFormat.Invalid("a deep chunk without samples has sample data.");

            return;
        }

        if (DecompressDeepRaw(stored, destination))
            return;

        if (_compression != ExrCompressionMethod.Zstd)
        {
            DecompressBytes(stored, default, destination);
            return;
        }

        var pieces = EnsurePieces(rowTotals.Length * channels.Count);
        var count = 0;
        var offset = 0;
        foreach (var total in rowTotals)
        {
            foreach (var channel in channels)
            {
                pieces[count++] = new ExrZstd.Piece(offset, total, channel.BytesPerSample);
                offset += total * channel.BytesPerSample;
            }
        }

        _zstd!.Decompress(stored, destination, pieces[..count]);
    }

    public void Dispose()
    {
        _scratch?.Dispose();
        _piz?.Dispose();
        _zstd?.Dispose();
        _dwa?.Dispose();
    }

    /// <summary>Copies a deep buffer stored raw; with NONE, the buffer must be stored raw.</summary>
    private bool DecompressDeepRaw(ReadOnlySpan<byte> stored, Span<byte> destination)
    {
        if (stored.Length == destination.Length)
        {
            stored.CopyTo(destination);
            return true;
        }

        if (_compression == ExrCompressionMethod.None)
            throw ExrFormat.Invalid(string.Create(CultureInfo.InvariantCulture, $"an uncompressed deep buffer has {stored.Length} bytes; {destination.Length} are expected."));

        if (stored.IsEmpty)
            throw ExrFormat.Invalid("a deep buffer has no data.");

        return false;
    }

    /// <summary>Decompresses the bytes of a buffer with RLE, ZIPS or ZIP, then undoes the predictor (sections 2 to 4).</summary>
    private void DecompressBytes(ReadOnlySpan<byte> stored, ArraySegment<byte> storedArray, Span<byte> destination)
    {
        var scratch = EnsureScratch(destination.Length);
        if (_compression == ExrCompressionMethod.Rle)
        {
            ExrRle.Decode(stored, scratch);
        }
        else
        {
            Inflate(stored, storedArray, scratch);
        }

        ExrPredictor.Decode(scratch, destination);
    }

    private Span<byte> EnsureScratch(int length)
    {
        if (_scratch is null || _scratch.Length < length)
        {
            _scratch?.Dispose();
            _scratch = null;
            _scratch = _scope.Rent(Math.Max(length, 1), AllocationKind.DecoderState, clear: false);
        }

        return _scratch.Span[..length];
    }

    private Span<ExrZstd.Piece> EnsurePieces(int count)
    {
        if (_pieces.Length < count)
        {
            _pieces = new ExrZstd.Piece[Math.Max(count, 2 * _pieces.Length)];
        }

        return _pieces;
    }

    /// <summary>Gets the pieces of a flat chunk (section 13.3): for each line, for each channel that has a sample line there, its samples.</summary>
    private Span<ExrZstd.Piece> GetPieces(in ExrChunkLayout layout)
    {
        var channels = layout.Channels;
        var pieces = EnsurePieces(layout.Lines * channels.Count);
        var count = 0;
        var offset = 0;
        for (var line = 0; line < layout.Lines; line++)
        {
            for (var c = 0; c < channels.Count; c++)
            {
                if (!layout.HasLine(c, line))
                    continue;

                var samples = layout.GetSampleCount(c);
                pieces[count++] = new ExrZstd.Piece(offset, samples, channels[c].BytesPerSample);
                offset += samples * channels[c].BytesPerSample;
            }
        }

        return pieces[..count];
    }

    private void Inflate(ReadOnlySpan<byte> stored, ArraySegment<byte> storedArray, Span<byte> destination)
    {
        if (storedArray.Array is not null)
        {
            ExrZlib.Inflate(storedArray, destination);
            return;
        }

        // The zlib stream reads a buffer
        using var copy = _scope.Rent(stored.Length, AllocationKind.DecoderState, clear: false);
        stored.CopyTo(copy.Span);
        ExrZlib.Inflate(copy, destination);
    }
}
