using Meziantou.Framework.Imaging.TestHarness.Fixtures;

namespace Meziantou.Framework.Imaging.TestHarness.Golden;

/// <summary>
/// The validated golden corpus. Loading validates the whole manifest (hashes, layouts, policies...) first, so golden
/// tests only ever consume consistent fixtures.
/// </summary>
/// <example>
/// <code>
/// public static TheoryData&lt;string&gt; PngFixtures => [.. GoldenCorpus.Default.GetIds(format: "png", kind: FixtureKinds.Valid)];
///
/// [Theory, MemberData(nameof(PngFixtures))]
/// public void DecodesLikeReference(string id)
/// {
///     var fixture = GoldenCorpus.Default.Get(id);
///     using var image = Image.Load(fixture.InputPath);
///     GoldenAssert.ImageMatches(fixture, ImageSnapshots.Create(image));
/// }
/// </code>
/// </example>
public sealed class GoldenCorpus
{
    private static readonly Lazy<GoldenCorpus> DefaultCorpus = new(() => Load(FixtureRoot.GetDirectory()), LazyThreadSafetyMode.ExecutionAndPublication);
    private readonly Dictionary<string, GoldenFixture> _fixtures;

    private GoldenCorpus(FullPath rootDirectory, FixtureManifest manifest)
    {
        RootDirectory = rootDirectory;
        Manifest = manifest;
        _fixtures = manifest.Fixtures.ToDictionary(entry => entry.Id, entry => new GoldenFixture(rootDirectory, entry), StringComparer.Ordinal);
    }

    /// <summary>Gets the corpus copied next to the test assembly (validated once per process).</summary>
    public static GoldenCorpus Default => DefaultCorpus.Value;

    /// <summary>Gets the corpus root directory.</summary>
    public FullPath RootDirectory { get; }

    /// <summary>Gets the manifest.</summary>
    public FixtureManifest Manifest { get; }

    /// <summary>Gets all the fixtures, ordered by id.</summary>
    public IEnumerable<GoldenFixture> Fixtures => _fixtures.Values.OrderBy(fixture => fixture.Id, StringComparer.Ordinal);

    /// <summary>
    /// Gets the AV1 conformance vectors (raw OBU streams with plane digests), ordered by id. They are not fixtures: they
    /// have no pixel reference and are never returned by <see cref="Fixtures"/> or <see cref="GetIds"/>.
    /// </summary>
    public IEnumerable<Av1VectorEntry> Av1Vectors => (Manifest.Av1Vectors?.Entries ?? []).OrderBy(vector => vector.Id, StringComparer.Ordinal);

    /// <summary>Gets an AV1 conformance vector.</summary>
    /// <param name="id">The vector id, e.g. <c>av1/lossless-gbr-8bit-34x36</c>.</param>
    /// <returns>The vector.</returns>
    /// <exception cref="KeyNotFoundException">Unknown id.</exception>
    public Av1VectorEntry GetAv1Vector(string id)
    {
        ArgumentNullException.ThrowIfNull(id);
        return (Manifest.Av1Vectors?.Entries ?? []).FirstOrDefault(vector => vector.Id == id) ?? throw new KeyNotFoundException($"The golden corpus has no AV1 vector '{id}'.");
    }

    /// <summary>Loads and validates a corpus.</summary>
    /// <param name="rootDirectory">The corpus root (containing manifest.json).</param>
    /// <returns>The corpus.</returns>
    /// <exception cref="InvalidOperationException">The manifest is invalid; the message lists every error.</exception>
    public static GoldenCorpus Load(FullPath rootDirectory)
    {
        var manifest = FixtureManifestLoader.Load(rootDirectory / FixtureRoot.ManifestFileName);
        var errors = FixtureManifestValidator.Validate(manifest, rootDirectory);
        if (errors.Count > 0)
            throw new InvalidOperationException($"The golden corpus '{rootDirectory}' is invalid:{Environment.NewLine}{string.Join(Environment.NewLine, errors)}");

        return new GoldenCorpus(rootDirectory, manifest);
    }

    /// <summary>Gets a fixture.</summary>
    /// <param name="id">The fixture id.</param>
    /// <returns>The fixture.</returns>
    /// <exception cref="KeyNotFoundException">Unknown id.</exception>
    public GoldenFixture Get(string id)
    {
        ArgumentNullException.ThrowIfNull(id);
        return _fixtures.TryGetValue(id, out var fixture) ? fixture : throw new KeyNotFoundException($"The golden corpus has no fixture '{id}'.");
    }

    /// <summary>Gets fixture ids, e.g. as theory data. Every filter is optional.</summary>
    /// <param name="format">The container format (<c>png</c>, <c>gif</c>, <c>jpeg</c>).</param>
    /// <param name="kind">The kind (<see cref="FixtureKinds"/>).</param>
    /// <param name="feature">A required encoding feature (e.g. <c>apng</c>, <c>png.interlace=adam7</c>).</param>
    /// <returns>The ids, ordered.</returns>
    public IEnumerable<string> GetIds(string? format = null, string? kind = null, string? feature = null)
        => Fixtures
            .Where(fixture => format is null || fixture.Entry.Format == format)
            .Where(fixture => kind is null || fixture.Entry.Kind == kind)
            .Where(fixture => feature is null || fixture.Entry.HasFeature(feature))
            .Select(fixture => fixture.Id);
}
