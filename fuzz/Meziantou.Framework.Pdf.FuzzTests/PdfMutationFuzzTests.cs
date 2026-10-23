using System.Globalization;
using Meziantou.Framework.Pdf.Filters;
using Meziantou.Framework.Pdf.Objects;
using Meziantou.Framework.Pdf.Parsing;
using Meziantou.Framework.Pdf.TestHarness;
using Meziantou.Framework.Pdf.TestHarness.Fixtures;

namespace Meziantou.Framework.Pdf.FuzzTests;

/// <summary>Mutated fixtures and mutated filter data only throw documented exceptions, in bounded time.</summary>
public sealed class PdfMutationFuzzTests
{
    public static TheoryData<string> Seeds => new(PdfFixtureManifest.Load().Fixtures.Where(f => f.Kind is "valid" or "damaged").Select(f => f.Path));

    public static TheoryData<string> Filters => new(["FlateDecode", "LZWDecode", "ASCIIHexDecode", "ASCII85Decode", "RunLengthDecode", "DCTDecode", "JPXDecode"]);

    [Theory]
    [MemberData(nameof(Seeds))]
    public void MutatedFixtures(string path)
    {
        var seed = File.ReadAllBytes(RepositoryPaths.Fixtures / path);
        var random = PdfFuzzRandom.Create(PdfFuzzSettings.Seed, path);
        PdfFuzzOracle.Run(seed, path);
        for (var i = 0; i < PdfFuzzSettings.Iterations; i++)
        {
            var mutated = PdfFuzzMutator.Mutate(seed, random);
            PdfFuzzOracle.Run(mutated, string.Create(CultureInfo.InvariantCulture, $"{path} #{i}"));
        }
    }

    [Theory]
    [MemberData(nameof(Filters))]
    public void MutatedFilterData(string filter)
    {
        var random = PdfFuzzRandom.Create(PdfFuzzSettings.Seed, filter);
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

        for (var i = 0; i < PdfFuzzSettings.Iterations * 10; i++)
        {
            var data = PdfFuzzMutator.Mutate(encoded, random);
            var parameters = random.Next(3) == 0 ? new PdfDictionary
            {
                [PdfNames.Predictor] = PdfInteger.Get(random.Next(2) == 0 ? 2 : 10 + random.Next(6)),
                [PdfNames.Columns] = PdfInteger.Get(random.Next(-1, 100)),
                [PdfNames.Colors] = PdfInteger.Get(random.Next(-1, 5)),
                [PdfNames.BitsPerComponent] = PdfInteger.Get(new[] { 1, 2, 4, 8, 16, 3 }[random.Next(6)]),
            } : null;
            try
            {
                PdfFilterPipeline.Decode(data, [PdfName.Create(filter)], [parameters], new PdfDecodeLimits(1 << 20), stopBeforeImageFilters: false, out _);
            }
            catch (Exception ex) when (ex is InvalidPdfContentException or UnsupportedPdfFeatureException or PdfResourceLimitException)
            {
            }
        }
    }

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
