using System.Collections.ObjectModel;
using System.Security.Cryptography;
using System.Text.Json;

namespace Meziantou.Framework.Pdf.TestHarness.Fixtures;

/// <summary>
/// The manifest of the fixtures (<c>tests/Meziantou.Framework.Pdf.Fixtures/manifest.json</c>): every file of the corpus
/// with its SHA-256, its provenance and its license, and what the tests expect of the fixtures.
/// </summary>
public sealed class PdfFixtureManifest
{
    private static readonly PdfFixtureManifestJsonContext SerializerContext = new(new JsonSerializerOptions(PdfFixtureManifestJsonContext.Default.Options)
    {
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    });

    /// <summary>Gets or sets the version of the schema of the manifest.</summary>
    public int SchemaVersion { get; set; } = 1;

    /// <summary>Gets or sets how the generated fixtures are produced.</summary>
    public PdfFixtureGenerator? Generator { get; set; }

    /// <summary>Gets the fixtures.</summary>
    public Collection<PdfFixture> Fixtures { get; init; } = [];

    /// <summary>Gets the vendored data sets: their files and their hashes.</summary>
    public Collection<PdfVendoredDataSet> Vendored { get; init; } = [];

    /// <summary>Gets the path of the manifest of the repository.</summary>
    public static FullPath DefaultPath => RepositoryPaths.Fixtures / "manifest.json";

    /// <summary>Reads the manifest of the repository.</summary>
    /// <returns>The manifest.</returns>
    public static PdfFixtureManifest Load() => Load(DefaultPath);

    /// <summary>Reads a manifest.</summary>
    /// <param name="path">The path of the manifest.</param>
    /// <returns>The manifest.</returns>
    public static PdfFixtureManifest Load(string path) => JsonSerializer.Deserialize(File.ReadAllText(path), SerializerContext.PdfFixtureManifest) ?? throw new InvalidOperationException("The manifest is empty");

    /// <summary>Writes the manifest.</summary>
    /// <param name="path">The path of the manifest.</param>
    public void Save(string path) => File.WriteAllText(path, JsonSerializer.Serialize(this, SerializerContext.PdfFixtureManifest).ReplaceLineEndings("\n") + "\n");

    /// <summary>Computes the SHA-256 of a file, in lowercase hexadecimal.</summary>
    /// <param name="path">The file.</param>
    /// <returns>The hash.</returns>
    public static string ComputeSha256(string path) => Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path)));

    /// <summary>Gets the full path of a fixture.</summary>
    /// <param name="fixture">The fixture.</param>
    /// <returns>The path.</returns>
    public static FullPath GetPath(PdfFixture fixture) => RepositoryPaths.Fixtures / fixture.Path;
}
