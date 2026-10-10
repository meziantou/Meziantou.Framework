using Meziantou.Framework.Imaging.TestHarness.Fixtures;
using Meziantou.Framework.Imaging.TestHarness.Golden;
using Meziantou.Framework.Imaging.TestHarness.Streams;

namespace Meziantou.Framework.Imaging.Tests.Conformance.Hardening;

/// <summary>
/// No clean end on malformed data: every proper prefix of every valid corpus input (PNG,
/// APNG, GIF, baseline and progressive JPEG) must fail the eager load, the full identification and the sequential reader
/// (which may return the complete frames before the cut, but must then throw instead of reporting its end), with
/// <see cref="InvalidImageContentException"/>, or <see cref="UnknownImageFormatException"/> while the signature itself is
/// incomplete. The reader reads through a non-seekable stream with short reads, and nothing leaks.
/// </summary>
public sealed class TruncationTests
{
    private const int MaxPrefixes = 600;

    private static readonly PixelConversionOptions Conversion = new() { DiscardIncompatibleColorProfile = true };

    /// <summary>
    /// Every valid fixture whose proper prefixes are all incomplete. Formats without an end-of-image marker are excluded
    /// (<see cref="FixtureTraits.ProperPrefixMayBeValid"/>): a prefix of such an input can be a complete image, which is a
    /// property of the format, not a missing check. Their truncation behavior is covered by the codec conformance tests,
    /// which know where the image data ends.
    /// </summary>
    public static TheoryData<string> ValidIds => [.. GoldenCorpus.Default.GetIds(kind: FixtureKinds.Valid)
        .Where(id => GoldenCorpus.Default.Get(id) is { Entry.DecodeOptions: null } fixture && !FixtureTraits.ProperPrefixMayBeValid(fixture))];

    [Theory]
    [MemberData(nameof(ValidIds))]
    public void EveryProperPrefixFailsExplicitly(string id)
    {
        var data = GoldenCorpus.Default.Get(id).ReadInput();
        var step = Math.Max(1, data.Length / MaxPrefixes);
        for (var length = 0; length < data.Length; length += length < 64 || data.Length - length < 64 ? 1 : step)
        {
            var prefix = data[..length];
            using var audit = new PoolAudit();
            AssertTruncationError(prefix, length, "load", Catch(() => Image.Load(prefix, new ImageDecodeOptions { Conversion = Conversion }).Dispose()));
            AssertTruncationError(prefix, length, "identify-full", Catch(() => Image.Identify(prefix, new ImageIdentifyOptions { Mode = ImageIdentifyMode.FullScan })));
            var framesRead = -1;
            AssertTruncationError(prefix, length, "reader", Catch(() =>
            {
                using var stream = new TestInputStream(prefix) { Seekable = false, MaxBytesPerRead = 1 + (length % 7) };
                using var reader = Image.OpenReader<Rgba64>(stream, new ImageReaderOptions { Conversion = Conversion });
                framesRead = 0;
                if (reader.Info.HasPosterFrame == true)
                {
                    reader.ReadPosterFrame()?.Dispose();
                }

                while (reader.ReadFrame() is { } frame)
                {
                    frame.Dispose();
                    framesRead++;
                }
            }));
            audit.AssertClean($"{id} truncated to {length} bytes");
        }
    }

    private static void AssertTruncationError(byte[] prefix, int length, string operation, Exception? exception)
    {
        if (exception is null)
            Assert.Fail($"{operation} of the first {length} bytes succeeded: a truncated input must never end cleanly.");

        var signatureIncomplete = Image.DetectFormat(prefix) == ImageFormat.Unknown;
        if (signatureIncomplete ? exception is not UnknownImageFormatException : exception is not InvalidImageContentException)
            Assert.Fail($"{operation} of the first {length} bytes threw {exception.GetType().Name} ({(signatureIncomplete ? "UnknownImageFormatException" : "InvalidImageContentException")} expected): {exception}");
    }

    private static Exception? Catch(Action action)
    {
        try
        {
            action();
            return null;
        }
        catch (Exception exception)
        {
            return exception;
        }
    }
}
