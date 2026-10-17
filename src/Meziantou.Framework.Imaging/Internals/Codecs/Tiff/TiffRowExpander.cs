using System.Runtime.InteropServices;

namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// Expands the decoded rows of one TIFF page to its lossless working representation
/// (<see cref="TiffPage.SourcePixelFormat"/>): the samples of 1, 2 and 4 bits are unpacked, the grayscale ones scaled
/// exactly to 8 bits, <c>WhiteIsZero</c> samples inverted, palette indexes replaced by their colors, 16-bit samples read
/// in the byte order of the file, and CMYK inks converted to RGB.
/// </summary>
/// <remarks>
/// <para>
/// A grayscale sample of <c>n</c> bits is multiplied by <c>255 / (2^n - 1)</c>, which is an integer for 1, 2 and 4 bits
/// (255, 85 and 17): the scaling is exact and can be undone.
/// </para>
/// <para>
/// The entries of a color map are 16-bit values. Writers store an 8-bit color <c>c</c> as <c>257 c</c> or as
/// <c>256 c</c>: when every entry of the map is a multiple of one of them, the colors are these 8-bit values and the
/// page is <see cref="PixelFormat.Rgb24"/>. Otherwise the map holds colors that 8 bits cannot store and the page is
/// <see cref="PixelFormat.Rgba64"/>, with the entries as they are.
/// </para>
/// <para>
/// The samples of a CMYK page are ink amounts (0 is no ink), which is what the ICC conversion of the inks expects. An
/// alpha sample is copied unchanged.
/// </para>
/// </remarks>
internal sealed class TiffRowExpander : IDisposable
{
    private readonly TiffPage _page;
    private readonly int _width;
    private readonly IccPipeline? _inkConversion;
    private readonly byte[]? _grayLookup;
    private PooledBuffer? _palette;
    private PooledBuffer? _inks;

    /// <summary>Creates the expander of a page.</summary>
    /// <param name="page">The page.</param>
    /// <param name="inkConversion">The conversion of the inks of a CMYK page to RGB; <see langword="null"/> for the other pages.</param>
    /// <param name="scope">The scope charged for the lookup table of a palette and the scratch row of 16-bit inks.</param>
    public TiffRowExpander(TiffPage page, IccPipeline? inkConversion, AllocationScope scope)
    {
        ArgumentNullException.ThrowIfNull(page);
        ArgumentNullException.ThrowIfNull(scope);
        if (page.IsCmyk != (inkConversion is not null))
            throw new ArgumentException("A CMYK page, and only it, needs the conversion of its inks.", nameof(inkConversion));

        _page = page;
        _width = page.Size.Width;
        _inkConversion = inkConversion;
        if (page.IsPalette)
        {
            _palette = CreatePalette(page, scope);
        }
        else if (page.IsGrayscale && page.BitsPerSample < 8)
        {
            _grayLookup = CreateGrayLookup(page);
        }
        else if (page.IsCmyk && page.DecodedBitsPerSample == 16)
        {
            // Five 16-bit samples per pixel in native byte order: the inks and the alpha sample, opaque when the page has none
            _inks = scope.Rent(checked(_width * 5 * sizeof(ushort)), AllocationKind.DecoderState, clear: false);
        }
    }

    /// <summary>Gets the number of bytes of one expanded row.</summary>
    public int ExpandedRowLength => _width * PixelFormats.GetBytesPerPixel(_page.SourcePixelFormat);

    /// <summary>Expands one decoded row.</summary>
    /// <param name="stored">The decoded samples of the row; only the first <c>Width</c> pixels are read (a tile row is padded).</param>
    /// <param name="destination">Exactly <c>Width</c> pixels of <see cref="TiffPage.SourcePixelFormat"/>.</param>
    public void Expand(ReadOnlySpan<byte> stored, Span<byte> destination)
    {
        var page = _page;
        if (page.IsPalette)
        {
            ExpandPalette(stored, destination);
        }
        else if (page.IsCmyk)
        {
            ExpandInks(stored, destination);
        }
        else if (page.BitsPerSample < 8)
        {
            ExpandPackedGray(stored, destination);
        }
        else if (page.BitsPerSample == 8)
        {
            ExpandBytes(stored, destination);
        }
        else
        {
            ExpandWords(stored, destination);
        }
    }

    public void Dispose()
    {
        _palette?.Dispose();
        _palette = null;
        _inks?.Dispose();
        _inks = null;
    }

    /// <summary>Builds the colors of every index: 4 bytes each (RGB and an unused byte), or 4 native 16-bit samples each for a 16-bit color map.</summary>
    private static PooledBuffer CreatePalette(TiffPage page, AllocationScope scope)
    {
        var count = 1 << page.BitsPerSample;
        var map = page.ColorMap;
        if (page.HasSixteenBitColorMap)
        {
            var buffer = scope.Rent(count * 4 * sizeof(ushort), AllocationKind.DecoderState, clear: false);
            var colors = unsafe(MemoryMarshal.Cast<byte, ushort>(buffer.Span));
            for (var i = 0; i < count; i++)
            {
                colors[i * 4] = map[i];
                colors[(i * 4) + 1] = map[count + i];
                colors[(i * 4) + 2] = map[(2 * count) + i];
                colors[(i * 4) + 3] = ushort.MaxValue;
            }

            return buffer;
        }
        else
        {
            // Packed native-endian Rgba32 pixels for IndexedPixels (the fourth byte is not written to the Rgb24 rows)
            var buffer = scope.Rent(count * 4, AllocationKind.DecoderState, clear: false);
            var colors = buffer.Span;
            var divisor = page.ColorMapDivisor;
            for (var i = 0; i < count; i++)
            {
                colors[i * 4] = (byte)(map[i] / divisor);
                colors[(i * 4) + 1] = (byte)(map[count + i] / divisor);
                colors[(i * 4) + 2] = (byte)(map[(2 * count) + i] / divisor);
                colors[(i * 4) + 3] = byte.MaxValue;
            }

            return buffer;
        }
    }

    private static byte[] CreateGrayLookup(TiffPage page)
    {
        var max = (1 << page.BitsPerSample) - 1;
        var lookup = new byte[max + 1];
        for (var i = 0; i <= max; i++)
        {
            var value = i * 255 / max;
            lookup[i] = (byte)(page.IsWhiteIsZero ? 255 - value : value);
        }

        return lookup;
    }

    /// <summary>Reads the sample of a pixel of a row of 1, 2, 4 or 8-bit samples, the first sample of a byte in its most significant bits.</summary>
    private static int ReadPackedSample(ReadOnlySpan<byte> stored, int x, int bits)
    {
        var position = x * bits;
        return (stored[position >> 3] >> (8 - bits - (position & 7))) & ((1 << bits) - 1);
    }

    private void ExpandPalette(ReadOnlySpan<byte> stored, Span<byte> destination)
    {
        var bits = _page.BitsPerSample;
        if (_page.HasSixteenBitColorMap)
        {
            var colors = unsafe(MemoryMarshal.Cast<byte, ulong>(_palette!.Span));
            var pixels = unsafe(MemoryMarshal.Cast<byte, ulong>(destination));
            for (var x = 0; x < _width; x++)
            {
                pixels[x] = colors[ReadPackedSample(stored, x, bits)];
            }

            return;
        }

        // The color map has an entry for every index
        IndexedPixels.Expand(stored, bits, _width, unsafe(MemoryMarshal.Cast<byte, uint>(_palette!.Span)), destination, rgba: false);
    }

    private void ExpandPackedGray(ReadOnlySpan<byte> stored, Span<byte> destination)
    {
        var bits = _page.BitsPerSample;
        var lookup = _grayLookup!;
        for (var x = 0; x < _width; x++)
        {
            destination[x] = lookup[ReadPackedSample(stored, x, bits)];
        }
    }

    private void ExpandInks(ReadOnlySpan<byte> stored, Span<byte> destination)
    {
        var page = _page;
        var samplesPerPixel = page.SamplesPerPixel;
        if (page.BitsPerSample == 8)
        {
            // Rgb24 without alpha, Rgba32 with the alpha sample copied
            ColorConversionKernels.Convert<byte, IccByteSample>(_inkConversion!, stored[..(_width * samplesPerPixel)], destination, samplesPerPixel, samplesPerPixel - 1);
            return;
        }

        var inks = unsafe(MemoryMarshal.Cast<byte, ushort>(_inks!.Span));
        for (var x = 0; x < _width; x++)
        {
            var offset = x * 5;
            for (var sample = 0; sample < samplesPerPixel; sample++)
            {
                inks[offset + sample] = ReadWord(stored, (x * samplesPerPixel) + sample);
            }

            if (samplesPerPixel == 4)
            {
                inks[offset + 4] = ushort.MaxValue;
            }
        }

        ColorConversionKernels.Convert<ushort, IccUInt16Sample>(_inkConversion!, inks, unsafe(MemoryMarshal.Cast<byte, ushort>(destination)), 5, 4);
    }

    private void ExpandBytes(ReadOnlySpan<byte> stored, Span<byte> destination)
    {
        var width = _width;
        var invert = _page.IsWhiteIsZero;
        switch (_page.SamplesPerPixel)
        {
            case 1:
                stored[..width].CopyTo(destination);
                if (invert)
                {
                    InvertBytes(destination[..width]);
                }

                break;

            case 2:
                for (var x = 0; x < width; x++)
                {
                    var gray = stored[x * 2];
                    if (invert)
                    {
                        gray = (byte)(255 - gray);
                    }

                    var offset = x * 4;
                    destination[offset] = gray;
                    destination[offset + 1] = gray;
                    destination[offset + 2] = gray;
                    destination[offset + 3] = stored[(x * 2) + 1];
                }

                break;

            default:
                // RGB (3) and RGBA (4) samples already match Rgb24 and Rgba32
                stored[..(width * _page.SamplesPerPixel)].CopyTo(destination);
                break;
        }
    }

    private void ExpandWords(ReadOnlySpan<byte> stored, Span<byte> destination)
    {
        var width = _width;
        var invert = _page.IsWhiteIsZero;
        var samples = unsafe(MemoryMarshal.Cast<byte, ushort>(destination));
        switch (_page.SamplesPerPixel)
        {
            case 1:
                ReadWords(stored, samples[..width]);
                if (invert)
                {
                    InvertWords(samples[..width]);
                }

                break;

            case 2:
                for (var x = 0; x < width; x++)
                {
                    var gray = ReadWord(stored, x * 2);
                    if (invert)
                    {
                        gray = (ushort)(ushort.MaxValue - gray);
                    }

                    var offset = x * 4;
                    samples[offset] = gray;
                    samples[offset + 1] = gray;
                    samples[offset + 2] = gray;
                    samples[offset + 3] = ReadWord(stored, (x * 2) + 1);
                }

                break;

            case 3:
                // There is no 16-bit RGB pixel format: the samples are widened to Rgba64 with an opaque alpha channel
                for (var x = 0; x < width; x++)
                {
                    var offset = x * 4;
                    samples[offset] = ReadWord(stored, x * 3);
                    samples[offset + 1] = ReadWord(stored, (x * 3) + 1);
                    samples[offset + 2] = ReadWord(stored, (x * 3) + 2);
                    samples[offset + 3] = ushort.MaxValue;
                }

                break;

            default:
                ReadWords(stored, samples[..(width * 4)]);
                break;
        }
    }

    private void ReadWords(ReadOnlySpan<byte> stored, Span<ushort> destination)
    {
        var source = stored[..(destination.Length * 2)];
        if (_page.IsBigEndian)
        {
            SampleEndianness.ReadBigEndian(source, destination);
        }
        else
        {
            SampleEndianness.ReadLittleEndian(source, destination);
        }
    }

    private ushort ReadWord(ReadOnlySpan<byte> stored, int index)
    {
        var offset = index * 2;
        return _page.IsBigEndian
            ? (ushort)((stored[offset] << 8) | stored[offset + 1])
            : (ushort)((stored[offset + 1] << 8) | stored[offset]);
    }

    private static void InvertBytes(Span<byte> values)
    {
        for (var i = 0; i < values.Length; i++)
        {
            values[i] = (byte)(255 - values[i]);
        }
    }

    private static void InvertWords(Span<ushort> values)
    {
        for (var i = 0; i < values.Length; i++)
        {
            values[i] = (ushort)(ushort.MaxValue - values[i]);
        }
    }
}
