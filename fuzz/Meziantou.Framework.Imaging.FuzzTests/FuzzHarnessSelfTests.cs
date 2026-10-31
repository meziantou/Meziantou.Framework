using Meziantou.Framework.Imaging.Internals;
using Meziantou.Framework.Imaging.TestHarness.Golden;

namespace Meziantou.Framework.Imaging.FuzzTests;

/// <summary>
/// Self-tests of the fuzz harness with deliberate defects: the minimizer must shrink a failing input to its cause; the mutator
/// must be deterministic; failure signatures must identify the undocumented exception and its first library frame; the PNG
/// CRC fix-up must restore valid chunks.
/// </summary>
public sealed class FuzzHarnessSelfTests
{
    [Fact]
    public void MinimizerShrinksToTheFailingBytes()
    {
        byte[] input = [.. Enumerable.Range(0, 300).Select(i => (byte)(i % 200)), 0xEE, 0xEF, .. Enumerable.Range(0, 100).Select(i => (byte)i)];
        static FuzzFailure? Check(byte[] data) => data.AsSpan().IndexOf((ReadOnlySpan<byte>)[0xEE, 0xEF]) >= 0 ? FuzzFailure.Property("marker", "found") : null;
        var minimized = FuzzMinimizer.Minimize(input, "property:marker", Check);
        Assert.Equal([0xEE, 0xEF], minimized);
    }

    [Fact]
    public void MutationsAreDeterministic()
    {
        byte[] seed = [.. Enumerable.Range(0, 256).Select(i => (byte)i)];
        var first = new FuzzRandom(1234);
        var second = new FuzzRandom(1234);
        for (var i = 0; i < 200; i++)
        {
            Assert.Equal(FuzzMutator.Mutate(first, seed, [seed]), FuzzMutator.Mutate(second, seed, [seed]));
        }
    }

    [Fact]
    public void FailureSignaturesNameTheExceptionAndTheFirstLibraryFrame()
    {
        var exception = Assert.Throws<ArgumentOutOfRangeException>(() => new SlabPool().Rent(-1, clear: false));
        var failure = FuzzFailure.FromException("load", exception);
        Assert.StartsWith("exception:load:System.ArgumentOutOfRangeException:", failure.Signature, StringComparison.Ordinal);
        Assert.Contains("SlabPool.Rent", failure.Signature, StringComparison.Ordinal);
    }

    [Fact]
    public void PngCrcFixUpRestoresValidChunks()
    {
        using var image = new Image<Rgba32>(3, 2);
        using var stream = new MemoryStream();
        image.Save(stream, new Formats.PngEncoder());
        var data = stream.ToArray();
        data[19] ^= 0x01; // IHDR width 3 -> 2: the CRC no longer matches
        Assert.Throws<InvalidImageContentException>(() => Image.Identify(data));
        FuzzMutator.FixPngCrcs(data);
        Assert.Equal(2, Image.Identify(data).Width);
    }

    [Fact]
    public void TheOpenExrSeedsReachEveryCompressionAndLevelMode()
    {
        // The encoder of the library writes scan lines without PXR24 or B44: the corpus files written by OpenEXR and the
        // files of the builder are the seeds of the rest, and they must decode, so that their mutations reach the decoder
        var seeds = FuzzSeeds.Get("exr");
        var ids = seeds.Select(seed => seed.Id).ToHashSet(StringComparer.Ordinal);
        foreach (var feature in new[] { "exr.compression=pxr24", "exr.compression=b44", "exr.compression=b44a", "exr.levels=mipmap", "exr.levels=ripmap" })
        {
            var fixtures = GoldenCorpus.Default.Fixtures.Where(fixture => fixture.IsValid && fixture.Entry.HasFeature(feature)).ToList();
            Assert.NotEmpty(fixtures);
            Assert.True(fixtures.TrueForAll(fixture => ids.Contains(fixture.Id)), $"The fixtures with the feature '{feature}' are not all fuzz seeds.");
        }

        foreach (var seed in seeds.Where(seed => seed.Id.StartsWith("exr/builder-", StringComparison.Ordinal)))
        {
            try
            {
                using var image = Image.Load(seed.Data);
            }
            catch (ImageException exception)
            {
                Assert.Fail($"The seed '{seed.Id}' does not decode: {exception.Message}");
            }
        }
    }
}
