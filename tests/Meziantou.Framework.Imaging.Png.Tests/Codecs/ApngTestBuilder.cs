using System.Buffers.Binary;

namespace Meziantou.Framework.Imaging.Tests.Codecs;

/// <summary>
/// Assembles APNG files for decoder unit tests from <see cref="PngTestImage"/> frame regions (independent test-side encoder:
/// forward filters, Adam7, BCL zlib): <c>acTL</c>, an optional separate poster (<c>IDAT</c> without <c>fcTL</c>), then one
/// <c>fcTL</c> per frame followed by <c>IDAT</c> (default image as frame zero) or <c>fdAT</c> chunks. Sequence numbers are
/// assigned in order unless a test overrides them to inject defects.
/// </summary>
internal sealed class ApngTestBuilder
{
    private readonly SyntheticImages.PngBuilder _png = new();
    private readonly byte _colorType;
    private readonly byte _bitDepth;
    private readonly bool _interlaced;
    private bool _imageDataWritten;
    private uint _sequence;

    public ApngTestBuilder(int width, int height, uint frameCount, uint plays = 0, byte colorType = 6, byte bitDepth = 8, bool interlaced = false, byte[]? palette = null, byte[]? transparency = null)
    {
        Width = width;
        Height = height;
        _colorType = colorType;
        _bitDepth = bitDepth;
        _interlaced = interlaced;
        _png.Header(width, height, bitDepth, colorType, interlaced ? (byte)1 : (byte)0);
        if (palette is not null)
        {
            _png.Chunk("PLTE", palette);
        }

        if (transparency is not null)
        {
            _png.Chunk("tRNS", transparency);
        }

        Span<byte> actl = stackalloc byte[8];
        BinaryPrimitives.WriteUInt32BigEndian(actl, frameCount);
        BinaryPrimitives.WriteUInt32BigEndian(actl[4..], plays);
        _png.Chunk("acTL", actl);
    }

    public int Width { get; }

    public int Height { get; }

    /// <summary>Creates an RGBA 8-bit region from pixels (row-major).</summary>
    public static PngTestImage Rgba(int width, int height, params Rgba32[] pixels)
    {
        Assert.HasCount(width * height, pixels);
        return PngTestImage.Create(width, height, 6, 8, [.. pixels.SelectMany(p => new ushort[] { p.R, p.G, p.B, p.A })]);
    }

    /// <summary>Creates an RGBA 16-bit region from pixels (row-major).</summary>
    public static PngTestImage Rgba(int width, int height, params Rgba64[] pixels)
    {
        Assert.HasCount(width * height, pixels);
        return PngTestImage.Create(width, height, 6, 16, [.. pixels.SelectMany(p => new[] { p.R, p.G, p.B, p.A })]);
    }

    /// <summary>Writes a separate poster: an <c>IDAT</c> image without <c>fcTL</c> (must be called first).</summary>
    public ApngTestBuilder Poster(PngTestImage image)
    {
        Assert.False(_imageDataWritten);
        WriteImageData(image, fdat: false);
        return this;
    }

    /// <summary>Writes a frame: <c>fcTL</c>, then <c>IDAT</c> (the first frame when there is no poster) or <c>fdAT</c> chunks.</summary>
    public ApngTestBuilder Frame(PngTestImage image, int x = 0, int y = 0, byte dispose = 0, byte blend = 0, ushort delayNumerator = 1, ushort delayDenominator = 10, int chunks = 1, uint? sequence = null)
    {
        Span<byte> data = stackalloc byte[26];
        BinaryPrimitives.WriteUInt32BigEndian(data, sequence ?? _sequence++);
        BinaryPrimitives.WriteInt32BigEndian(data[4..], image.Width);
        BinaryPrimitives.WriteInt32BigEndian(data[8..], image.Height);
        BinaryPrimitives.WriteInt32BigEndian(data[12..], x);
        BinaryPrimitives.WriteInt32BigEndian(data[16..], y);
        BinaryPrimitives.WriteUInt16BigEndian(data[20..], delayNumerator);
        BinaryPrimitives.WriteUInt16BigEndian(data[22..], delayDenominator);
        data[24] = dispose;
        data[25] = blend;
        _png.Chunk("fcTL", data);
        WriteImageData(image, fdat: _imageDataWritten, chunks);
        return this;
    }

    /// <summary>Writes a raw chunk (defect injection).</summary>
    public ApngTestBuilder Chunk(string type, ReadOnlySpan<byte> data)
    {
        _png.Chunk(type, data);
        return this;
    }

    public byte[] ToArray(bool end = true) => end ? _png.End().ToArray() : _png.ToArray();

    private void WriteImageData(PngTestImage image, bool fdat, int chunks = 1)
    {
        Assert.Equal(_colorType, image.ColorType);
        Assert.Equal(_bitDepth, image.BitDepth);
        var stream = SyntheticImages.Zlib(image.GetFilteredData(_interlaced));
        var size = (stream.Length + chunks - 1) / chunks;
        for (var offset = 0; offset < stream.Length; offset += size)
        {
            var piece = stream.AsSpan(offset, Math.Min(size, stream.Length - offset));
            if (fdat)
            {
                var payload = new byte[4 + piece.Length];
                BinaryPrimitives.WriteUInt32BigEndian(payload, _sequence++);
                piece.CopyTo(payload.AsSpan(4));
                _png.Chunk("fdAT", payload);
            }
            else
            {
                _png.Chunk("IDAT", piece);
            }
        }

        _imageDataWritten = true;
    }
}
