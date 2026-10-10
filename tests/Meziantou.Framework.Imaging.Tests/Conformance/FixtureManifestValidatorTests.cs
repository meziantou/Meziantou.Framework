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

    [Fact]
    public void ValidAv1VectorPasses()
    {
        var file = WriteFile("av1/a.obu", [1, 2, 3]);
        var errors = FixtureManifestValidator.Validate(CreateAv1Manifest(CreateAv1Vector("av1/a", file)), _root);
        Assert.Empty(errors);

        // A monochrome vector has one plane
        var gray = CreateAv1Vector("av1/a", file, subsampling: "4:0:0", planes: 1);
        Assert.Empty(FixtureManifestValidator.Validate(CreateAv1Manifest(gray), _root));
    }

    [Fact]
    public void Av1VectorFilesAreHashedAndNeverOrphans()
    {
        var file = WriteFile("av1/a.obu", [1, 2, 3]);
        WriteFile("av1/orphan.obu", [4]);
        var errors = FixtureManifestValidator.Validate(CreateAv1Manifest(CreateAv1Vector("av1/a", file)), _root);
        Assert.Contains(errors, error => error.Contains("av1/orphan.obu", StringComparison.Ordinal));
        Assert.DoesNotContain(errors, error => error.Contains("av1/a.obu", StringComparison.Ordinal));

        File.Delete(_root / "av1" / "orphan.obu");
        File.WriteAllBytes(_root / "av1" / "a.obu", [1, 2, 4]);
        errors = FixtureManifestValidator.Validate(CreateAv1Manifest(CreateAv1Vector("av1/a", file)), _root);
        Assert.Contains(errors, error => error.Contains("sha256", StringComparison.Ordinal));

        var missing = CreateAv1Vector("av1/b", new FixtureFile { Path = "av1/b.obu", Sha256 = new string('0', 64) });
        Assert.Contains(FixtureManifestValidator.Validate(CreateAv1Manifest(missing), _root), error => error.Contains("does not exist", StringComparison.Ordinal));
    }

    [Fact]
    public void Av1VectorsNeedProvenanceLicenseAndTwoDecoders()
    {
        var file = WriteFile("av1/a.obu", [1, 2, 3]);

        var license = CreateAv1Vector("av1/a", file, provenance: new FixtureProvenance { Origin = "generated", License = "GPL-3.0-only", Generator = "g", Tools = ["t"], Commands = ["c"] });
        Assert.Contains(FixtureManifestValidator.Validate(CreateAv1Manifest(license), _root), error => error.Contains("GPL-3.0-only", StringComparison.Ordinal));

        var noCommand = CreateAv1Vector("av1/a", file, provenance: new FixtureProvenance { Origin = "generated", License = "CC0-1.0", Generator = "g", Tools = ["t"] });
        Assert.Contains(FixtureManifestValidator.Validate(CreateAv1Manifest(noCommand), _root), error => error.Contains("'commands'", StringComparison.Ordinal));

        var noTools = CreateAv1Vector("av1/a", file, provenance: new FixtureProvenance { Origin = "generated", License = "CC0-1.0", Commands = ["c"] });
        var errors = FixtureManifestValidator.Validate(CreateAv1Manifest(noTools), _root);
        Assert.Contains(errors, error => error.Contains("'generator'", StringComparison.Ordinal));
        Assert.Contains(errors, error => error.Contains("'tools'", StringComparison.Ordinal));

        // A digest from a single decoder is not a reference
        var oneDecoder = CreateAv1Manifest([new Av1VectorTool { Name = "libdav1d 1.5.4", Command = "c" }], CreateAv1Vector("av1/a", file));
        Assert.Contains(FixtureManifestValidator.Validate(oneDecoder, _root), error => error.Contains("two distinct independent decoders", StringComparison.Ordinal));
        var sameDecoderTwice = CreateAv1Manifest([new Av1VectorTool { Name = "libdav1d 1.5.4", Command = "c" }, new Av1VectorTool { Name = "libdav1d 1.5.4", Command = "d" }], CreateAv1Vector("av1/a", file));
        Assert.Contains(FixtureManifestValidator.Validate(sameDecoderTwice, _root), error => error.Contains("two distinct independent decoders", StringComparison.Ordinal));
    }

    [Fact]
    public void Av1VectorShapeIsEnforced()
    {
        var file = WriteFile("av1/a.obu", [1, 2, 3]);
        AssertAv1Error(CreateAv1Vector("av1/a", file, bitDepth: 9), "invalid bitDepth 9");
        AssertAv1Error(CreateAv1Vector("av1/a", file, subsampling: "4:1:1"), "invalid subsampling '4:1:1'");
        AssertAv1Error(CreateAv1Vector("av1/a", file, subsampling: "4:0:0", planes: 3), "3 plane digests, 1 expected");
        AssertAv1Error(CreateAv1Vector("av1/a", file, planes: 1), "1 plane digests, 3 expected");
        AssertAv1Error(CreateAv1Vector("av1/a", file, frameCount: 2), "frameCount 2 does not match the 1 frames");
        AssertAv1Error(CreateAv1Vector("av1/a", file, digest: new string('A', 64)), "plane digests are SHA-256 hashes");
        AssertAv1Error(CreateAv1Vector("av1/a", file, digest: "abc"), "plane digests are SHA-256 hashes");
        AssertAv1Error(CreateAv1Vector("av1/a", file, width: 0), "dimensions must be positive");
        AssertAv1Error(CreateAv1Vector("av1/a", file, features: []), "features must be present");
        AssertAv1Error(CreateAv1Vector("av1/a", file, features: ["av1.lossless", "av1.lossless"]), "features must be present");
        AssertAv1Error(CreateAv1Vector("other/a", file), "invalid id");
        AssertAv1Error(CreateAv1Vector("av1/A", file), "invalid id");

        var elsewhere = WriteFile("png/a.obu", [1]);
        AssertAv1Error(CreateAv1Vector("av1/a", elsewhere), "must be a raw OBU stream 'av1/*.obu'");
        var extension = WriteFile("av1/a.ivf", [1]);
        AssertAv1Error(CreateAv1Vector("av1/a", extension), "must be a raw OBU stream 'av1/*.obu'");
    }

    [Fact]
    public void Av1VectorsAreNotFixtures()
    {
        var file = WriteFile("av1/a.obu", [1, 2, 3]);
        var other = WriteFile("av1/b.obu", [4]);

        // The identifiers of the vectors and of the fixtures share one namespace, and a file belongs to one vector
        var duplicate = CreateAv1Manifest(CreateAv1Vector("av1/a", file), CreateAv1Vector("av1/a", other));
        Assert.Contains(FixtureManifestValidator.Validate(duplicate, _root), error => error.Contains("duplicate id", StringComparison.Ordinal));
        var sameFile = CreateAv1Manifest(CreateAv1Vector("av1/a", file), CreateAv1Vector("av1/b", file));
        Assert.Contains(FixtureManifestValidator.Validate(sameFile, _root), error => error.Contains("already used by another entry", StringComparison.Ordinal));

        // A vector has no pixel reference: the generic suites must never find it among the fixtures
        var asFixture = CreateManifest(CreateEntry("av1/a", file));
        Assert.Contains(FixtureManifestValidator.Validate(asFixture, _root), error => error.Contains("belong to 'av1Vectors'", StringComparison.Ordinal));
        var fixtureOnVectorFile = CreateManifest(CreateEntry("invalid/png/a", other));
        Assert.Contains(FixtureManifestValidator.Validate(fixtureOnVectorFile, _root), error => error.Contains("belong to 'av1Vectors'", StringComparison.Ordinal));
    }

    [Fact]
    public void Av1VectorSectionRoundTripsThroughTheLoader()
    {
        var path = _root / "manifest.json";
        File.WriteAllText(path, """
            {
              "schemaVersion": 2,
              "fixtures": [],
              "av1Vectors": {
                "generator": { "script": "s", "command": "c", "tools": ["t"] },
                "headerParser": { "name": "p", "command": "c" },
                "decoders": [{ "name": "a", "command": "c" }, { "name": "b", "command": "c" }],
                "planeDigest": "d",
                "entries": [{
                  "id": "av1/a",
                  "input": { "path": "av1/a.obu", "sha256": "0000000000000000000000000000000000000000000000000000000000000000", "role": "encoded" },
                  "provenance": { "origin": "generated", "license": "CC0-1.0" },
                  "features": ["av1.bitDepth=10"],
                  "pixelFormat": "gray10le",
                  "bitDepth": 10,
                  "subsampling": "4:0:0",
                  "frameCount": 1,
                  "frames": [{ "width": 3, "height": 2, "planes": ["1111111111111111111111111111111111111111111111111111111111111111"] }]
                }]
              }
            }
            """);
        var vector = Assert.Single(FixtureManifestLoader.Load(path).Av1Vectors!.Entries);
        Assert.Equal(("av1/a", "gray10le", 10, "4:0:0", 1, 1), (vector.Id, vector.PixelFormat, vector.BitDepth, vector.Subsampling, vector.FrameCount, vector.PlaneCount));
        Assert.Equal((3, 2), (vector.Frames[0].Width, vector.Frames[0].Height));
        Assert.True(vector.HasFeature("av1.bitDepth=10"));
        Assert.False(vector.HasFeature("av1.bitDepth"));
        Assert.True(vector.HasFeatureKey("av1.bitDepth"));
        Assert.False(vector.HasFeatureKey("av1.bit"));

        // Unknown properties are rejected in the section too
        File.WriteAllText(path, """{ "schemaVersion": 2, "fixtures": [], "av1Vectors": { "generator": { "script": "s", "command": "c", "tools": [] }, "headerParser": { "name": "p", "command": "c" }, "decoders": [], "planeDigest": "d", "entries": [], "unexpected": true } }""");
        Assert.Throws<System.Text.Json.JsonException>(() => FixtureManifestLoader.Load(path));
    }

    private void AssertAv1Error(Av1VectorEntry vector, string expected)
        => Assert.Contains(FixtureManifestValidator.Validate(CreateAv1Manifest(vector), _root), error => error.Contains(expected, StringComparison.Ordinal));

    private static FixtureManifest CreateAv1Manifest(params Av1VectorEntry[] vectors)
        => CreateAv1Manifest([new Av1VectorTool { Name = "libdav1d 1.5.4", Command = "ffmpeg -c:v libdav1d" }, new Av1VectorTool { Name = "libaom 3.15.1", Command = "ffmpeg -c:v libaom-av1" }], vectors);

    private static FixtureManifest CreateAv1Manifest(Av1VectorTool[] decoders, params Av1VectorEntry[] vectors) => new()
    {
        SchemaVersion = FixtureManifest.CurrentSchemaVersion,
        Fixtures = [],
        Av1Vectors = new Av1VectorSet
        {
            Generator = new FixtureGeneratorInfo { Script = "tools/generator.cs", Command = "generator avif", Tools = ["ffmpeg 9.0.2"] },
            HeaderParser = new Av1VectorTool { Name = "ffmpeg 9.0.2 (trace_headers)", Command = "ffmpeg -bsf:v trace_headers" },
            Decoders = decoders,
            PlaneDigest = "SHA-256 of the samples of one plane.",
            Entries = vectors,
        },
    };

    private static Av1VectorEntry CreateAv1Vector(string id, FixtureFile input, FixtureProvenance? provenance = null, int bitDepth = 8, string subsampling = "4:2:0", int? planes = null,
        int frameCount = 1, string? digest = null, int width = 4, string[]? features = null) => new()
        {
            Id = id,
            Input = input,
            Provenance = provenance ?? new FixtureProvenance { Origin = "generated", License = "CC0-1.0", Generator = "tools/generator.cs (Vectors)", Tools = ["ffmpeg 9.0.2", "libaom 3.15.1"], Commands = ["ffmpeg -f obu {input}"] },
            Features = features ?? ["av1.bitDepth=8"],
            PixelFormat = "yuv420p",
            BitDepth = bitDepth,
            Subsampling = subsampling,
            FrameCount = frameCount,
            Frames = [new Av1VectorFrame { Width = width, Height = 4, Planes = [.. Enumerable.Repeat(digest ?? new string('a', 64), planes ?? (subsampling == "4:0:0" ? 1 : 3))] }],
        };

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
