using Meziantou.Framework.Imaging.TestHarness.Fixtures;

namespace Meziantou.Framework.Imaging.Tests.Conformance;

/// <summary>Proves that the manifest validation detects the file-level corpus problems it is responsible for.</summary>
public sealed class FixtureManifestValidatorTests : IDisposable
{
    private readonly FullPath _root = FullPath.GetTempPath() / ("mfi-fixtures-" + Guid.NewGuid().ToString("N"));

    public FixtureManifestValidatorTests() => Directory.CreateDirectory(_root);

    public void Dispose() => Directory.Delete(_root, recursive: true);

    [Fact]
    public void ValidErrorEntryPasses()
    {
        var file = WriteFile("png/a.png", [1, 2, 3]);
        var errors = FixtureManifestValidator.Validate(CreateManifest(CreateEntry("png/a", file)), _root);
        Assert.Empty(errors);
    }

    [Fact]
    public void HashMismatchIsReported()
    {
        var file = WriteFile("png/a.png", [1, 2, 3]);
        File.WriteAllBytes(_root / "png" / "a.png", [1, 2, 4]);
        var errors = FixtureManifestValidator.Validate(CreateManifest(CreateEntry("png/a", file)), _root);
        Assert.Contains(errors, error => error.Contains("sha256", StringComparison.Ordinal));
    }

    [Fact]
    public void OrphanFileIsReported()
    {
        WriteFile("png/orphan.png", [1]);
        var errors = FixtureManifestValidator.Validate(CreateManifest(), _root);
        Assert.Contains(errors, error => error.Contains("png/orphan.png", StringComparison.Ordinal));
    }

    [Fact]
    public void MissingFileIsReported()
    {
        var entry = CreateEntry("png/a", new FixtureFile { Path = "png/missing.png", Sha256 = new string('0', 64) });
        var errors = FixtureManifestValidator.Validate(CreateManifest(entry), _root);
        Assert.Contains(errors, error => error.Contains("does not exist", StringComparison.Ordinal));
    }

    [Fact]
    public void DisallowedLicenseIsReported()
    {
        var file = WriteFile("a.png", [1]);
        var entry = CreateEntry("a", file, new FixtureProvenance { Origin = "hand-authored", License = "GPL-3.0-only" });
        var errors = FixtureManifestValidator.Validate(CreateManifest(entry), _root);
        Assert.Contains(errors, error => error.Contains("GPL-3.0-only", StringComparison.Ordinal));
    }

    [Fact]
    public void ExternalFixtureRequiresSource()
    {
        var file = WriteFile("a.png", [1]);
        var entry = CreateEntry("a", file, new FixtureProvenance { Origin = "external", License = "CC0-1.0" });
        var errors = FixtureManifestValidator.Validate(CreateManifest(entry), _root);
        Assert.Contains(errors, error => error.Contains("source", StringComparison.Ordinal));
    }

    [Fact]
    public void GeneratedFixtureRequiresGeneratorAndTools()
    {
        var file = WriteFile("a.png", [1]);
        var entry = CreateEntry("a", file, new FixtureProvenance { Origin = "generated", License = "CC0-1.0" });
        var errors = FixtureManifestValidator.Validate(CreateManifest(entry), _root);
        Assert.Contains(errors, error => error.Contains("'generator'", StringComparison.Ordinal));
        Assert.Contains(errors, error => error.Contains("'tools'", StringComparison.Ordinal));
    }

    [Fact]
    public void ErrorFixturesRequireExpectedError()
    {
        var file = WriteFile("a.png", [1]);
        var entry = new FixtureEntry { Id = "a", Kind = FixtureKinds.Invalid, Format = "png", Input = file, Provenance = HandAuthored() };
        var errors = FixtureManifestValidator.Validate(CreateManifest(entry), _root);
        Assert.Contains(errors, error => error.Contains("expectedError", StringComparison.Ordinal));
    }

    [Fact]
    public void ValidFixturesRequireReferenceExpectationAndPolicy()
    {
        var file = WriteFile("a.png", [1]);
        var entry = new FixtureEntry { Id = "a", Kind = FixtureKinds.Valid, Format = "png", Input = file, Provenance = HandAuthored() };
        var errors = FixtureManifestValidator.Validate(CreateManifest(entry), _root);
        Assert.Contains(errors, error => error.Contains("'reference'", StringComparison.Ordinal));
        Assert.Contains(errors, error => error.Contains("'comparison'", StringComparison.Ordinal));
        Assert.Contains(errors, error => error.Contains("'expected'", StringComparison.Ordinal));
    }

    [Fact]
    public void KindSpecificErrorsAreEnforced()
    {
        var file = WriteFile("a.png", [1]);
        var unsupported = CreateEntry("u", file, kind: FixtureKinds.Unsupported, exception: "InvalidImageContentException");
        var limit = CreateEntry("l", file, kind: FixtureKinds.Limit, exception: "ImageResourceLimitException");
        var badLimit = new FixtureEntry
        {
            Id = "b",
            Kind = FixtureKinds.Limit,
            Format = "png",
            Input = file,
            Provenance = HandAuthored(),
            ExpectedError = new ExpectedErrorDefinition { Exception = "ImageResourceLimitException", LimitKind = "Width" },
            DecodeOptions = new DecodeOptionsDefinition { Limits = new Dictionary<string, long>(StringComparer.Ordinal) { ["MaxColors"] = 0 } },
        };
        var errors = FixtureManifestValidator.Validate(CreateManifest(unsupported, limit, badLimit), _root);
        Assert.Contains(errors, error => error.Contains("unsupported fixtures must expect UnsupportedImageFeatureException", StringComparison.Ordinal));
        Assert.Contains(errors, error => error.Contains("limit fixtures must declare decodeOptions.limits", StringComparison.Ordinal));
        Assert.Contains(errors, error => error.Contains("unknown resource limit 'MaxColors'", StringComparison.Ordinal));
        Assert.Contains(errors, error => error.Contains("'MaxColors' must be positive", StringComparison.Ordinal));
    }

    [Fact]
    public void PathTraversalAndDuplicateIdsAreReported()
    {
        var file = WriteFile("a.png", [1]);
        var outside = CreateEntry("b", new FixtureFile { Path = "../outside.png", Sha256 = new string('0', 64) });
        var errors = FixtureManifestValidator.Validate(CreateManifest(CreateEntry("a", file), CreateEntry("a", file), outside), _root);
        Assert.Contains(errors, error => error.Contains("duplicate id", StringComparison.Ordinal));
        Assert.Contains(errors, error => error.Contains("escapes the fixture root", StringComparison.Ordinal));
    }

    [Fact]
    public void UnknownFormatIsReported()
    {
        var file = WriteFile("a.tiff", [1]);
        var entry = new FixtureEntry { Id = "a", Kind = FixtureKinds.Invalid, Format = "tiff", Input = file, Provenance = HandAuthored(), ExpectedError = new ExpectedErrorDefinition { Exception = "UnknownImageFormatException" } };
        Assert.Contains(FixtureManifestValidator.Validate(CreateManifest(entry), _root), error => error.Contains("invalid format 'tiff'", StringComparison.Ordinal));
    }

    [Fact]
    public void UnsupportedSchemaVersionIsReported()
    {
        var manifest = new FixtureManifest { SchemaVersion = 1, Fixtures = [] };
        Assert.Single(FixtureManifestValidator.Validate(manifest, _root));
    }

    [Fact]
    public void UnknownManifestPropertiesAreRejected()
    {
        var path = _root / "manifest.json";
        File.WriteAllText(path, """{ "schemaVersion": 2, "fixtures": [], "unexpected": true }""");
        Assert.Throws<System.Text.Json.JsonException>(() => FixtureManifestLoader.Load(path));
    }

    private static FixtureManifest CreateManifest(params FixtureEntry[] entries) => new() { SchemaVersion = FixtureManifest.CurrentSchemaVersion, Fixtures = entries };

    private static FixtureProvenance HandAuthored() => new() { Origin = "hand-authored", License = "CC0-1.0" };

    private static FixtureEntry CreateEntry(string id, FixtureFile input, FixtureProvenance? provenance = null, string kind = FixtureKinds.Invalid, string exception = "InvalidImageContentException") => new()
    {
        Id = id,
        Kind = kind,
        Format = "png",
        Input = input,
        Provenance = provenance ?? HandAuthored(),
        ExpectedError = new ExpectedErrorDefinition { Exception = exception, LimitKind = kind == FixtureKinds.Limit ? "Width" : null },
    };

    private FixtureFile WriteFile(string relativePath, byte[] content)
    {
        var path = _root / relativePath;
        path.CreateParentDirectory();
        File.WriteAllBytes(path, content);
        return new FixtureFile { Path = relativePath, Sha256 = FixtureManifestValidator.ComputeSha256(path) };
    }
}
