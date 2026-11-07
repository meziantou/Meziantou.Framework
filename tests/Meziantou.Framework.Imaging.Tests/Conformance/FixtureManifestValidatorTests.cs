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

    [Fact]
    public void ValidHevcVectorPasses()
    {
        var file = WriteFile("hevc/a.hevc", [0, 0, 1, 2]);
        Assert.Empty(FixtureManifestValidator.Validate(CreateHevcManifest(CreateHevcVector("hevc/a", file)), _root));

        // A monochrome vector has one plane, and one value in each picture hash
        var gray = CreateHevcVector("hevc/a", file, chromaFormat: "4:0:0", pixelFormat: "gray");
        Assert.Empty(FixtureManifestValidator.Validate(CreateHevcManifest(gray), _root));

        // Two decoders that agree are enough when the third one is left out with a reason
        var twoDecoders = CreateHevcVector("hevc/a", file, agree: ["ffmpeg", "hm"], excluded: [Exclude("libde265")], verifiedBy: ["hm"]);
        Assert.Empty(FixtureManifestValidator.Validate(CreateHevcManifest(twoDecoders), _root));

        // Output in another order than the decoding order, with a hash of each type
        foreach (var (hashType, hashLength) in new[] { ("md5", 32), ("crc", 4), ("checksum", 8) })
        {
            var reordered = CreateHevcVector("hevc/a", file, hashType: hashType, hashLength: hashLength, outputOrder: "reordered", decodeOrders: [0, 2, 1],
                features: ["hevc.encoder=x265", "hevc.bitDepth=8", "hevc.chroma=4:2:0", "hevc.pictureHash=" + hashType, "hevc.frames=3", "hevc.outputOrder=reordered"]);
            Assert.Empty(FixtureManifestValidator.Validate(CreateHevcManifest(reordered), _root));
        }
    }

    [Fact]
    public void HevcVectorFilesAreHashedAndNeverOrphans()
    {
        var file = WriteFile("hevc/a.hevc", [0, 0, 1, 2]);
        WriteFile("hevc/orphan.hevc", [4]);
        var errors = FixtureManifestValidator.Validate(CreateHevcManifest(CreateHevcVector("hevc/a", file)), _root);
        Assert.Contains(errors, error => error.Contains("hevc/orphan.hevc", StringComparison.Ordinal));
        Assert.DoesNotContain(errors, error => error.Contains("hevc/a.hevc", StringComparison.Ordinal));

        File.Delete(_root / "hevc" / "orphan.hevc");
        File.WriteAllBytes(_root / "hevc" / "a.hevc", [0, 0, 1, 3]);
        errors = FixtureManifestValidator.Validate(CreateHevcManifest(CreateHevcVector("hevc/a", file)), _root);
        Assert.Contains(errors, error => error.Contains("sha256", StringComparison.Ordinal));

        var missing = CreateHevcVector("hevc/b", new FixtureFile { Path = "hevc/b.hevc", Sha256 = new string('0', 64) });
        Assert.Contains(FixtureManifestValidator.Validate(CreateHevcManifest(missing), _root), error => error.Contains("does not exist", StringComparison.Ordinal));
    }

    [Fact]
    public void HevcVectorsNeedProvenanceLicenseAndTwoDecodersThatAgree()
    {
        var file = WriteFile("hevc/a.hevc", [0, 0, 1, 2]);

        var license = CreateHevcVector("hevc/a", file, provenance: new FixtureProvenance { Origin = "generated", License = "GPL-2.0-or-later", Generator = "g", Tools = ["t"], Commands = ["c"] });
        Assert.Contains(FixtureManifestValidator.Validate(CreateHevcManifest(license), _root), error => error.Contains("GPL-2.0-or-later", StringComparison.Ordinal));

        var noCommand = CreateHevcVector("hevc/a", file, provenance: new FixtureProvenance { Origin = "generated", License = "CC0-1.0", Generator = "g", Tools = ["t"] });
        Assert.Contains(FixtureManifestValidator.Validate(CreateHevcManifest(noCommand), _root), error => error.Contains("'commands'", StringComparison.Ordinal));

        var noTools = CreateHevcVector("hevc/a", file, provenance: new FixtureProvenance { Origin = "generated", License = "CC0-1.0", Commands = ["c"] });
        var errors = FixtureManifestValidator.Validate(CreateHevcManifest(noTools), _root);
        Assert.Contains(errors, error => error.Contains("'generator'", StringComparison.Ordinal));
        Assert.Contains(errors, error => error.Contains("'tools'", StringComparison.Ordinal));

        // A section with a single decoder cannot produce a reference
        var oneDecoder = CreateHevcManifest([new HevcVectorTool { Id = "hm", Name = "HM 18.0", Command = "c" }], CreateHevcVector("hevc/a", file, agree: ["hm"], verifiedBy: ["hm"]));
        Assert.Contains(FixtureManifestValidator.Validate(oneDecoder, _root), error => error.Contains("two distinct independent decoders", StringComparison.Ordinal));
        var sameDecoderTwice = CreateHevcManifest([new HevcVectorTool { Id = "hm", Name = "HM 18.0", Command = "c" }, new HevcVectorTool { Id = "hm", Name = "HM 18.0", Command = "d" }], CreateHevcVector("hevc/a", file, agree: ["hm"], verifiedBy: ["hm"]));
        Assert.Contains(FixtureManifestValidator.Validate(sameDecoderTwice, _root), error => error.Contains("the decoders must be distinct", StringComparison.Ordinal));
        var withoutId = CreateHevcManifest([new HevcVectorTool { Name = "HM 18.0", Command = "c" }, new HevcVectorTool { Id = "ffmpeg", Name = "ffmpeg 9.0.2", Command = "d" }], CreateHevcVector("hevc/a", file, agree: ["ffmpeg"], verifiedBy: ["ffmpeg"]));
        Assert.Contains(FixtureManifestValidator.Validate(withoutId, _root), error => error.Contains("every decoder must have an id", StringComparison.Ordinal));

        // A digest from a single decoder is not a reference: the frames of such a vector have none, and the vector is one
        // the library rejects
        var single = CreateHevcVector("hevc/a", file, agree: ["hm"], excluded: [Exclude("ffmpeg"), Exclude("libde265", HevcVectorExclusion.Rejects)], verifiedBy: ["hm"]);
        AssertHevcError(single, "plane digests need two decoders that agree");
        AssertHevcError(single, "plane digests without two decoders that agree");
        var unsupported = CreateHevcVector("hevc/a", file, agree: ["hm"], excluded: [Exclude("ffmpeg"), Exclude("libde265", HevcVectorExclusion.Rejects)], verifiedBy: ["hm"], planes: 0,
            features: ["hevc.encoder=hm", "hevc.bitDepth=8", "hevc.chroma=4:2:0", "hevc.pictureHash=md5", "hevc.unsupported=extendedPrecision"]);
        Assert.Empty(FixtureManifestValidator.Validate(CreateHevcManifest(unsupported), _root));
    }

    [Fact]
    public void HevcVectorsRecordEveryDecoderOnce()
    {
        var file = WriteFile("hevc/a.hevc", [0, 0, 1, 2]);
        const string EveryDecoder = "must either agree or be excluded, once";

        // A decoder that is neither among the ones that agree nor left out, one that is both, and an unknown one
        AssertHevcError(CreateHevcVector("hevc/a", file, agree: ["ffmpeg", "hm"], verifiedBy: ["hm"]), EveryDecoder);
        AssertHevcError(CreateHevcVector("hevc/a", file, agree: ["ffmpeg", "libde265", "hm"], excluded: [Exclude("hm")]), EveryDecoder);
        AssertHevcError(CreateHevcVector("hevc/a", file, agree: ["ffmpeg", "hm"], excluded: [Exclude("other")], verifiedBy: ["hm"]), EveryDecoder);
        AssertHevcError(CreateHevcVector("hevc/a", file, agree: [], excluded: [Exclude("ffmpeg"), Exclude("libde265"), Exclude("hm")], verifiedBy: []), "no decoder decodes the stream as recorded");

        // A decoder is left out with what it does, why, and what was observed
        AssertHevcError(CreateHevcVector("hevc/a", file, agree: ["ffmpeg", "hm"], excluded: [Exclude("libde265", outcome: "skipped")], verifiedBy: ["hm"]), "needs an outcome");
        AssertHevcError(CreateHevcVector("hevc/a", file, agree: ["ffmpeg", "hm"], excluded: [Exclude("libde265", reason: " ")], verifiedBy: ["hm"]), "needs an outcome");
        AssertHevcError(CreateHevcVector("hevc/a", file, agree: ["ffmpeg", "hm"], excluded: [Exclude("libde265", observed: "")], verifiedBy: ["hm"]), "needs an outcome");

        // The hashes of the stream are checked by a decoder that agrees
        const string Verified = "the picture hashes must be verified by at least one of the decoders that agree";
        AssertHevcError(CreateHevcVector("hevc/a", file, verifiedBy: []), Verified);
        AssertHevcError(CreateHevcVector("hevc/a", file, agree: ["ffmpeg", "hm"], excluded: [Exclude("libde265")], verifiedBy: ["libde265"]), Verified);
        AssertHevcError(CreateHevcVector("hevc/a", file, verifiedBy: ["hm", "hm"]), Verified);
    }

    [Fact]
    public void HevcVectorShapeIsEnforced()
    {
        var file = WriteFile("hevc/a.hevc", [0, 0, 1, 2]);
        AssertHevcError(CreateHevcVector("hevc/a", file, bitDepth: 9), "invalid bitDepth 9");
        AssertHevcError(CreateHevcVector("hevc/a", file, bitDepthChroma: 8), "invalid bitDepth 8 or bitDepthChroma");
        AssertHevcError(CreateHevcVector("hevc/a", file, chromaFormat: "4:1:1"), "invalid chromaFormat '4:1:1'");
        AssertHevcError(CreateHevcVector("hevc/a", file, chromaFormat: "4:0:0", pixelFormat: "gray", planes: 3), "3 plane digests, 1 expected");
        AssertHevcError(CreateHevcVector("hevc/a", file, planes: 1), "1 plane digests, 3 expected");
        AssertHevcError(CreateHevcVector("hevc/a", file, frameCount: 2), "frameCount 2 does not match the 1 frames");
        AssertHevcError(CreateHevcVector("hevc/a", file, pictureCount: 0), "pictureCount 0 is less than the 1 output pictures");
        AssertHevcError(CreateHevcVector("hevc/a", file, digest: new string('A', 64)), "plane digests are SHA-256 hashes");
        AssertHevcError(CreateHevcVector("hevc/a", file, digest: "abc"), "plane digests are SHA-256 hashes");
        AssertHevcError(CreateHevcVector("hevc/a", file, width: 0), "dimensions must be positive");
        AssertHevcError(CreateHevcVector("hevc/a", file, features: []), "features must be present");
        AssertHevcError(CreateHevcVector("other/a", file), "invalid id");
        AssertHevcError(CreateHevcVector("hevc/A", file), "invalid id");
        AssertHevcError(CreateHevcVector("hevc/a", file, outputOrder: "display"), "invalid outputOrder 'display'");

        // The picture hash: a known type, one value per colour component, of the length of the type
        AssertHevcError(CreateHevcVector("hevc/a", file, hashType: "sha1"), "invalid picture hash type 'sha1'");
        AssertHevcError(CreateHevcVector("hevc/a", file, hashLength: 30), "the picture hash has one lowercase hexadecimal value of 32 characters");
        AssertHevcError(CreateHevcVector("hevc/a", file, hashType: "crc", hashLength: 32), "the picture hash has one lowercase hexadecimal value of 4 characters");
        AssertHevcError(CreateHevcVector("hevc/a", file, hashComponents: 1), "per colour component (3 for 4:2:0)");

        // The frames are in output order and name the picture they are in decoding order
        AssertHevcError(CreateHevcVector("hevc/a", file, decodeOrders: [0, 0], features: ["hevc.encoder=x265", "hevc.bitDepth=8", "hevc.chroma=4:2:0", "hevc.pictureHash=md5", "hevc.frames=2"]), "decodeOrder must be the index of one coded picture");
        AssertHevcError(CreateHevcVector("hevc/a", file, decodeOrders: [5]), "decodeOrder must be the index of one coded picture");
        AssertHevcError(CreateHevcVector("hevc/a", file, decodeOrders: [1, 0], features: ["hevc.encoder=x265", "hevc.bitDepth=8", "hevc.chroma=4:2:0", "hevc.pictureHash=md5", "hevc.frames=2"]), "outputOrder 'decoding' does not match the decoding order of the frames");
        AssertHevcError(CreateHevcVector("hevc/a", file, outputOrder: "reordered", features: ["hevc.encoder=x265", "hevc.bitDepth=8", "hevc.chroma=4:2:0", "hevc.pictureHash=md5", "hevc.outputOrder=reordered"]), "outputOrder 'reordered' does not match the decoding order of the frames");

        var elsewhere = WriteFile("png/a.hevc", [1]);
        AssertHevcError(CreateHevcVector("hevc/a", elsewhere), "must be a raw byte stream 'hevc/*.hevc'");
        var extension = WriteFile("hevc/a.h265", [1]);
        AssertHevcError(CreateHevcVector("hevc/a", extension), "must be a raw byte stream 'hevc/*.hevc'");
    }

    [Fact]
    public void HevcVectorFeaturesUseTheDocumentedVocabulary()
    {
        var file = WriteFile("hevc/a.hevc", [0, 0, 1, 2]);
        string[] required = ["hevc.encoder=x265", "hevc.bitDepth=8", "hevc.chroma=4:2:0", "hevc.pictureHash=md5"];

        // Unknown keys, and unknown values of the keys that are enumerations (one by one in a list of values)
        AssertHevcError(CreateHevcVector("hevc/a", file, features: [.. required, "hevc.filmGrain"]), "unknown feature key 'hevc.filmGrain'");
        AssertHevcError(CreateHevcVector("hevc/a", file, features: [.. required, "av1.lossless"]), "unknown feature key 'av1.lossless'");
        AssertHevcError(CreateHevcVector("hevc/a", file, features: [.. required, "hevc.nal=IDR_N_LP,KEY"]), "invalid value 'KEY' of the feature 'hevc.nal'");
        AssertHevcError(CreateHevcVector("hevc/a", file, features: [.. required, "hevc.sao=band"]), "invalid value 'band' of the feature 'hevc.sao'");
        AssertHevcError(CreateHevcVector("hevc/a", file, features: ["hevc.encoder=libx265", "hevc.bitDepth=8", "hevc.chroma=4:2:0", "hevc.pictureHash=md5"]), "invalid value 'libx265' of the feature 'hevc.encoder'");
        Assert.Empty(FixtureManifestValidator.Validate(CreateHevcManifest(CreateHevcVector("hevc/a", file, features: [.. required, "hevc.nal=IDR_N_LP,TRAIL_R", "hevc.sliceQp=-12,30", "hevc.wpp"])), _root));

        // The features say what the other fields of the entry say
        AssertHevcError(CreateHevcVector("hevc/a", file, features: ["hevc.encoder=x265", "hevc.bitDepth=10", "hevc.chroma=4:2:0", "hevc.pictureHash=md5"]), "the feature 'hevc.bitDepth' must be 'hevc.bitDepth=8'");
        AssertHevcError(CreateHevcVector("hevc/a", file, features: ["hevc.encoder=x265", "hevc.bitDepth=8,10", "hevc.chroma=4:2:0", "hevc.pictureHash=md5"]), "the feature 'hevc.bitDepth' must be 'hevc.bitDepth=8'");
        AssertHevcError(CreateHevcVector("hevc/a", file, features: ["hevc.encoder=x265", "hevc.bitDepth=8", "hevc.chroma=4:4:4", "hevc.pictureHash=md5"]), "the feature 'hevc.chroma' must be 'hevc.chroma=4:2:0'");
        AssertHevcError(CreateHevcVector("hevc/a", file, features: ["hevc.encoder=x265", "hevc.bitDepth=8", "hevc.chroma=4:2:0", "hevc.pictureHash=crc"]), "the feature 'hevc.pictureHash' must be 'hevc.pictureHash=md5'");
        AssertHevcError(CreateHevcVector("hevc/a", file, features: ["hevc.encoder=x265", "hevc.bitDepth=8", "hevc.chroma=4:2:0"]), "the feature 'hevc.pictureHash' must be 'hevc.pictureHash=md5'");
        AssertHevcError(CreateHevcVector("hevc/a", file, features: ["hevc.bitDepth=8", "hevc.chroma=4:2:0", "hevc.pictureHash=md5"]), "the feature 'hevc.encoder' is required");
        AssertHevcError(CreateHevcVector("hevc/a", file, features: [.. required, "hevc.frames=2"]), "the feature 'hevc.frames' must be absent");
        AssertHevcError(CreateHevcVector("hevc/a", file, decodeOrders: [0, 1], features: required), "the feature 'hevc.frames' must be 'hevc.frames=2'");
        AssertHevcError(CreateHevcVector("hevc/a", file, decodeOrders: [0, 1], features: [.. required, "hevc.frames=3"]), "the feature 'hevc.frames' must be 'hevc.frames=2'");
        AssertHevcError(CreateHevcVector("hevc/a", file, features: [.. required, "hevc.outputOrder=reordered"]), "the feature 'hevc.outputOrder' must be absent");
        AssertHevcError(CreateHevcVector("hevc/a", file, features: [.. required, "hevc.matrix=identity"]), "the feature 'hevc.matrix' must be absent");
        AssertHevcError(CreateHevcVector("hevc/a", file, chromaFormat: "4:4:4", pixelFormat: "gbrp", features: ["hevc.encoder=x265", "hevc.bitDepth=8", "hevc.chroma=4:4:4", "hevc.pictureHash=md5"]), "the feature 'hevc.matrix' must be 'hevc.matrix=identity'");
        AssertHevcError(CreateHevcVector("hevc/a", file, bitDepth: 10, bitDepthChroma: 8, features: ["hevc.encoder=hm", "hevc.bitDepth=10", "hevc.chroma=4:2:0", "hevc.pictureHash=md5"]), "the feature 'hevc.bitDepthChroma' must be 'hevc.bitDepthChroma=8'");
        AssertHevcError(CreateHevcVector("hevc/a", file, features: [.. required, "hevc.bitDepthChroma=10"]), "the feature 'hevc.bitDepthChroma' must be absent");
    }

    [Fact]
    public void HevcVectorsAreNotFixtures()
    {
        var file = WriteFile("hevc/a.hevc", [0, 0, 1, 2]);
        var other = WriteFile("hevc/b.hevc", [4]);

        // The identifiers of the vectors and of the fixtures share one namespace, and a file belongs to one vector
        var duplicate = CreateHevcManifest(CreateHevcVector("hevc/a", file), CreateHevcVector("hevc/a", other));
        Assert.Contains(FixtureManifestValidator.Validate(duplicate, _root), error => error.Contains("duplicate id", StringComparison.Ordinal));
        var sameFile = CreateHevcManifest(CreateHevcVector("hevc/a", file), CreateHevcVector("hevc/b", file));
        Assert.Contains(FixtureManifestValidator.Validate(sameFile, _root), error => error.Contains("already used by another entry", StringComparison.Ordinal));

        // A vector has no pixel reference: the generic suites must never find it among the fixtures
        var asFixture = CreateManifest(CreateEntry("hevc/a", file));
        Assert.Contains(FixtureManifestValidator.Validate(asFixture, _root), error => error.Contains("belong to 'hevcVectors'", StringComparison.Ordinal));
        var fixtureOnVectorFile = CreateManifest(CreateEntry("invalid/png/a", other));
        Assert.Contains(FixtureManifestValidator.Validate(fixtureOnVectorFile, _root), error => error.Contains("belong to 'hevcVectors'", StringComparison.Ordinal));
    }

    [Fact]
    public void HevcVectorSectionRoundTripsThroughTheLoader()
    {
        var path = _root / "manifest.json";
        File.WriteAllText(path, """
            {
              "schemaVersion": 2,
              "fixtures": [],
              "hevcVectors": {
                "generator": { "script": "s", "command": "c", "tools": ["t"] },
                "headerParser": { "name": "p", "command": "c" },
                "decoders": [{ "id": "ffmpeg", "name": "a", "command": "c" }, { "id": "libde265", "name": "b", "command": "c" }, { "id": "hm", "name": "c", "command": "c" }],
                "planeDigest": "d",
                "pictureHash": "h",
                "entries": [{
                  "id": "hevc/a",
                  "input": { "path": "hevc/a.hevc", "sha256": "0000000000000000000000000000000000000000000000000000000000000000", "role": "encoded" },
                  "provenance": { "origin": "generated", "license": "CC0-1.0" },
                  "features": ["hevc.bitDepth=10", "hevc.nal=CRA,RASL_N,TRAIL_R", "hevc.tiles.spacing=uniform", "hevc.wpp"],
                  "pixelFormat": "yuv420p-y10-c8",
                  "bitDepth": 10,
                  "bitDepthChroma": 8,
                  "chromaFormat": "4:2:0",
                  "pictureCount": 3,
                  "frameCount": 1,
                  "outputOrder": "decoding",
                  "decoders": { "agree": ["libde265", "hm"], "excluded": [{ "decoder": "ffmpeg", "outcome": "rejects", "reason": "r", "observed": "o" }] },
                  "pictureHash": { "type": "crc", "verifiedBy": ["hm"] },
                  "frames": [{ "width": 3, "height": 2, "poc": 4, "decodeOrder": 1, "planes": ["1111111111111111111111111111111111111111111111111111111111111111"], "hash": ["12ab", "34cd", "56ef"] }]
                }]
              }
            }
            """);
        var vector = Assert.Single(FixtureManifestLoader.Load(path).HevcVectors!.Entries);
        Assert.Equal(("hevc/a", "yuv420p-y10-c8", 10, 8, "4:2:0", 3, 1, 3), (vector.Id, vector.PixelFormat, vector.BitDepth, vector.BitDepthChroma, vector.ChromaFormat, vector.PictureCount, vector.FrameCount, vector.PlaneCount));
        Assert.Equal((3, 2, 4, 1), (vector.Frames[0].Width, vector.Frames[0].Height, vector.Frames[0].Poc, vector.Frames[0].DecodeOrder));
        Assert.Equal(["12ab", "34cd", "56ef"], vector.Frames[0].Hash);
        Assert.Equal((HevcVectorPictureHash.Crc, 4), (vector.PictureHash.Type, vector.PictureHash.HexLength));
        Assert.True(vector.HasDigests);
        Assert.Equal(["libde265", "hm"], vector.Decoders.Agree);
        Assert.Equal(("ffmpeg", HevcVectorExclusion.Rejects, "r", "o"), (vector.Decoders.Excluded[0].Decoder, vector.Decoders.Excluded[0].Outcome, vector.Decoders.Excluded[0].Reason, vector.Decoders.Excluded[0].Observed));

        // A value of a feature is one of its comma-separated values, and a key is whole
        Assert.True(vector.HasFeature("hevc.nal=RASL_N"));
        Assert.False(vector.HasFeature("hevc.nal=RASL"));
        Assert.True(vector.HasFeature("hevc.wpp"));
        Assert.False(vector.HasFeature("hevc.bitDepth"));
        Assert.True(vector.HasFeatureKey("hevc.bitDepth"));
        Assert.False(vector.HasFeatureKey("hevc.tiles"));
        Assert.True(vector.HasFeatureKey("hevc.tiles.spacing"));
        Assert.Equal(["CRA", "RASL_N", "TRAIL_R"], vector.GetFeatureValues("hevc.nal"));
        Assert.Empty(vector.GetFeatureValues("hevc.wpp"));

        // Unknown properties are rejected in the section too
        File.WriteAllText(path, """{ "schemaVersion": 2, "fixtures": [], "hevcVectors": { "generator": { "script": "s", "command": "c", "tools": [] }, "headerParser": { "name": "p", "command": "c" }, "decoders": [], "planeDigest": "d", "pictureHash": "h", "entries": [], "unexpected": true } }""");
        Assert.Throws<System.Text.Json.JsonException>(() => FixtureManifestLoader.Load(path));
    }

    private void AssertHevcError(HevcVectorEntry vector, string expected)
        => Assert.Contains(FixtureManifestValidator.Validate(CreateHevcManifest(vector), _root), error => error.Contains(expected, StringComparison.Ordinal));

    private static HevcVectorExclusion Exclude(string decoder, string outcome = HevcVectorExclusion.Differs, string reason = "A known behavior of this decoder.", string observed = "outputs other samples")
        => new() { Decoder = decoder, Outcome = outcome, Reason = reason, Observed = observed };

    private static FixtureManifest CreateHevcManifest(params HevcVectorEntry[] vectors)
        => CreateHevcManifest(
            [
                new HevcVectorTool { Id = "ffmpeg", Name = "ffmpeg 9.0.2 (hevc)", Command = "ffmpeg -c:v hevc" },
                new HevcVectorTool { Id = "libde265", Name = "libde265 1.1.3 (dec265)", Command = "dec265" },
                new HevcVectorTool { Id = "hm", Name = "HM 18.0 (TAppDecoder)", Command = "TAppDecoder" },
            ], vectors);

    private static FixtureManifest CreateHevcManifest(HevcVectorTool[] decoders, params HevcVectorEntry[] vectors) => new()
    {
        SchemaVersion = FixtureManifest.CurrentSchemaVersion,
        Fixtures = [],
        HevcVectors = new HevcVectorSet
        {
            Generator = new FixtureGeneratorInfo { Script = "tools/generator.cs", Command = "generator hevc", Tools = ["ffmpeg 9.0.2"] },
            HeaderParser = new HevcVectorTool { Name = "ffmpeg 9.0.2 (trace_headers)", Command = "ffmpeg -bsf:v trace_headers" },
            Decoders = decoders,
            PlaneDigest = "SHA-256 of the samples of one plane.",
            PictureHash = "The decoded picture hash SEI message of each picture.",
            Entries = vectors,
        },
    };

    /// <summary>A vector of one frame per decode order given (one by default), in the order given.</summary>
    private static HevcVectorEntry CreateHevcVector(string id, FixtureFile input, FixtureProvenance? provenance = null, int bitDepth = 8, int? bitDepthChroma = null, string chromaFormat = "4:2:0", string pixelFormat = "yuv420p",
        int? planes = null, int? frameCount = null, int? pictureCount = null, string? digest = null, int width = 4, string[]? features = null, string[]? agree = null, HevcVectorExclusion[]? excluded = null, string[]? verifiedBy = null,
        string hashType = "md5", int? hashLength = null, int? hashComponents = null, string outputOrder = "decoding", int[]? decodeOrders = null)
    {
        var planeCount = chromaFormat == "4:0:0" ? 1 : 3;
        decodeOrders ??= [0];
        var length = hashLength ?? (hashType == "crc" ? 4 : hashType == "checksum" ? 8 : 32);
        return new()
        {
            Id = id,
            Input = input,
            Provenance = provenance ?? new FixtureProvenance { Origin = "generated", License = "CC0-1.0", Generator = "tools/generator.cs (Vectors)", Tools = ["ffmpeg 9.0.2", "x265 4.2"], Commands = ["ffmpeg -f hevc {input}"] },
            Features = features ?? ["hevc.encoder=x265", $"hevc.bitDepth={bitDepth}", "hevc.chroma=" + chromaFormat, "hevc.pictureHash=" + hashType, .. decodeOrders.Length > 1 ? new[] { $"hevc.frames={decodeOrders.Length}" } : []],
            PixelFormat = pixelFormat,
            BitDepth = bitDepth,
            BitDepthChroma = bitDepthChroma,
            ChromaFormat = chromaFormat,
            PictureCount = pictureCount ?? decodeOrders.Length,
            FrameCount = frameCount ?? decodeOrders.Length,
            OutputOrder = outputOrder,
            Decoders = new HevcVectorAgreement { Agree = agree ?? ["ffmpeg", "libde265", "hm"], Excluded = excluded ?? [] },
            PictureHash = new HevcVectorPictureHash { Type = hashType, VerifiedBy = verifiedBy ?? ["libde265", "hm"] },
            Frames =
            [
                .. decodeOrders.Select((decodeOrder, index) => new HevcVectorFrame
                {
                    Width = width,
                    Height = 4,
                    Poc = index,
                    DecodeOrder = decodeOrder,
                    Planes = [.. Enumerable.Repeat(digest ?? new string('a', 64), planes ?? planeCount)],
                    Hash = [.. Enumerable.Repeat(new string('b', length), hashComponents ?? planeCount)],
                }),
            ],
        };
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

    [Fact]
    public void FontsAreValidated()
    {
        var file = WriteFile("fonts/a.ttf", [0, 1, 0, 0]);
        var woff = WriteFile("fonts/a.woff", [(byte)'w', (byte)'O', (byte)'F', (byte)'F']);
        var vectors = WriteFile("fonts/vectors/a.json", [(byte)'{', (byte)'}']);
        FontEntry Font(string id = "fonts/a", string license = "OFL-1.1", string flavor = "ttf", string modification = "none", string? reserved = null, string? original = null, int faceCount = 1, FixtureFile? fontFile = null, string[]? features = null) => new()
        {
            Id = id,
            File = fontFile ?? file,
            Provenance = new FixtureProvenance { Origin = "external", License = license, Source = "https://example.com/a.ttf" },
            Flavor = flavor,
            FaceCount = faceCount,
            Modification = modification,
            ReservedFontName = reserved,
            Original = original,
            Features = features ?? ["font.table=glyf"],
            Vectors = fontFile is null ? vectors : null,
        };

        IReadOnlyList<string> Validate(params FontEntry[] fonts)
            => FixtureManifestValidator.Validate(new FixtureManifest { SchemaVersion = FixtureManifest.CurrentSchemaVersion, Fixtures = [], Fonts = fonts }, _root);

        Assert.Empty(Validate(Font(), Font(id: "fonts/a-woff", flavor: "woff", modification: "container", original: "fonts/a", fontFile: woff)));
        Assert.Single(Validate(Font(reserved: "Example"), Font(id: "fonts/a-woff", flavor: "woff", modification: "container", original: "fonts/a", fontFile: woff)));

        // Without the section, the files are orphans: every committed file needs provenance and a license
        Assert.Equal(3, FixtureManifestValidator.Validate(CreateManifest(), _root).Count(error => error.Contains("is not referenced by the manifest", StringComparison.Ordinal)));

        void AssertError(string expected, params FontEntry[] fonts)
            => Assert.Contains(Validate(fonts), error => error.Contains(expected, StringComparison.Ordinal));

        AssertError("license 'GPL-3.0' is not in the allowed list", Font(license: "GPL-3.0"));
        AssertError("duplicate id", Font(), Font());
        AssertError("invalid id", Font(id: "Fonts/A"));
        AssertError("invalid flavor 'eot'", Font(flavor: "eot"));
        AssertError("invalid modification 'edited'", Font(modification: "edited"));
        AssertError("faceCount must be 1, or more for a collection", Font(faceCount: 0));
        AssertError("faceCount must be 1, or more for a collection", Font(faceCount: 2));
        AssertError("features must be non-empty and unique", Font(features: ["font.table=glyf", "font.table=glyf"]));

        // A font with a Reserved Font Name is only stored as published
        AssertError("with the Reserved Font Name 'Example' must be stored unmodified", Font(modification: "subset", reserved: "Example"));
        AssertError("with the Reserved Font Name 'Example' must be stored unmodified", Font(), Font(id: "fonts/a-woff", flavor: "woff", modification: "container", original: "fonts/a", reserved: "Example", fontFile: woff));
        AssertError("has the Reserved Font Name 'Example' and must only be stored unmodified", Font(reserved: "Example"), Font(id: "fonts/a-woff", flavor: "woff", modification: "container", original: "fonts/a", fontFile: woff));

        AssertError("must name the font it comes from", Font(), Font(id: "fonts/a-woff", flavor: "woff", modification: "container", fontFile: woff));
        AssertError("must name the font it comes from", Font(), Font(id: "fonts/a-woff", flavor: "woff", modification: "container", original: "fonts/missing", fontFile: woff));
        AssertError("must name the font it comes from", Font(id: "fonts/a-woff", flavor: "woff", modification: "container", original: "fonts/a-woff", fontFile: woff));
        AssertError("'original' is only for a font in another container", Font(), Font(id: "fonts/b", original: "fonts/a", fontFile: woff));

        // The font license is not accepted for anything else than a font
        var image = WriteFile("a.png", [1]);
        Assert.Contains(
            FixtureManifestValidator.Validate(CreateManifest(CreateEntry("a", image, new FixtureProvenance { Origin = "hand-authored", License = "OFL-1.1" })), _root),
            error => error.Contains("the license 'OFL-1.1' is only accepted for font files", StringComparison.Ordinal));

        // A changed font no longer matches its hash
        File.WriteAllBytes(_root / "fonts" / "a.ttf", [0, 1, 0, 1]);
        AssertError("has sha256", Font());
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
