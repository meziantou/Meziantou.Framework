using System.Reflection;
using Meziantou.Framework.Imaging.TestHarness.Golden;

namespace Meziantou.Framework.Imaging.Tests.Conformance;

/// <summary>
/// The part of the golden corpus that the generic suites of a test project cover. The suites that run every fixture through
/// the decoders and every encoder through the fault injection are compiled into one test project per codec, and each project
/// declares the fixture formats and the encoders it is responsible for (<c>ImagingTestFixtureFormats</c> and
/// <c>ImagingTestEncoders</c> in its project file, which become assembly metadata). <c>CorpusScopeTests</c> checks that every
/// format and every encoder belongs to exactly one project, so the projects cover the same cases as a single one would.
/// </summary>
/// <remarks>
/// Only the cases of a suite are scoped. The sources of the encoder suites are not: an encoder is tested with the fixtures
/// of every format (<c>EncoderSources</c>), so <see cref="GoldenCorpus.Default"/> stays the whole corpus in every project.
/// </remarks>
internal static class CorpusScope
{
    /// <summary>The MSBuild property that lists the fixture formats of a test project, separated by semicolons.</summary>
    public const string FixtureFormatsPropertyName = "ImagingTestFixtureFormats";

    /// <summary>The MSBuild property that lists the encoders of a test project, separated by semicolons.</summary>
    public const string EncodersPropertyName = "ImagingTestEncoders";

    private const string MetadataPrefix = "Meziantou.Framework.Imaging.Tests.";

    private static readonly Lazy<HashSet<string>> ScopedFormats = new(LoadFormats, LazyThreadSafetyMode.ExecutionAndPublication);

    /// <summary>Gets the name of every encoder configuration of the save cases, in the order of the cases.</summary>
    public static IReadOnlyList<string> AllEncoderNames { get; } = ["png", "png-indexed", "png-minimum-size", "png-frames", "png-layout", "gif", "gif-frames", "jpeg", "jpeg-progressive", "webp", "webp-frames", "qoi", "bmp", "bmp-indexed", "tga", "pnm", "avif", "heic", "jxl", "jxl-lossy", "jxl-progressive", "jp2", "exr"];

    /// <summary>Gets the encoder configurations of this test project, in the order of <see cref="AllEncoderNames"/>.</summary>
    public static IReadOnlyList<string> EncoderNames { get; } = LoadEncoderNames();

    /// <summary>Gets the fixtures of the formats of this test project, valid or not, ordered by id.</summary>
    public static IEnumerable<GoldenFixture> Fixtures => GoldenCorpus.Default.Fixtures.Where(fixture => ScopedFormats.Value.Contains(fixture.Entry.Format));

    /// <summary>Gets the ids of the fixtures of a kind among <see cref="Fixtures"/>, ordered.</summary>
    public static IEnumerable<string> GetIds(string kind) => Fixtures.Where(fixture => fixture.Entry.Kind == kind).Select(fixture => fixture.Id);

    /// <summary>Splits the value of <see cref="FixtureFormatsPropertyName"/> or <see cref="EncodersPropertyName"/>.</summary>
    public static string[] ParseList(string? value) => (value ?? "").Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    private static HashSet<string> LoadFormats()
    {
        var formats = ReadMetadata(FixtureFormatsPropertyName);
        var known = GoldenCorpus.Default.Fixtures.Select(fixture => fixture.Entry.Format).ToHashSet(StringComparer.Ordinal);
        var unknown = formats.Where(format => !known.Contains(format)).ToArray();
        if (unknown.Length > 0)
            throw new InvalidOperationException($"{FixtureFormatsPropertyName} lists formats without a fixture: {string.Join(", ", unknown)}. Formats of the corpus: {string.Join(", ", known.Order(StringComparer.Ordinal))}.");

        return formats.ToHashSet(StringComparer.Ordinal);
    }

    private static string[] LoadEncoderNames()
    {
        var names = ReadMetadata(EncodersPropertyName);
        var unknown = names.Where(name => !AllEncoderNames.Contains(name)).ToArray();
        if (unknown.Length > 0)
            throw new InvalidOperationException($"{EncodersPropertyName} lists unknown encoders: {string.Join(", ", unknown)}. Known encoders: {string.Join(", ", AllEncoderNames)}.");

        return [.. AllEncoderNames.Where(names.Contains)];
    }

    private static string[] ReadMetadata(string propertyName)
    {
        var key = MetadataPrefix + propertyName;
        var value = typeof(CorpusScope).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>().FirstOrDefault(attribute => attribute.Key == key)?.Value;
        return ParseList(value);
    }
}
