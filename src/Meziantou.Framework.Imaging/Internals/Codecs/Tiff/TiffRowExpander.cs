using System.Buffers.Binary;
using System.Runtime.InteropServices;

namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// Expands the decoded rows of one TIFF page to its lossless working representation
/// (<see cref="TiffPage.SourcePixelFormat"/>): the samples of 1, 2 and 4 bits are unpacked, the grayscale ones scaled
/// exactly to 8 bits, <c>WhiteIsZero</c> samples inverted, palette indexes replaced by their colors, 16-bit samples read
/// in the byte order of the file, CMYK inks converted to RGB, and floating-point samples widened to four components.
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
/// <para>
/// Floating-point samples keep their bits: a half-precision sample is a component of <see cref="PixelFormat.RgbaHalf"/>,
/// a 32-bit one a component of <see cref="PixelFormat.RgbaFloat"/>, and a 24-bit one (1 sign bit, 7 exponent bits with a
/// bias of 63, 16 fraction bits) the single-precision number of the same value, which always exists. Not-a-number values
/// and infinities are kept too.
/// </para>
/// </remarks>
internal sealed class TiffRowExpander : IDisposable
{
    private readonly TiffPage _page;
    private readonly int _width;
    private readonly IccPipeline? _inkConversion;
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
        else if (page.IsFloatingPoint)
        {
            ExpandFloats(stored, destination);
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
        => PackedGraySamples.Expand(stored, _page.BitsPerSample, _width, _page.IsWhiteIsZero, destination);

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

    /// <summary>
    /// Expands grayscale or RGB floating-point samples, with or without alpha, to four components: a gray sample is repeated
    /// in the three colors, and a pixel without alpha sample is opaque.
    /// </summary>
    private void ExpandFloats(ReadOnlySpan<byte> stored, Span<byte> destination)
    {
        var page = _page;
        var samplesPerPixel = page.SamplesPerPixel;
        var colors = page.IsGrayscale ? 1 : 3;
        var hasAlpha = page.HasAlpha;
        var bigEndian = page.IsBigEndian;
        var bytesPerSample = page.BitsPerSample / 8;
        var pixelLength = samplesPerPixel * bytesPerSample;
        if (bytesPerSample == 2)
        {
            var halves = unsafe(MemoryMarshal.Cast<byte, ushort>(destination));
            for (var x = 0; x < _width; x++)
            {
                var source = stored.Slice(x * pixelLength, pixelLength);
                var target = halves.Slice(x * 4, 4);
                for (var c = 0; c < 3; c++)
                {
                    target[c] = ReadUInt16(source[(colors == 1 ? 0 : c * 2)..], bigEndian);
                }

                target[3] = hasAlpha ? ReadUInt16(source[(colors * 2)..], bigEndian) : BitConverter.HalfToUInt16Bits(Half.One);
            }

            return;
        }

        var singles = unsafe(MemoryMarshal.Cast<byte, uint>(destination));
        for (var x = 0; x < _width; x++)
        {
            var source = stored.Slice(x * pixelLength, pixelLength);
            var target = singles.Slice(x * 4, 4);
            for (var c = 0; c < 3; c++)
            {
                target[c] = ReadSingleBits(source[(colors == 1 ? 0 : c * bytesPerSample)..], bytesPerSample, bigEndian);
            }

            target[3] = hasAlpha ? ReadSingleBits(source[(colors * bytesPerSample)..], bytesPerSample, bigEndian) : BitConverter.SingleToUInt32Bits(1f);
        }
    }

    private static ushort ReadUInt16(ReadOnlySpan<byte> bytes, bool bigEndian)
        => bigEndian ? BinaryPrimitives.ReadUInt16BigEndian(bytes) : BinaryPrimitives.ReadUInt16LittleEndian(bytes);

    /// <summary>Reads a 24- or 32-bit floating-point sample as the bits of the single-precision number of the same value.</summary>
    private static uint ReadSingleBits(ReadOnlySpan<byte> bytes, int length, bool bigEndian)
    {
        if (length == 4)
            return bigEndian ? BinaryPrimitives.ReadUInt32BigEndian(bytes) : BinaryPrimitives.ReadUInt32LittleEndian(bytes);

        var value = bigEndian
            ? ((uint)bytes[0] << 16) | ((uint)bytes[1] << 8) | bytes[2]
            : ((uint)bytes[2] << 16) | ((uint)bytes[1] << 8) | bytes[0];
        return Float24ToSingleBits(value);
    }

    /// <summary>
    /// Converts a 24-bit floating-point number (1 sign bit, 7 exponent bits with a bias of 63, 16 fraction bits, with the
    /// subnormal numbers, infinities and not-a-number values of IEEE 754) to the bits of the single-precision number of the
    /// same value: its exponent range and its precision are within those of a single.
    /// </summary>
    /// <param name="value">The 24 bits of the number, in the low bits.</param>
    /// <returns>The bits of the single-precision number.</returns>
    internal static uint Float24ToSingleBits(uint value)
    {
        var sign = ((value >> 23) & 1) << 31;
        var exponent = (value >> 16) & 0x7F;
        var fraction = value & 0xFFFF;

        // Infinities and not-a-number values, whose payload is kept
        if (exponent == 0x7F)
            return sign | 0x7F80_0000 | (fraction << 7);

        // A normal number: the exponent is rebiased from 63 to 127
        if (exponent != 0)
            return sign | ((exponent + 64) << 23) | (fraction << 7);

        // Zero and the subnormal numbers, fraction * 2^(1 - 63 - 16): a normal single, computed exactly
        return sign | BitConverter.SingleToUInt32Bits(MathF.ScaleB(fraction, -78));
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

            case 2 when !invert:
                SixteenBitSamples.ExpandGrayAlpha(stored, width, destination, _page.IsBigEndian);
                break;

            case 2:
                for (var x = 0; x < width; x++)
                {
                    var gray = (ushort)(ushort.MaxValue - ReadWord(stored, x * 2));
                    var offset = x * 4;
                    samples[offset] = gray;
                    samples[offset + 1] = gray;
                    samples[offset + 2] = gray;
                    samples[offset + 3] = ReadWord(stored, (x * 2) + 1);
                }

                break;

            case 3:
                // There is no 16-bit RGB pixel format: the samples are widened to Rgba64 with an opaque alpha channel
                SixteenBitSamples.ExpandRgbOpaque(stored, width, destination, _page.IsBigEndian);
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
