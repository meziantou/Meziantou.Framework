using System.Buffers.Binary;

namespace Meziantou.Framework.Imaging.Tests.Codecs;

/// <summary>
/// A PNG test image defined by its encoded samples, with an independent test-side encoder (forward filters, Adam7 pass
/// extraction, sample packing; zlib by the BCL compressor) and the expected decoded pixels computed directly from the PNG
/// specification (sample scaling, palette lookup, tRNS keys). Nothing here uses the library decoder.
/// </summary>
internal sealed class PngTestImage
{
    private static readonly (int X0, int Y0, int Dx, int Dy)[] Adam7Passes = [(0, 0, 8, 8), (4, 0, 8, 8), (0, 4, 4, 8), (2, 0, 4, 4), (0, 2, 2, 4), (1, 0, 2, 2), (0, 1, 1, 2)];

    private PngTestImage(int width, int height, byte colorType, byte bitDepth, ushort[] samples, byte[]? palette, byte[]? transparency)
    {
        Width = width;
        Height = height;
        ColorType = colorType;
        BitDepth = bitDepth;
        Samples = samples;
        Palette = palette;
        Transparency = transparency;
    }

    public int Width { get; }

    public int Height { get; }

    public byte ColorType { get; }

    public byte BitDepth { get; }

    /// <summary>Gets the encoded samples, row-major, <see cref="Channels"/> per pixel (palette: indices).</summary>
    public ushort[] Samples { get; }

    public byte[]? Palette { get; }

    public byte[]? Transparency { get; }

    public int Channels => GetChannels(ColorType);

    public static int GetChannels(byte colorType) => colorType switch { 0 or 3 => 1, 2 => 3, 4 => 2, _ => 4 };

    /// <summary>Creates seeded random samples for a legal color type/bit depth; optionally with a tRNS chunk (gray key, RGB key, palette alphas).</summary>
    [SuppressMessage("Security", "CA5394:Do not use insecure randomness", Justification = "Deterministic seeded test inputs.")]
    public static PngTestImage CreateRandom(int width, int height, byte colorType, byte bitDepth, bool transparency, int seed)
    {
        var random = new Random(seed);
        var channels = GetChannels(colorType);
        var samples = new ushort[width * height * channels];
        byte[]? palette = null;
        byte[]? trns = null;
        if (colorType == 3)
        {
            // Fewer entries than the bit depth allows, so the decoder cannot rely on a full table
            var entries = bitDepth switch { 1 => 2, 2 => 3, 4 => 11, _ => 200 };
            palette = new byte[entries * 3];
            random.NextBytes(palette);
            for (var i = 0; i < samples.Length; i++)
            {
                samples[i] = (ushort)random.Next(entries);
            }

            if (transparency)
            {
                trns = new byte[Math.Min(entries - 1, 5)];
                random.NextBytes(trns);
                trns[0] = 0;
            }
        }
        else
        {
            var max = (1 << bitDepth) - 1;
            for (var i = 0; i < samples.Length; i++)
            {
                samples[i] = (ushort)random.Next(max + 1);
            }

            if (transparency)
            {
                // The key is the first pixel, so at least one pixel is transparent
                trns = new byte[channels * 2];
                for (var c = 0; c < channels; c++)
                {
                    BinaryPrimitives.WriteUInt16BigEndian(trns.AsSpan(c * 2), samples[c]);
                }
            }
        }

        return new PngTestImage(width, height, colorType, bitDepth, samples, palette, trns);
    }

    public static PngTestImage Create(int width, int height, byte colorType, byte bitDepth, ushort[] samples, byte[]? palette = null, byte[]? transparency = null)
        => new(width, height, colorType, bitDepth, samples, palette, transparency);

    /// <summary>Gets the expected decoded pixel at full 16-bit precision (8-bit and lower samples scaled to 8 bits, then x257).</summary>
    public Rgba64 GetExpectedPixel(int x, int y)
    {
        var offset = ((y * Width) + x) * Channels;
        ushort Scale(int sample) => BitDepth == 16 ? (ushort)sample : (ushort)(sample * (255 / ((1 << BitDepth) - 1)) * 257);
        switch (ColorType)
        {
            case 0:
            {
                var gray = Scale(Samples[offset]);
                var transparent = Transparency is not null && Samples[offset] == BinaryPrimitives.ReadUInt16BigEndian(Transparency);
                return new Rgba64(gray, gray, gray, transparent ? (ushort)0 : ushort.MaxValue);
            }

            case 2:
            {
                var transparent = Transparency is not null
                    && Samples[offset] == BinaryPrimitives.ReadUInt16BigEndian(Transparency)
                    && Samples[offset + 1] == BinaryPrimitives.ReadUInt16BigEndian(Transparency.AsSpan(2))
                    && Samples[offset + 2] == BinaryPrimitives.ReadUInt16BigEndian(Transparency.AsSpan(4));
                return new Rgba64(Scale(Samples[offset]), Scale(Samples[offset + 1]), Scale(Samples[offset + 2]), transparent ? (ushort)0 : ushort.MaxValue);
            }

            case 3:
            {
                var index = Samples[offset];
                var alpha = Transparency is not null && index < Transparency.Length ? Transparency[index] : (byte)255;
                return new Rgba64((ushort)(Palette![index * 3] * 257), (ushort)(Palette[(index * 3) + 1] * 257), (ushort)(Palette[(index * 3) + 2] * 257), (ushort)(alpha * 257));
            }

            case 4:
            {
                var gray = Scale(Samples[offset]);
                return new Rgba64(gray, gray, gray, Scale(Samples[offset + 1]));
            }

            default:
                return new Rgba64(Scale(Samples[offset]), Scale(Samples[offset + 1]), Scale(Samples[offset + 2]), Scale(Samples[offset + 3]));
        }
    }

    public Rgba64[] GetExpectedPixels()
    {
        var result = new Rgba64[Width * Height];
        for (var y = 0; y < Height; y++)
        {
            for (var x = 0; x < Width; x++)
            {
                result[(y * Width) + x] = GetExpectedPixel(x, y);
            }
        }

        return result;
    }

    /// <summary>Gets the uncompressed, filtered image data (scanlines with their filter byte); scanline k uses filter <c>(k + filterSeed) mod 5</c>.</summary>
    public byte[] GetFilteredData(bool interlaced, int filterSeed = 0)
    {
        var output = new List<byte>();
        var bytesPerPixel = Math.Max(1, Channels * BitDepth / 8);
        var scanline = 0;
        foreach (var (x0, y0, dx, dy) in interlaced ? Adam7Passes : [(0, 0, 1, 1)])
        {
            var columns = Enumerable.Range(0, Width).Where(x => x >= x0 && (x - x0) % dx == 0).ToArray();
            var rows = Enumerable.Range(0, Height).Where(y => y >= y0 && (y - y0) % dy == 0).ToArray();
            if (columns.Length == 0 || rows.Length == 0)
                continue; // empty pass: no scanline, no filter byte

            byte[]? previous = null;
            foreach (var y in rows)
            {
                var raw = Pack(columns.SelectMany(x => Enumerable.Range(0, Channels).Select(c => Samples[(((y * Width) + x) * Channels) + c])).ToArray());
                var filter = (byte)((scanline++ + filterSeed) % 5);
                output.Add(filter);
                for (var i = 0; i < raw.Length; i++)
                {
                    var a = i >= bytesPerPixel ? raw[i - bytesPerPixel] : 0;
                    var b = previous?[i] ?? 0;
                    var c = i >= bytesPerPixel && previous is not null ? previous[i - bytesPerPixel] : 0;
                    var predictor = filter switch
                    {
                        0 => 0,
                        1 => a,
                        2 => b,
                        3 => (a + b) / 2,
                        _ => Paeth(a, b, c),
                    };
                    output.Add((byte)(raw[i] - predictor));
                }

                previous = raw;
            }
        }

        return [.. output];
    }

    /// <summary>Encodes the image: IHDR, PLTE/tRNS, then the zlib datastream split into IDAT chunks of <paramref name="idatChunkSize"/> bytes.</summary>
    public byte[] Encode(bool interlaced, int filterSeed = 0, int idatChunkSize = int.MaxValue, byte[]? zlib = null, Action<SyntheticImages.PngBuilder>? afterImageData = null)
    {
        var builder = new SyntheticImages.PngBuilder().Header(Width, Height, BitDepth, ColorType, interlaced ? (byte)1 : (byte)0);
        if (Palette is not null)
        {
            builder.Chunk("PLTE", Palette);
        }

        if (Transparency is not null)
        {
            builder.Chunk("tRNS", Transparency);
        }

        var stream = zlib ?? SyntheticImages.Zlib(GetFilteredData(interlaced, filterSeed));
        var offset = 0;
        do
        {
            // At least one (possibly empty) IDAT chunk
            var length = Math.Min(idatChunkSize, stream.Length - offset);
            builder.Chunk("IDAT", stream.AsSpan(offset, length));
            offset += length;
        }
        while (offset < stream.Length);

        afterImageData?.Invoke(builder);
        return builder.End().ToArray();
    }

    private static int Paeth(int a, int b, int c)
    {
        var p = a + b - c;
        var pa = Math.Abs(p - a);
        var pb = Math.Abs(p - b);
        var pc = Math.Abs(p - c);
        if (pa <= pb && pa <= pc)
            return a;

        return pb <= pc ? b : c;
    }

    private byte[] Pack(ushort[] samples)
    {
        if (BitDepth == 16)
            return [.. samples.SelectMany(sample => new[] { (byte)(sample >> 8), (byte)sample })];

        if (BitDepth == 8)
            return [.. samples.Select(sample => (byte)sample)];

        var perByte = 8 / BitDepth;
        var result = new byte[(samples.Length + perByte - 1) / perByte];
        for (var i = 0; i < samples.Length; i++)
        {
            result[i / perByte] |= (byte)(samples[i] << (8 - (BitDepth * ((i % perByte) + 1))));
        }

        return result;
    }
}
