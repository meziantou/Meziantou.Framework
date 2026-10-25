using System.Buffers.Binary;
using System.Globalization;
using Meziantou.Framework.Imaging;
using Meziantou.Framework.Imaging.Formats;
using Meziantou.Framework.Pdf.Filters;
using Meziantou.Framework.Pdf.Objects;
using Meziantou.Framework.Pdf.Parsing;
using Meziantou.Framework.Pdf.TestHarness;
using Meziantou.Framework.Pdf.TestHarness.Fixtures;

namespace Meziantou.Framework.Pdf.FuzzTests;

/// <summary>Mutated fixtures and mutated filter data only throw documented exceptions, in bounded time.</summary>
public sealed class PdfMutationFuzzTests
{
    private const int BitmapWidth = 61;
    private const int BitmapHeight = 23;

    private static readonly PdfName RowsName = PdfName.Create("Rows");
    private static readonly PdfName EncodedByteAlignName = PdfName.Create("EncodedByteAlign");
    private static readonly PdfName EndOfBlockName = PdfName.Create("EndOfBlock");
    private static readonly PdfName Jbig2GlobalsName = PdfName.Create("JBIG2Globals");

    public static TheoryData<string> Seeds => new(PdfFixtureManifest.Load().Fixtures.Where(f => f.Kind is "valid" or "damaged" or "encrypted").Select(f => f.Path));

    public static TheoryData<string> Filters => new(["FlateDecode", "LZWDecode", "ASCIIHexDecode", "ASCII85Decode", "RunLengthDecode", "DCTDecode", "JPXDecode", "CCITTFaxDecode", "JBIG2Decode"]);

    [Theory]
    [MemberData(nameof(Seeds))]
    public async Task MutatedFixtures(string path)
    {
        // An encrypted fixture is read without a password too: only the documents without a user password open then
        var password = PdfFixtureManifest.Load().Fixtures.Single(f => f.Path == path).Expected?.Password;
        string?[] passwords = password is null ? [null] : [null, password];
        var seed = File.ReadAllBytes(RepositoryPaths.Fixtures / path);
        var random = PdfFuzzRandom.Create(PdfFuzzSettings.Seed, path);
        foreach (var candidate in passwords)
        {
            await PdfFuzzOracle.RunAsync(seed, path, candidate);
        }

        for (var i = 0; i < PdfFuzzSettings.Iterations; i++)
        {
            var mutated = PdfFuzzMutator.Mutate(seed, random);
            var description = string.Create(CultureInfo.InvariantCulture, $"{path} #{i}");

            // Rendering costs more than reading: a deterministic quarter of the inputs are rendered
            var render = random.Next(4) == 0;
            foreach (var candidate in passwords)
            {
                await PdfFuzzOracle.RunAsync(mutated, description, candidate, render);
            }
        }
    }

    [Theory]
    [MemberData(nameof(Filters))]
    public void MutatedFilterData(string filter)
    {
        var random = PdfFuzzRandom.Create(PdfFuzzSettings.Seed, filter);
        var seeds = GetFilterSeeds(filter);
        if (filter is "CCITTFaxDecode" or "JBIG2Decode")
        {
            // The seeds are valid: the mutations start from data that decodes
            foreach (var (encoded, parameters) in seeds)
            {
                Assert.NotEmpty(PdfFilterPipeline.Decode(encoded, [PdfName.Create(filter)], [parameters], new PdfDecodeLimits(1 << 20), stopBeforeImageFilters: false, out _));
            }
        }

        for (var i = 0; i < PdfFuzzSettings.Iterations * 10; i++)
        {
            var (encoded, seedParameters) = seeds[random.Next(seeds.Length)];
            var data = PdfFuzzMutator.Mutate(encoded, random);
            var parameters = filter switch
            {
                "CCITTFaxDecode" => CreateCcittParameters(seedParameters, random),
                "JBIG2Decode" => CreateJbig2Parameters(seedParameters, random),
                _ => random.Next(3) == 0 ? new PdfDictionary
                {
                    [PdfNames.Predictor] = PdfInteger.Get(random.Next(2) == 0 ? 2 : 10 + random.Next(6)),
                    [PdfNames.Columns] = PdfInteger.Get(random.Next(-1, 100)),
                    [PdfNames.Colors] = PdfInteger.Get(random.Next(-1, 5)),
                    [PdfNames.BitsPerComponent] = PdfInteger.Get(new[] { 1, 2, 4, 8, 16, 3 }[random.Next(6)]),
                } : null,
            };
            try
            {
                PdfFilterPipeline.Decode(data, [PdfName.Create(filter)], [parameters], new PdfDecodeLimits(1 << 20), stopBeforeImageFilters: false, out _);
            }
            catch (Exception ex) when (ex is InvalidPdfContentException or UnsupportedPdfFeatureException or PdfResourceLimitException)
            {
            }
        }
    }

    /// <summary>Gets the valid encoded data the mutations start from, with the parameters that decode it.</summary>
    private static (byte[] Data, PdfDictionary? Parameters)[] GetFilterSeeds(string filter)
    {
        switch (filter)
        {
            case "CCITTFaxDecode":
                // Modified Huffman (each row on a byte), Group 3 one-dimensional and Group 4
                return
                [
                    (EncodeCcitt(TiffCompression.CcittModifiedHuffman), new PdfDictionary { [PdfNames.K] = PdfInteger.Get(0), [PdfNames.Columns] = PdfInteger.Get(BitmapWidth), [RowsName] = PdfInteger.Get(BitmapHeight), [EncodedByteAlignName] = PdfBoolean.True }),
                    (EncodeCcitt(TiffCompression.CcittGroup3), new PdfDictionary { [PdfNames.K] = PdfInteger.Get(0), [PdfNames.Columns] = PdfInteger.Get(BitmapWidth) }),
                    (EncodeCcitt(TiffCompression.CcittGroup4), new PdfDictionary { [PdfNames.K] = PdfInteger.Get(-1), [PdfNames.Columns] = PdfInteger.Get(BitmapWidth), [RowsName] = PdfInteger.Get(BitmapHeight), [PdfNames.BlackIs1] = PdfBoolean.True }),
                ];

            case "JBIG2Decode":
                // A generic region coded with MMR (the Group 4 code of T.6) on a page: the page in the stream, a striped
                // page that a second region is combined with, or the page in the global segments
                var mmr = EncodeCcitt(TiffCompression.CcittGroup4);
                byte[] region = [.. Jbig2RegionInfo(BitmapWidth, BitmapHeight, 0, 0, 0), 1, .. mmr];
                var page = Jbig2Segment(0, 48, Jbig2PageInformation(BitmapWidth, BitmapHeight, flags: 0));
                var stripedPage = Jbig2Segment(0, 48, Jbig2PageInformation(BitmapWidth, uint.MaxValue, flags: 0x40));
                var globals = new PdfStream();
                globals.SetData(page);
                return
                [
                    ([.. page, .. Jbig2Segment(1, 38, region), .. Jbig2Segment(2, 49, [])], null),
                    ([.. stripedPage, .. Jbig2Segment(1, 38, region), .. Jbig2Segment(2, 38, [.. Jbig2RegionInfo(BitmapWidth, BitmapHeight, 8, 2, 2), 1, .. mmr]), .. Jbig2Segment(3, 50, [0, 0, 0, BitmapHeight + 3]), .. Jbig2Segment(4, 49, [])], null),
                    (Jbig2Segment(1, 38, region), new PdfDictionary { [Jbig2GlobalsName] = globals }),
                ];

            default:
                byte[] plain = [.. Enumerable.Range(0, 2000).Select(i => (byte)(i % 13 * 7))];
                var encoded = filter switch
                {
                    "FlateDecode" => FlateFilter.Encode(plain),
                    "LZWDecode" => LzwFilter.Encode(plain),
                    "ASCIIHexDecode" => AsciiHexFilter.Encode(plain),
                    "ASCII85Decode" => Ascii85Filter.Encode(plain),
                    "RunLengthDecode" => RunLengthFilter.Encode(plain),
                    _ => plain,
                };
                return [(encoded, null)];
        }
    }

    /// <summary>The parameters of the seed, no parameters, or the parameters of the seed with random values: other kinds of coding, sizes and alignments.</summary>
    private static PdfDictionary? CreateCcittParameters(PdfDictionary? seedParameters, PdfFuzzRandom random)
    {
        if (random.Next(4) == 0)
            return seedParameters;

        if (random.Next(8) == 0)
            return null;

        var parameters = new PdfDictionary();
        if (seedParameters is not null)
        {
            foreach (var (key, value) in seedParameters)
            {
                parameters[key] = value;
            }
        }

        var count = 1 + random.Next(3);
        for (var i = 0; i < count; i++)
        {
            switch (random.Next(6))
            {
                case 0:
                    parameters[PdfNames.K] = PdfInteger.Get(new[] { -1, 0, 1, 2, 4, -5, int.MaxValue }[random.Next(7)]);
                    break;
                case 1:
                    parameters[PdfNames.Columns] = PdfInteger.Get(new[] { 0, -1, 1, 7, 8, BitmapWidth - 1, BitmapWidth + 1, 1728, 65536, int.MaxValue }[random.Next(10)]);
                    break;
                case 2:
                    parameters[RowsName] = PdfInteger.Get(new[] { 0, -1, 1, BitmapHeight - 1, BitmapHeight + 1, 100_000, int.MaxValue }[random.Next(7)]);
                    break;
                case 3:
                    parameters[PdfNames.BlackIs1] = PdfBoolean.Get(random.Next(2) == 0);
                    break;
                case 4:
                    parameters[EncodedByteAlignName] = PdfBoolean.Get(random.Next(2) == 0);
                    break;
                default:
                    parameters[EndOfBlockName] = random.Next(5) == 0 ? PdfInteger.Get(1) : PdfBoolean.Get(random.Next(2) == 0);
                    break;
            }
        }

        return parameters;
    }

    /// <summary>The parameters of the seed, no parameters, global segments that are themselves mutated, or globals that are not a stream.</summary>
    private static PdfDictionary? CreateJbig2Parameters(PdfDictionary? seedParameters, PdfFuzzRandom random)
    {
        switch (random.Next(4))
        {
            case 0:
                return null;
            case 1:
                var globals = new PdfStream();
                globals.SetData(PdfFuzzMutator.Mutate(Jbig2Segment(0, 48, Jbig2PageInformation(BitmapWidth, BitmapHeight, flags: 0)), random));
                return new PdfDictionary { [Jbig2GlobalsName] = globals };
            case 2:
                return new PdfDictionary { [Jbig2GlobalsName] = PdfInteger.Get(1) };
            default:
                return seedParameters;
        }
    }

    /// <summary>Codes a bilevel bitmap (diagonal stripes and a block: runs of every length) with the CCITT encoder of the TIFF encoder, and gets the strip.</summary>
    private static byte[] EncodeCcitt(TiffCompression compression)
    {
        var pixels = new Gray8[BitmapWidth * BitmapHeight];
        for (var y = 0; y < BitmapHeight; y++)
        {
            for (var x = 0; x < BitmapWidth; x++)
            {
                var black = (x + y) % 11 < 4 || (x > 30 && x < 50 && y > 5 && y < 18);
                pixels[(y * BitmapWidth) + x] = new Gray8(black ? (byte)0 : (byte)255);
            }
        }

        using var image = Image.ImportPixelData<Gray8>(pixels, BitmapWidth, BitmapHeight);
        using var stream = new MemoryStream();
        image.Save(stream, new TiffEncoder { Compression = compression, PixelLayout = TiffPixelLayout.Bilevel, RowsPerStrip = BitmapHeight });
        var file = stream.ToArray();

        // A little-endian classic TIFF with one strip: the offset (273) and the length (279) of the strip are in their entries
        var directory = BinaryPrimitives.ReadInt32LittleEndian(file.AsSpan(4));
        var entryCount = BinaryPrimitives.ReadUInt16LittleEndian(file.AsSpan(directory));
        int? offset = null;
        int? length = null;
        for (var i = 0; i < entryCount; i++)
        {
            var entry = file.AsSpan(directory + 2 + (i * 12), 12);
            var value = BinaryPrimitives.ReadUInt16LittleEndian(entry[2..]) == 3 ? BinaryPrimitives.ReadUInt16LittleEndian(entry[8..]) : BinaryPrimitives.ReadInt32LittleEndian(entry[8..]);
            switch (BinaryPrimitives.ReadUInt16LittleEndian(entry))
            {
                case 273:
                    offset = value;
                    break;
                case 279:
                    length = value;
                    break;
            }
        }

        return file.AsSpan(offset ?? throw new InvalidOperationException("The TIFF file has no strip"), length ?? throw new InvalidOperationException("The TIFF file has no strip length")).ToArray();
    }

    /// <summary>A segment of page 1 that refers to no other segment (T.88, 7.2).</summary>
    private static byte[] Jbig2Segment(uint number, int type, byte[] data) => [(byte)(number >> 24), (byte)(number >> 16), (byte)(number >> 8), (byte)number, (byte)type, 0, 1, .. BigEndian((uint)data.Length), .. data];

    private static byte[] Jbig2PageInformation(int width, uint height, int flags) => [.. BigEndian((uint)width), .. BigEndian(height), 0, 0, 0, 0, 0, 0, 0, 0, (byte)flags, 0, 0];

    private static byte[] Jbig2RegionInfo(int width, int height, int x, int y, int combinationOperator) => [.. BigEndian((uint)width), .. BigEndian((uint)height), .. BigEndian((uint)x), .. BigEndian((uint)y), (byte)combinationOperator];

    private static byte[] BigEndian(uint value) => [(byte)(value >> 24), (byte)(value >> 16), (byte)(value >> 8), (byte)value];

    [Fact]
    public void RandomTokensForTheParser()
    {
        var random = new PdfFuzzRandom(PdfFuzzSettings.Seed);
        for (var i = 0; i < PdfFuzzSettings.Iterations * 50; i++)
        {
            var data = PdfFuzzMutator.Mutate("<< /A [1 2 3 0 R (s) <41> /N] /B << /C true >> >>"u8.ToArray(), random);
            var parser = new PdfObjectParser(new PdfLexer(data), new PdfIndirectObjectCollection(), 32);
            try
            {
                while (parser.Lexer.Position < data.Length)
                {
                    var start = parser.Lexer.Position;
                    parser.ParseObject();
                    if (parser.Lexer.Position == start)
                        break;
                }
            }
            catch (Exception ex) when (ex is InvalidPdfContentException or PdfResourceLimitException)
            {
            }
        }
    }
}
