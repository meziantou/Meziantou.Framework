using Meziantou.Framework.Imaging.TestHarness.Adapters;
using Meziantou.Framework.Imaging.TestHarness.Fixtures;
using Meziantou.Framework.Imaging.TestHarness.Golden;

namespace Meziantou.Framework.Imaging.Tests.Conformance;

/// <summary>
/// Eager loading of every corpus input through every input variant: decoded rows and metadata are compared with the
/// independent references (untyped and typed loads), and error fixtures must fail with their declared exception.
/// </summary>
public sealed class EagerLoadConformanceTests
{
    public static TheoryData<string, InputVariant> ValidCases => CreateCases(valid: true);

    public static TheoryData<string, InputVariant> ErrorCases => CreateCases(valid: false);

    [Theory]
    [MemberData(nameof(ValidCases))]
    public async Task LoadMatchesReferences(string id, InputVariant variant)
    {
        var fixture = GoldenCorpus.Default.Get(id);
        var format = FixtureOptions.GetFormat(fixture);
        var data = fixture.ReadInput();
        using (var image = await InputVariants.LoadAsync(variant, data, format, options: null, XunitCancellationToken))
        {
            GoldenAssert.ImageMatches(fixture, ImageSnapshots.Capture(image, includeMetadata: true));
        }

        using var typed = await LoadTypedAsync(Enum.Parse<PixelFormat>(fixture.Expected.PixelFormat), variant, data, format);
        GoldenAssert.ImageMatches(fixture, ImageSnapshots.Capture(typed, includeMetadata: true));
    }

    [Theory]
    [MemberData(nameof(ErrorCases))]
    public async Task ErrorFixturesFailExplicitly(string id, InputVariant variant)
    {
        var fixture = GoldenCorpus.Default.Get(id);
        var format = FixtureOptions.GetFormat(fixture);
        var options = FixtureOptions.CreateDecodeOptions(fixture);
        var data = fixture.ReadInput();
        await GoldenAssert.FailsAsExpectedAsync(fixture, () => InputVariants.LoadAsync(variant, data, format, options, XunitCancellationToken));
    }

    private static TheoryData<string, InputVariant> CreateCases(bool valid)
    {
        var data = new TheoryData<string, InputVariant>();
        foreach (var fixture in GoldenCorpus.Default.Fixtures.Where(fixture => fixture.IsValid == valid))
        {
            foreach (var variant in InputVariants.All)
            {
                data.Add(fixture.Id, variant);
            }
        }

        return data;
    }

    private static async Task<Image> LoadTypedAsync(PixelFormat pixelFormat, InputVariant variant, byte[] data, ImageFormat format) => pixelFormat switch
    {
        PixelFormat.Rgba32 => await InputVariants.LoadAsync<Rgba32>(variant, data, format, options: null, XunitCancellationToken),
        PixelFormat.Rgb24 => await InputVariants.LoadAsync<Rgb24>(variant, data, format, options: null, XunitCancellationToken),
        PixelFormat.Rgba64 => await InputVariants.LoadAsync<Rgba64>(variant, data, format, options: null, XunitCancellationToken),
        PixelFormat.Gray8 => await InputVariants.LoadAsync<Gray8>(variant, data, format, options: null, XunitCancellationToken),
        PixelFormat.Gray16 => await InputVariants.LoadAsync<Gray16>(variant, data, format, options: null, XunitCancellationToken),
        _ => await InputVariants.LoadAsync<Bgra32>(variant, data, format, options: null, XunitCancellationToken),
    };
}
