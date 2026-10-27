namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// Turns the stored data of a chunk into its uncompressed data, whatever its compression: the state of the decompressors
/// (PIZ tables, scratch planes) is rented once, for the largest chunk of the image.
/// </summary>
internal sealed class ExrChunkDecompressor : IDisposable
{
    private readonly AllocationScope _scope;
    private readonly ExrCompressionMethod _compression;
    private readonly PooledBuffer? _scratch;
    private readonly ExrPiz? _piz;

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
    public void Decompress(ReadOnlySpan<byte> stored, Span<byte> destination, in ExrChunkLayout layout)
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

            case ExrCompressionMethod.Rle:
            {
                var scratch = _scratch!.Span[..destination.Length];
                ExrRle.Decode(stored, scratch);
                ExrPredictor.Decode(scratch, destination);
                break;
            }

            case ExrCompressionMethod.Zips or ExrCompressionMethod.Zip:
            {
                var scratch = _scratch!.Span[..destination.Length];
                Inflate(stored, scratch);
                ExrPredictor.Decode(scratch, destination);
                break;
            }

            case ExrCompressionMethod.Piz:
                _piz!.Decompress(stored, destination, layout);
                break;

            case ExrCompressionMethod.Pxr24:
            {
                var scratch = _scratch!.Span[..ExrPxr24.GetInflatedLength(layout)];
                Inflate(stored, scratch);
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

    public void Dispose()
    {
        _scratch?.Dispose();
        _piz?.Dispose();
    }

    private void Inflate(ReadOnlySpan<byte> stored, Span<byte> destination)
    {
        // The zlib stream reads a buffer
        using var copy = _scope.Rent(stored.Length, AllocationKind.DecoderState, clear: false);
        stored.CopyTo(copy.Span);
        ExrZlib.Inflate(copy, destination);
    }
}
