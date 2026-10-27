using Meziantou.Framework.Imaging.Formats;
using Meziantou.Framework.Imaging.TestHarness.Exr;
using Meziantou.Framework.Imaging.Tests;

namespace Meziantou.Framework.Imaging.FuzzTests;

/// <summary>
/// The seeds of the formats the golden corpus has no fixture of (TIFF, ICO and CUR, ANI, OpenEXR): files written by the encoders of
/// the library (every compression, pixel layout and container option they have), and files assembled byte by byte by the
/// builders of the unit tests for what the encoders never write (tiles, the horizontal predictor, packed samples,
/// palette and masked DIB payloads, the sequence and rate tables of an animated cursor).
/// </summary>
internal static class FuzzGeneratedSeeds
{
    public static IReadOnlyList<string> Groups { get; } = ["tiff", "ico", "ani", "exr"];

    public static List<FuzzSeed> Get(string group) => group switch
    {
        "tiff" => GetTiffSeeds(),
        "ico" => GetIconSeeds(),
        "ani" => GetAnimatedCursorSeeds(),
        "exr" => GetOpenExrSeeds(),
        _ => throw new ArgumentOutOfRangeException(nameof(group), group, "The group has no generated seeds."),
    };

    private static List<FuzzSeed> GetTiffSeeds()
    {
        var seeds = new List<FuzzSeed>();
        using (var rgb = CreateRgb24(17, 11, salt: 1))
        {
            seeds.Add(Encode("tiff/rgb24-deflate", rgb, new TiffEncoder()));
            seeds.Add(Encode("tiff/rgb24-lzw-strips", rgb, new TiffEncoder { Compression = TiffCompression.Lzw, RowsPerStrip = 3 }));
            seeds.Add(Encode("tiff/rgb24-jpeg", rgb, new TiffEncoder { Compression = TiffCompression.Jpeg, JpegQuality = 80, JpegChromaSubsampling = JpegChromaSubsampling.Ratio420 }));
            seeds.Add(Encode("tiff/rgb24-indexed-lzw", rgb, new TiffEncoder { Compression = TiffCompression.Lzw, PixelLayout = TiffPixelLayout.Indexed }));
        }

        using (var rgba = CreateRgba32(9, 7, salt: 2))
        {
            seeds.Add(Encode("tiff/rgba32-packbits-bigendian", rgba, new TiffEncoder { Compression = TiffCompression.PackBits, BigEndian = true }));
        }

        using (var gray = CreateGray8(13, 6, salt: 3))
        {
            seeds.Add(Encode("tiff/gray8-none", gray, new TiffEncoder { Compression = TiffCompression.None }));
        }

        using (var deep = CreateRgba64(6, 5, salt: 4))
        {
            seeds.Add(Encode("tiff/rgba64-deflate-bigtiff", deep, new TiffEncoder { BigTiff = true, RowsPerStrip = 2 }));
        }

        using (var gray16 = CreateGray16(7, 4, salt: 5))
        {
            seeds.Add(Encode("tiff/gray16-lzw-bigtiff-bigendian", gray16, new TiffEncoder { Compression = TiffCompression.Lzw, BigTiff = true, BigEndian = true }));
        }

        using (var bilevel = CreateBilevel(37, 9))
        {
            seeds.Add(Encode("tiff/bilevel-ccitt-mh", bilevel, new TiffEncoder { Compression = TiffCompression.CcittModifiedHuffman, PixelLayout = TiffPixelLayout.Bilevel }));
            seeds.Add(Encode("tiff/bilevel-ccitt-g3", bilevel, new TiffEncoder { Compression = TiffCompression.CcittGroup3, PixelLayout = TiffPixelLayout.Bilevel }));
            seeds.Add(Encode("tiff/bilevel-ccitt-g4", bilevel, new TiffEncoder { Compression = TiffCompression.CcittGroup4, PixelLayout = TiffPixelLayout.Bilevel, RowsPerStrip = 4 }));
        }

        // A document whose pages differ in size, pixel format and compression
        using (var collection = ImageCollection.Create(ImageCollectionKind.Pages))
        {
            using var first = CreateRgb24(8, 6, salt: 6);
            using var second = CreateGray8(5, 9, salt: 7);
            using var third = CreateRgba32(4, 4, salt: 8);
            collection.Add(first);
            collection.Add(second);
            collection.Add(third);
            seeds.Add(Save("tiff/pages-deflate", collection, new TiffEncoder { RowsPerStrip = 2 }));
            seeds.Add(Save("tiff/pages-packbits-bigtiff", collection, new TiffEncoder { Compression = TiffCompression.PackBits, BigTiff = true }));
        }

        // Tiles, the horizontal predictor, packed samples and an orientation: TIFF features the encoder never writes
        seeds.Add(new FuzzSeed("tiff/builder-gray8-tiles-lzw", new TiffFileBuilder().AddPage(new TiffPageSpec { Width = 20, Height = 20, TileWidth = 16, TileLength = 16, Compression = 5, Samples = Samples(20 * 20, 251) }).Build()));
        seeds.Add(new FuzzSeed("tiff/builder-rgb24-tiles-deflate-predictor", new TiffFileBuilder().AddPage(new TiffPageSpec { Width = 32, Height = 20, SamplesPerPixel = 3, Photometric = 2, TileWidth = 16, TileLength = 16, Compression = 8, Predictor = 2, Samples = Samples(32 * 20 * 3, 256) }).Build()));
        seeds.Add(new FuzzSeed("tiff/builder-gray16-predictor-deflate", new TiffFileBuilder().AddPage(new TiffPageSpec { Width = 6, Height = 3, BitsPerSample = 16, Compression = 32946, Predictor = 2, Samples = Samples(6 * 3 * 2, 256) }).Build()));
        seeds.Add(new FuzzSeed("tiff/builder-gray4-whiteiszero-packbits", new TiffFileBuilder().AddPage(new TiffPageSpec { Width = 7, Height = 5, BitsPerSample = 4, Photometric = 0, Compression = 32773, Orientation = 6, Samples = Samples(4 * 5, 256) }).Build()));
        seeds.Add(new FuzzSeed("tiff/builder-bilevel-strips-bigendian-gap", new TiffFileBuilder { BigEndian = true, Gap = 6 }
            .AddPage(new TiffPageSpec { Width = 10, Height = 4, BitsPerSample = 1, Photometric = 1, RowsPerStrip = 1, Samples = [0b1010_0000, 0b1100_0000, 0b0000_1111, 0b0100_0000, 0b1111_1111, 0b1100_0000, 0b0101_0101, 0b0000_0000] })
            .AddPage(new TiffPageSpec { Width = 3, Height = 2, SamplesPerPixel = 3, Photometric = 2, Compression = 5, Samples = Samples(3 * 2 * 3, 256) })
            .Build()));
        seeds.Add(new FuzzSeed("tiff/builder-rgba32-bigtiff-unassociated", new TiffFileBuilder { BigTiff = true }.AddPage(new TiffPageSpec { Width = 5, Height = 4, SamplesPerPixel = 4, Photometric = 2, ExtraSamples = [2], Compression = 8, Samples = Samples(5 * 4 * 4, 256) }).Build()));
        return seeds;
    }

    private static List<FuzzSeed> GetIconSeeds()
    {
        var seeds = new List<FuzzSeed>();

        // Representations of several sizes and depths: DIB payloads up to 64 pixels per side, PNG payloads above or for
        // 16-bit pixels
        using (var collection = ImageCollection.Create(ImageCollectionKind.Representations))
        {
            using var small = CreateRgba32(16, 16, salt: 11);
            using var medium = CreateRgb24(32, 32, salt: 12);
            using var large = CreateRgba32(72, 72, salt: 13);
            using var gray = CreateGray8(24, 24, salt: 14);
            collection.Add(small);
            collection.Add(medium);
            collection.Add(large);
            collection.Add(gray);
            seeds.Add(Save("ico/representations-auto", collection, new IcoEncoder()));
            seeds.Add(Save("ico/representations-png", collection, new IcoEncoder { PayloadFormat = IconPayloadFormat.Png }));
        }

        // Two representations of the largest size: the deepest is the default one
        using (var collection = ImageCollection.Create(ImageCollectionKind.Representations))
        {
            using var shallow = CreateRgb24(8, 8, salt: 15);
            using var deep = CreateRgba64(8, 8, salt: 16);
            collection.Add(shallow);
            collection.Add(deep);
            seeds.Add(Save("ico/same-size-rgba64", collection, new IcoEncoder()));
        }

        using (var single = CreateRgba32(5, 3, salt: 17))
        {
            seeds.Add(Encode("ico/single-dib", single, new IcoEncoder { PayloadFormat = IconPayloadFormat.Dib }));
            seeds.Add(Encode("cur/single-png", single, new IcoEncoder { Kind = IconKind.Cursor, PayloadFormat = IconPayloadFormat.Png }));
        }

        // Cursors with a hotspot per representation
        using (var collection = ImageCollection.Create(ImageCollectionKind.Representations))
        {
            using var small = CreateRgba32(12, 12, salt: 18);
            using var large = CreateRgba32(20, 20, salt: 19);
            collection.Add(small, new Point(3, 7));
            collection.Add(large, new Point(19, 0));
            seeds.Add(Save("cur/hotspots-dib", collection, new IcoEncoder { Kind = IconKind.Cursor, PayloadFormat = IconPayloadFormat.Dib }));
            seeds.Add(Save("cur/hotspots-auto", collection, new IcoEncoder { Kind = IconKind.Cursor }));
        }

        // DIB payloads the encoder never writes: 24 bits with an AND mask, 8-bit palette, 4- and 1-bit palettes, 32 bits
        // with a mask and without one
        var palette = new[] { new Rgb24(0, 0, 0), new Rgb24(255, 255, 255), new Rgb24(200, 30, 40), new Rgb24(10, 160, 90) };
        var mask = Enumerable.Range(0, 6 * 5).Select(index => index % 7 == 0).ToArray();
        seeds.Add(new FuzzSeed("ico/builder-dib24-mask", new IcoFileBuilder().Add(new IcoEntrySpec { Width = 6, Height = 5, BitCountOrHotspotY = 24, Payload = IcoFileBuilder.Dib24(6, 5, [.. Enumerable.Range(0, 30).Select(i => new Rgb24((byte)(i * 8), (byte)(255 - (i * 5)), (byte)(i * 3)))], mask) }).Build()));
        seeds.Add(new FuzzSeed("ico/builder-dib8-palette", new IcoFileBuilder().Add(new IcoEntrySpec { Width = 6, Height = 5, BitCountOrHotspotY = 8, Payload = IcoFileBuilder.Dib8(6, 5, [.. Enumerable.Range(0, 30).Select(i => (byte)(i % palette.Length))], palette, mask) }).Build()));
        seeds.Add(new FuzzSeed("ico/builder-dib4-and-dib1", new IcoFileBuilder()
            .Add(new IcoEntrySpec { Width = 6, Height = 5, ColorCount = 4, BitCountOrHotspotY = 4, Payload = IcoFileBuilder.Dib(6, 5, 4, PaletteTable(palette), PackedRows(6, 5, 4), mask, includeMask: true) })
            .Add(new IcoEntrySpec { Width = 9, Height = 4, ColorCount = 2, BitCountOrHotspotY = 1, Payload = IcoFileBuilder.Dib(9, 4, 1, PaletteTable(palette[..2]), PackedRows(9, 4, 1), mask: null, includeMask: true) })
            .Build()));
        seeds.Add(new FuzzSeed("cur/builder-dib32-mask-and-unmasked", new IcoFileBuilder { Type = 2 }
            .Add(new IcoEntrySpec { Width = 4, Height = 4, PlanesOrHotspotX = 1, BitCountOrHotspotY = 2, Payload = IcoFileBuilder.Dib32(4, 4, [.. Enumerable.Range(0, 16).Select(i => new Rgba32((byte)(i * 16), 7, 9, (byte)(i * 17)))], [.. Enumerable.Range(0, 16).Select(i => i % 3 == 0)]) })
            .Add(new IcoEntrySpec { Width = 3, Height = 2, PlanesOrHotspotX = 2, BitCountOrHotspotY = 1, Payload = IcoFileBuilder.Dib32(3, 2, [.. Enumerable.Range(0, 6).Select(i => new Rgba32(1, (byte)(i * 40), 3))], includeMask: false) })
            .Build()));
        return seeds;
    }

    private static List<FuzzSeed> GetAnimatedCursorSeeds()
    {
        var seeds = new List<FuzzSeed>();
        using (var animation = CreateAnimation(6, 5, [6, 3, 6], hotspots: [new Point(1, 1), null, new Point(5, 4)]))
        {
            seeds.Add(Encode("ani/three-steps-auto", animation, new AniEncoder()));
            seeds.Add(Encode("ani/three-steps-png", animation, new AniEncoder { PayloadFormat = IconPayloadFormat.Png }));
        }

        // A repeated drawing is stored once and shown through the sequence table
        using (var animation = CreateAnimation(4, 4, [2, 2, 5, 2], hotspots: [null, null, null, null], repeatFirst: true))
        {
            seeds.Add(Encode("ani/repeated-frame-dib", animation, new AniEncoder { PayloadFormat = IconPayloadFormat.Dib }));
        }

        using (var still = CreateRgba32(7, 3, salt: 21))
        {
            seeds.Add(Encode("ani/still", still, new AniEncoder()));
        }

        using (var deep = CreateRgba64(5, 5, salt: 22))
        {
            seeds.Add(Encode("ani/rgba64-png", deep, new AniEncoder()));
        }

        // Tables and chunks the encoder never writes: explicit rates and sequence, the tables after the frames, metadata,
        // icon frames instead of cursor frames
        var first = Cursor(CreateRgba32(3, 3, salt: 23), new Point(1, 2));
        var second = Cursor(CreateRgba32(3, 3, salt: 24), new Point(0, 0));
        seeds.Add(new FuzzSeed("ani/builder-sequence-rates", new AniFileBuilder { DisplayRate = 9, Sequence = [1, 0, 0, 1], Rates = [1, 2, 3, 4], Title = "Fuzz", Author = "Seed" }.AddFrame(first).AddFrame(second).Build()));
        seeds.Add(new FuzzSeed("ani/builder-tables-after-frames", new AniFileBuilder { TablesAfterFrames = true, Sequence = [1, 0], Rates = [3, 5], Flags = 1, LeadingChunks = AniFileBuilder.Chunk("JUNK", [1, 2, 3]) }.AddFrame(first).AddFrame(second).Build()));
        var icon = new IcoFileBuilder().Add(new IcoEntrySpec { Width = 3, Height = 2, Payload = IcoFileBuilder.Dib32(3, 2, [.. Enumerable.Range(0, 6).Select(i => new Rgba32((byte)(i * 30), 1, 2, 255))]) }).Build();
        seeds.Add(new FuzzSeed("ani/builder-icon-frames", new AniFileBuilder { DisplayRate = 1, Rates = [0, 7] }.AddFrame(icon).AddFrame(icon).Build()));
        return seeds;
    }

    private static List<FuzzSeed> GetOpenExrSeeds()
    {
        var seeds = new List<FuzzSeed>();
        using (var rgba = CreateRgba32(19, 37, salt: 40))
        using (var half = rgba.CloneAs<RgbaHalf>())
        using (var full = rgba.CloneAs<RgbaFloat>())
        {
            half.Metadata.TransferFunction = Metadata.ColorTransferFunction.Linear;
            full.Metadata.TransferFunction = Metadata.ColorTransferFunction.Linear;
            full.Frames[0][3, 4] = new RgbaFloat(12.5f, -3f, float.PositiveInfinity, 1f);
            foreach (var compression in Enum.GetValues<OpenExrCompression>())
            {
                seeds.Add(Encode("exr/half-" + compression.ToString().ToLowerInvariant(), half, new OpenExrEncoder { Compression = compression }));
                seeds.Add(Encode("exr/float-" + compression.ToString().ToLowerInvariant(), full, new OpenExrEncoder { Compression = compression }));
            }
        }

        using (var gray = CreateGray8(11, 40, salt: 41))
        {
            seeds.Add(Encode("exr/luminance-piz", gray, new OpenExrEncoder { Compression = OpenExrCompression.Piz, MetadataHandling = MetadataHandling.DiscardUnsupported }));
        }

        // Tiles and their levels, windows, sample types and line orders the encoder never writes
        static byte[] Halves(int count, int salt) => ExrFileBuilder.Halves([.. Enumerable.Range(0, count).Select(i => ((i * 7) + salt) % 13 * 0.5f)]);
        var tiled = new ExrFileBuilder { VersionField = 2 | 0x200 };
        foreach (var attribute in ExrFileBuilder.ScanLines(3, 3, 0, ExrChannelDescription.Half("G"), ExrChannelDescription.Half("R")).Attributes)
        {
            tiled.Attributes.Add(attribute);
        }

        tiled.Set("tiles", "tiledesc", ExrFileBuilder.TileDescription(2, 2, levelMode: 1, roundingMode: 0));
        tiled.AddTile(0, 0, 0, 0, Halves(8, 1)).AddTile(1, 0, 0, 0, Halves(4, 2)).AddTile(0, 1, 0, 0, Halves(4, 3)).AddTile(1, 1, 0, 0, Halves(2, 4)).AddTile(0, 0, 1, 1, Halves(2, 5));
        tiled.FileOrder = [4, 2, 0, 3, 1];
        seeds.Add(new FuzzSeed("exr/builder-mipmap-tiles", tiled.Build()));

        var windows = ExrFileBuilder.ScanLines(2, 3, 0, ExrChannelDescription.Half("A"), ExrChannelDescription.FloatingPoint("B"), ExrChannelDescription.UnsignedInt("G"), ExrChannelDescription.Half("R"))
            .Set("displayWindow", "box2i", ExrFileBuilder.Box(-2, -1, 3, 4))
            .Set("lineOrder", "lineOrder", [1])
            .Set("chromaticities", "chromaticities", ExrFileBuilder.Floats(0.708f, 0.292f, 0.170f, 0.797f, 0.131f, 0.046f, 0.3127f, 0.3290f));
        for (var y = 2; y >= 0; y--)
        {
            windows.AddScanLineChunk(y, [.. Halves(2, y), .. ExrFileBuilder.Floats(y, -y), .. ExrFileBuilder.UInts((uint)y, 7), .. Halves(2, y + 3)]);
        }

        windows.FileOrder = [2, 1, 0];
        seeds.Add(new FuzzSeed("exr/builder-windows-mixed-types", windows.Build()));
        return seeds;
    }

    private static Image<Rgba32> CreateAnimation(int width, int height, int[] jiffies, Point?[] hotspots, bool repeatFirst = false)
    {
        var image = CreateRgba32(width, height, salt: 30);
        try
        {
            image.Frames[0].Metadata.Duration = new FrameDuration(jiffies[0], 60);
            image.Frames[0].Metadata.Hotspot = hotspots[0];
            for (var i = 1; i < jiffies.Length; i++)
            {
                using var source = repeatFirst && i % 2 == 0 ? image.CloneFrame(0) : CreateRgba32(width, height, salt: 30 + i);
                var frame = image.AppendFrame(source.Frames[0]);
                frame.Metadata.Duration = new FrameDuration(jiffies[i], 60);
                frame.Metadata.Hotspot = hotspots[i];
            }

            return image;
        }
        catch
        {
            image.Dispose();
            throw;
        }
    }

    private static byte[] Cursor(Image image, Point hotspot)
    {
        using (image)
        {
            image.Frames[0].Metadata.Hotspot = hotspot;
            using var stream = new MemoryStream();
            image.Save(stream, new IcoEncoder { Kind = IconKind.Cursor, PayloadFormat = IconPayloadFormat.Dib });
            return stream.ToArray();
        }
    }

    private static FuzzSeed Encode(string id, Image image, ImageEncoder encoder)
    {
        using var stream = new MemoryStream();
        image.Save(stream, encoder);
        return new FuzzSeed(id, stream.ToArray());
    }

    private static FuzzSeed Save(string id, ImageCollection collection, ImageEncoder encoder)
    {
        using var stream = new MemoryStream();
        collection.Save(stream, encoder);
        return new FuzzSeed(id, stream.ToArray());
    }

    private static byte[] Samples(int length, int modulo)
    {
        var samples = new byte[length];
        for (var i = 0; i < samples.Length; i++)
        {
            samples[i] = (byte)(i * 7 % modulo);
        }

        return samples;
    }

    private static byte[] PaletteTable(Rgb24[] palette)
    {
        var table = new byte[palette.Length * 4];
        for (var i = 0; i < palette.Length; i++)
        {
            table[i * 4] = palette[i].B;
            table[(i * 4) + 1] = palette[i].G;
            table[(i * 4) + 2] = palette[i].R;
        }

        return table;
    }

    // Rows of packed palette indexes, padded to four bytes, whose indexes cycle through the palette
    private static byte[][] PackedRows(int width, int height, int bitCount)
    {
        var rowLength = (((width * bitCount) + 31) / 32) * 4;
        var colors = 1 << bitCount;
        var rows = new byte[height][];
        for (var y = 0; y < height; y++)
        {
            var row = new byte[rowLength];
            for (var x = 0; x < width; x++)
            {
                var index = ((x + y) % Math.Min(colors, 4)) & (colors - 1);
                var bit = x * bitCount;
                row[bit / 8] |= (byte)(index << (8 - bitCount - (bit % 8)));
            }

            rows[y] = row;
        }

        return rows;
    }

    private static Image<Rgb24> CreateRgb24(int width, int height, int salt)
        => Create(width, height, (x, y) => new Rgb24((byte)((x * 37) + salt), (byte)((y * 59) ^ salt), (byte)((x * y) + (salt * 13))));

    private static Image<Rgba32> CreateRgba32(int width, int height, int salt)
        => Create(width, height, (x, y) => new Rgba32((byte)((x * 41) + salt), (byte)((y * 23) ^ salt), (byte)(x + y + salt), (byte)((x + y) % 4 is 0 ? 255 : (x * 70) + (y * 9))));

    private static Image<Rgba64> CreateRgba64(int width, int height, int salt)
        => Create(width, height, (x, y) => new Rgba64((ushort)((x * 4099) + salt), (ushort)((y * 3001) ^ salt), (ushort)((x * y * 257) + salt), (ushort)(x == y ? 65535 : (x * 9000) + y)));

    private static Image<Gray8> CreateGray8(int width, int height, int salt)
        => Create(width, height, (x, y) => new Gray8((byte)((x * 19) + (y * 7) + salt)));

    private static Image<Gray16> CreateGray16(int width, int height, int salt)
        => Create(width, height, (x, y) => new Gray16((ushort)((x * 7919) + (y * 104) + salt)));

    private static Image<Gray8> CreateBilevel(int width, int height)
        => Create(width, height, (x, y) => new Gray8(((x / 3) + y) % 3 is 0 || x == y ? (byte)0 : (byte)255));

    private static Image<TPixel> Create<TPixel>(int width, int height, Func<int, int, TPixel> pixel)
        where TPixel : unmanaged
    {
        var pixels = new TPixel[width * height];
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                pixels[(y * width) + x] = pixel(x, y);
            }
        }

        return Image.ImportPixelData<TPixel>(pixels, width, height);
    }
}
