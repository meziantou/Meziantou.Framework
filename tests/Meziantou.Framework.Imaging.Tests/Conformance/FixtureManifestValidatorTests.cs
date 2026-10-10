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
        File.WriteAllText(path, """{ "schemaVersion": 3, "fixtures": [], "unexpected": true }""");
        Assert.Throws<System.Text.Json.JsonException>(() => FixtureManifestLoader.Load(path));
    }

    [Fact]
    public void ColorSectionsAreValidated()
    {
        // Two colors of 3 + 1 channels: 2 * 4 * 2 bytes of vectors
        var profile = WriteFile("icc/a.icc", [1, 2, 3]);
        var vectors = WriteFile("icc/a.u16le", new byte[16]);
        ColorProfileEntry Profile(string id = "icc/a", string license = "CC0-1.0") => new() { Id = id, File = profile, Provenance = new FixtureProvenance { Origin = "external", License = license, Source = "https://example.com/a.icc" } };
        ColorTransformEntry Transform(string id = "icc/a-to-a", string source = "icc/a", string intent = "perceptual", bool compensation = false, int sourceChannels = 3, int sampleCount = 2, int maximum = 2, double mean = 0.5, string justification = "The same matrix and curves; only the rounding of the printed samples differs.", string tool = "transicc (LittleCMS 2.19)") => new()
        {
            Id = id,
            Source = source,
            Destination = "icc/a",
            Intent = intent,
            BlackPointCompensation = compensation,
            SourceChannels = sourceChannels,
            DestinationChannels = 1,
            SampleCount = sampleCount,
            Vectors = vectors,
            Reference = new ColorTransformReference { Tool = tool, Command = "transicc -n -c0", Generator = "tools/generator.cs (Generate)" },
            Comparison = new ColorTransformComparison { MaxAbsoluteError = maximum, MaxMeanAbsoluteError = mean, Justification = justification },
        };

        IReadOnlyList<string> Validate(ColorProfileEntry[] profiles, ColorTransformEntry[] transforms)
            => FixtureManifestValidator.Validate(new FixtureManifest { SchemaVersion = FixtureManifest.CurrentSchemaVersion, Fixtures = [], ColorProfiles = profiles, ColorTransforms = transforms }, _root);

        Assert.Empty(Validate([Profile()], [Transform()]));

        // Without the sections, their files are orphans: every committed file needs provenance and a license
        Assert.Equal(2, FixtureManifestValidator.Validate(CreateManifest(), _root).Count(error => error.Contains("is not referenced by the manifest", StringComparison.Ordinal)));

        void AssertError(string expected, ColorProfileEntry[] profiles, ColorTransformEntry[] transforms)
            => Assert.Contains(Validate(profiles, transforms), error => error.Contains(expected, StringComparison.Ordinal));

        AssertError("license 'GPL-3.0' is not in the allowed list", [Profile(license: "GPL-3.0")], [Transform()]);
        AssertError("duplicate id", [Profile(), Profile()], [Transform()]);
        AssertError("duplicate id", [Profile()], [Transform(), Transform()]);
        AssertError("invalid id", [Profile(id: "ICC/A")], []);
        AssertError("the source profile 'icc/missing' is not in 'colorProfiles'", [Profile()], [Transform(source: "icc/missing")]);
        AssertError("invalid intent 'vivid'", [Profile()], [Transform(intent: "vivid")]);
        AssertError("black point compensation does not apply", [Profile()], [Transform(intent: "absolute-colorimetric", compensation: true)]);
        AssertError("channel counts must be 1, 3 or 4", [Profile()], [Transform(sourceChannels: 2)]);
        AssertError("sampleCount must be positive", [Profile()], [Transform(sampleCount: 0)]);
        AssertError("has 16 bytes but 24 are declared", [Profile()], [Transform(sampleCount: 3)]);
        AssertError("the tolerance must be between 0 and 64", [Profile()], [Transform(maximum: 65)]);
        AssertError("the tolerance must be between 0 and 64", [Profile()], [Transform(maximum: 2, mean: 3)]);
        AssertError("the tolerance needs a justification", [Profile()], [Transform(justification: "close enough")]);
        AssertError("the reference must record the tool", [Profile()], [Transform(tool: " ")]);

        // A changed vector file no longer matches its hash
        File.WriteAllBytes(_root / "icc" / "a.u16le", new byte[] { 1, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 });
        AssertError("has sha256", [Profile()], [Transform()]);
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
