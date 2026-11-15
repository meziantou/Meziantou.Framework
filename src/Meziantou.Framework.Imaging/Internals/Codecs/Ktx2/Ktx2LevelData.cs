using System.Buffers.Binary;
using System.IO.Compression;
using Meziantou.Framework.Hashing;

namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// The data of one level of a texture: read where it is when it is not supercompressed, inflated whole when it is
/// (Zstandard, ZLIB), or the slices of its BasisLZ images.
/// </summary>
internal sealed class Ktx2LevelData : IDisposable
{
    private readonly RandomAccessSource _source;
    private readonly Ktx2Texture _texture;
    private readonly int _level;
    private readonly Ktx2Level _entry;
    private PooledBuffer? _inflated;
    private Etc1SCodebooks? _codebooks;

    private Ktx2LevelData(RandomAccessSource source, Ktx2Texture texture, int level)
    {
        _source = source;
        _texture = texture;
        _level = level;
        _entry = texture.LevelIndex[level];
    }

    public static Ktx2LevelData Open(RandomAccessSource source, Ktx2Texture texture, int level, ImageCodecContext context)
    {
        var data = new Ktx2LevelData(source, texture, level);
        try
        {
            if (texture.Scheme is Ktx2Format.SchemeZstandard or Ktx2Format.SchemeZlib)
            {
                data.Inflate(context);
            }

            return data;
        }
        catch
        {
            data.Dispose();
            throw;
        }
    }

    /// <summary>Reads bytes of the level, once inflated.</summary>
    public void Read(long offset, Span<byte> destination)
    {
        if (_inflated is not null)
        {
            _inflated.Span.Slice((int)offset, destination.Length).CopyTo(destination);
            return;
        }

        _source.Read(_entry.Offset + offset, destination);
    }

    /// <summary>Decodes the ETC1S blocks of an image of a BasisLZ level.</summary>
    public Etc1SIndices DecodeEtc1S(Ktx2Subresource subresource, Etc1SIndices? previous, ImageCodecContext context)
    {
        var image = _texture.BasisImages[_texture.GetBasisImageIndex(subresource)];
        var (columns, rows) = _texture.GetBlockCounts(subresource.MipLevel);
        _codebooks ??= Etc1SCodebooks.Read(_source, _texture, context);
        return Etc1SIndices.Decode(_source, _texture, _codebooks, _entry, image, columns, rows, previous, context);
    }

    public void Dispose()
    {
        _inflated?.Dispose();
        _inflated = null;
        _codebooks?.Dispose();
        _codebooks = null;
    }

    private void Inflate(ImageCodecContext context)
    {
        if (_entry.UncompressedLength > int.MaxValue - 64)
            throw new ImageResourceLimitException(ImageResourceLimitKind.LiveAllocationBytes, context.Limits.MaxLiveAllocationBytes, _entry.UncompressedLength);

        // The uncompressed length is the one the images of the level have (validated): an expansion beyond it is an error
        _inflated = context.Scope.Rent((int)_entry.UncompressedLength, AllocationKind.DecoderState, clear: false);
        PooledBuffer? copy = null;
        try
        {
            if (!_source.TryGetSpan(_entry.Offset, _entry.Length, out var compressed))
            {
                copy = _source.ReadToBuffer(_entry.Offset, _entry.Length);
                compressed = copy.Span;
            }

            var destination = _inflated.Span[..(int)_entry.UncompressedLength];
            if (_texture.Scheme == Ktx2Format.SchemeZstandard)
            {
                try
                {
                    new ZstdDecoder().Decompress(compressed, destination);
                }
                catch (InvalidDataException exception)
                {
                    throw new InvalidImageContentException(string.Create(CultureInfo.InvariantCulture, $"The KTX 2.0 file is invalid: the Zstandard data of the level {_level} are malformed: {exception.Message}"), ImageFormat.Ktx2, exception);
                }
            }
            else
            {
                // The ZLIB stream reads a buffer: a copy rented from the scope when the input is a span
                if (copy is null)
                {
                    copy = context.Scope.Rent(compressed.Length, AllocationKind.DecoderState, clear: false);
                    compressed.CopyTo(copy.Span);
                }

                InflateZlib(copy, destination);
            }
        }
        finally
        {
            copy?.Dispose();
        }
    }

    private void InflateZlib(PooledBuffer stored, Span<byte> destination)
    {
        var compressed = stored.Span;
        if (compressed.Length < 6)
            throw Ktx2Format.Invalid(string.Create(CultureInfo.InvariantCulture, $"the ZLIB data of the level {_level} are truncated."));

        try
        {
            using var input = new MemoryStream(stored.RawBuffer, 0, stored.Length, writable: false);
            using var zlib = new ZLibStream(input, CompressionMode.Decompress);
            var written = 0;
            while (written < destination.Length)
            {
                var read = zlib.Read(destination[written..]);
                if (read <= 0)
                    throw Ktx2Format.Invalid(string.Create(CultureInfo.InvariantCulture, $"the ZLIB data of the level {_level} produce {written} bytes; {destination.Length} are expected."));

                written += read;
            }

            Span<byte> extra = stackalloc byte[1];
            if (zlib.Read(extra) != 0)
                throw Ktx2Format.Invalid(string.Create(CultureInfo.InvariantCulture, $"the ZLIB data of the level {_level} produce more than {destination.Length} bytes."));
        }
        catch (Exception exception) when (exception is InvalidDataException or IOException)
        {
            throw new InvalidImageContentException(string.Create(CultureInfo.InvariantCulture, $"The KTX 2.0 file is invalid: the ZLIB data of the level {_level} are malformed."), ImageFormat.Ktx2, exception);
        }

        // ZLibStream reports a truncated stream as a clean end and does not tell where the stream ends: the checksum must be its last four bytes
        if (Adler32.Compute(destination) != BinaryPrimitives.ReadUInt32BigEndian(compressed[^4..]))
            throw Ktx2Format.Invalid(string.Create(CultureInfo.InvariantCulture, $"the ZLIB data of the level {_level} are truncated, followed by other bytes, or do not match their checksum."));
    }
}
