using System.Buffers.Binary;
using System.IO.Compression;
using System.Text.RegularExpressions;
using Meziantou.Framework.SnapshotTesting.MergeTools;

namespace Meziantou.Framework.SnapshotTesting.Tests;

public sealed class SnapshotTests
{
    private const string SnapshotUpdateStrategyEnvironmentVariableName = "SNAPSHOTTESTING_STRATEGY";

    [Fact]
    public void Validate_CreateSnapshotFile()
    {
        using var directory = TemporaryDirectory.Create();
        var settings = new SnapshotSettings()
        {
            AutoDetectContinuousEnvironment = false,
            SnapshotUpdateStrategy = SnapshotUpdateStrategy.OverwriteWithoutFailure,
            AssertionExceptionCreator = new FixedAssertionExceptionBuilder(),
            SnapshotPathStrategy = context => directory / ("snapshot_" + context.Index.ToString(CultureInfo.InvariantCulture) + ".verified." + (context.Extension ?? "txt")),
        };

        Snapshot.Validate(new { A = 1 }, settings);

        var files = Directory.GetFiles(directory.FullPath);
        Assert.Single(files);
        Assert.Contains("A: 1", File.ReadAllText(files[0]), StringComparison.Ordinal);
    }

    [Fact]
    public void Validate_FailsWhenSnapshotFileSetChanged()
    {
        using var directory = TemporaryDirectory.Create();
        var baseSettings = new SnapshotSettings()
        {
            AutoDetectContinuousEnvironment = false,
            AssertionExceptionCreator = new FixedAssertionExceptionBuilder(),
            SnapshotPathStrategy = context => directory / ("snapshot_fixed_" + context.Index.ToString(CultureInfo.InvariantCulture) + ".verified." + (context.Extension ?? "txt")),
            SnapshotUpdateStrategy = SnapshotUpdateStrategy.OverwriteWithoutFailure,
        };

        ValidateWithSerializerCount(baseSettings, 3);

        var validateSettings = baseSettings with
        {
            SnapshotUpdateStrategy = SnapshotUpdateStrategy.Disallow,
        };

        var exception = Assert.Throws<SnapshotAssertionException>(() => ValidateWithSerializerCount(validateSettings, 2));
        Assert.Contains("Unexpected snapshot files:", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Settings_WithDeepClone()
    {
        var original = new SnapshotSettings();
        original.Serializers.Add(new FixedCountSerializer(count: 1));
        original.Comparers.Set(SnapshotType.Create("dummy"), ByteArraySnapshotComparer.Instance);
        original.ScrubLinesContaining(StringComparison.OrdinalIgnoreCase, "line2");
        var originalSerializerCount = original.Serializers.Count;

        var clone = original.Clone();
        clone.Serializers.Add(new FixedCountSerializer(count: 2));

        Assert.NotSame(original.Serializers, clone.Serializers);
        Assert.NotSame(original.Comparers, clone.Comparers);
        Assert.NotSame(original.Scrubbers, clone.Scrubbers);
        Assert.Equal(original.Scrubbers, clone.Scrubbers);
        Assert.Equal(originalSerializerCount + 1, clone.Serializers.Count);
        Assert.Equal(originalSerializerCount, original.Serializers.Count);
    }

    [Fact]
    public void ResolveSourceFilePath_ThrowsWhenSourceFilePathIsNotFound()
    {
        var sourceFilePath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "file.cs");

        var exception = Assert.Throws<SnapshotException>(() => SnapshotCallerContext.ResolveSourceFilePath(sourceFilePath));
        Assert.Contains(sourceFilePath, exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ResolveSourceFilePath_UsesRegisteredSourceRootMapping()
    {
        using var directory = TemporaryDirectory.Create();
        var sourceFilePath = directory.GetFullPath("sub/file.cs");
        sourceFilePath.CreateParentDirectory();
        File.WriteAllText(sourceFilePath, "class C {}");

        var sourceRoot = directory.FullPath.Value.Replace('\\', '/');
        Snapshot.RegisterSourceRootMapping("/_snapshot_tests_/", sourceRoot + "/");

        var resolvedPath = SnapshotCallerContext.ResolveSourceFilePath("/_snapshot_tests_/sub/file.cs");

        Assert.Equal(sourceFilePath, resolvedPath);
    }

    [Fact]
    public void SnapshotPathStrategy_UsesIndexPatternForShortName()
    {
        var settings = new SnapshotSettings();
        var context = new SnapshotPathContext(
            SourceFilePath: FullPath.FromPath("C:\\temp\\snapshot-tests.cs"),
            ClassName: null,
            MethodName: "MethodName",
            LineNumber: 42,
            Type: SnapshotType.Default,
            Index: 2,
            Extension: "png",
            TestContext: new SnapshotTestContext(TestName: "Image snapshot"),
            Settings: settings,
            SnapshotCount: 3);

        var path = settings.SnapshotPathStrategy(context);
        Assert.Matches(new Regex("^[A-Za-z0-9._-]+_2\\.verified\\.png$", RegexOptions.CultureInvariant, matchTimeout: TimeSpan.FromSeconds(1)), path.Name);
        Assert.DoesNotMatch(new Regex("_[0-9a-f]{8}_2\\.verified\\.png$", RegexOptions.CultureInvariant, matchTimeout: TimeSpan.FromSeconds(1)), path.Name);
        Assert.True(path.Name.Length <= settings.MaxSnapshotFileNameLength);
    }

    [Fact]
    public void SnapshotPathStrategy_OmitsIndex_WhenSingleSnapshot()
    {
        var settings = new SnapshotSettings();
        var context = new SnapshotPathContext(
            SourceFilePath: FullPath.FromPath("C:\\temp\\snapshot-tests.cs"),
            ClassName: null,
            MethodName: "MethodName",
            LineNumber: 42,
            Type: SnapshotType.Default,
            Index: 0,
            Extension: "png",
            TestContext: new SnapshotTestContext(TestName: "Image snapshot"),
            Settings: settings,
            SnapshotCount: 1);

        var path = settings.SnapshotPathStrategy(context);
        Assert.Matches(new Regex("^[A-Za-z0-9._-]+\\.verified\\.png$", RegexOptions.CultureInvariant, matchTimeout: TimeSpan.FromSeconds(1)), path.Name);
        Assert.DoesNotContain("_0", path.Name, StringComparison.Ordinal);
        Assert.True(path.Name.Length <= settings.MaxSnapshotFileNameLength);
    }

    [Fact]
    public void SnapshotPathStrategy_PrefixesClassName_WhenAvailable()
    {
        var settings = new SnapshotSettings
        {
            SnapshotNamingStrategy = SnapshotNamingStrategies.ClassName_TestName,
        };
        var context = new SnapshotPathContext(
            SourceFilePath: FullPath.FromPath("C:\\temp\\snapshot-tests.cs"),
            ClassName: "UnitTestClass1",
            MethodName: "MethodName",
            LineNumber: 42,
            Type: SnapshotType.Default,
            Index: 0,
            Extension: "png",
            TestContext: new SnapshotTestContext(TestName: "MethodName"),
            Settings: settings,
            SnapshotCount: 1);

        var path = settings.SnapshotPathStrategy(context);
        Assert.Equal("UnitTestClass1_MethodName.verified.png", path.Name);
    }

    [Fact]
    public void SnapshotPathStrategy_DoesNotDuplicateClassName_WhenNameIsClassQualified()
    {
        var settings = new SnapshotSettings
        {
            SnapshotNamingStrategy = SnapshotNamingStrategies.ClassName_TestName,
        };
        var context = new SnapshotPathContext(
            SourceFilePath: FullPath.FromPath("C:\\temp\\snapshot-tests.cs"),
            ClassName: "UnitTestClass1",
            MethodName: "MethodName",
            LineNumber: 42,
            Type: SnapshotType.Default,
            Index: 0,
            Extension: "png",
            TestContext: new SnapshotTestContext(TestName: "MyNamespace.UnitTestClass1.MethodName"),
            Settings: settings,
            SnapshotCount: 1);

        var path = settings.SnapshotPathStrategy(context);
        Assert.Equal("MyNamespace.UnitTestClass1.MethodName.verified.png", path.Name);
    }

    [Fact]
    public void SnapshotPathStrategy_DefaultsToClassNameTestNameStrategy()
    {
        var settings = new SnapshotSettings();
        var context = new SnapshotPathContext(
            SourceFilePath: FullPath.FromPath("C:\\temp\\snapshot-tests.cs"),
            ClassName: "UnitTestClass1",
            MethodName: "MethodName",
            LineNumber: 42,
            Type: SnapshotType.Default,
            Index: 0,
            Extension: "png",
            TestContext: new SnapshotTestContext(TestName: "Case_alpha"),
            Settings: settings,
            SnapshotCount: 1);

        var path = settings.SnapshotPathStrategy(context);
        Assert.Equal("UnitTestClass1_Case_alpha.verified.png", path.Name);
    }

    [Fact]
    public void SnapshotPathStrategy_UsesTestNameStrategy()
    {
        var settings = new SnapshotSettings
        {
            SnapshotNamingStrategy = SnapshotNamingStrategies.TestName,
        };
        var context = new SnapshotPathContext(
            SourceFilePath: FullPath.FromPath("C:\\temp\\snapshot-tests.cs"),
            ClassName: "UnitTestClass1",
            MethodName: "MethodName",
            LineNumber: 42,
            Type: SnapshotType.Default,
            Index: 0,
            Extension: "png",
            TestContext: new SnapshotTestContext(TestName: "Case_alpha"),
            Settings: settings,
            SnapshotCount: 1);

        var path = settings.SnapshotPathStrategy(context);
        Assert.Equal("Case_alpha.verified.png", path.Name);
    }

    [Fact]
    public void SnapshotPathStrategy_UsesFullNameStrategy()
    {
        var settings = new SnapshotSettings
        {
            SnapshotNamingStrategy = SnapshotNamingStrategies.FullName,
        };
        var context = new SnapshotPathContext(
            SourceFilePath: FullPath.FromPath("C:\\temp\\snapshot-tests.cs"),
            ClassName: "UnitTestClass1",
            MethodName: "MethodName",
            LineNumber: 42,
            Type: SnapshotType.Default,
            Index: 0,
            Extension: "png",
            TestContext: null,
            Settings: settings,
            SnapshotCount: 1);

        var path = settings.SnapshotPathStrategy(context);
        Assert.Equal("UnitTestClass1.MethodName.verified.png", path.Name);
    }

    [Fact]
    public void SnapshotPathStrategy_FullNameStrategy_DoesNotDuplicateClassName_WhenNameIsClassQualified()
    {
        var settings = new SnapshotSettings
        {
            SnapshotNamingStrategy = SnapshotNamingStrategies.FullName,
        };
        var context = new SnapshotPathContext(
            SourceFilePath: FullPath.FromPath("C:\\temp\\snapshot-tests.cs"),
            ClassName: "UnitTestClass1",
            MethodName: "MethodName",
            LineNumber: 42,
            Type: SnapshotType.Default,
            Index: 0,
            Extension: "png",
            TestContext: new SnapshotTestContext(TestName: "MyNamespace.UnitTestClass1.MethodName"),
            Settings: settings,
            SnapshotCount: 1);

        var path = settings.SnapshotPathStrategy(context);
        Assert.Equal("MyNamespace.UnitTestClass1.MethodName.verified.png", path.Name);
    }

    [Fact]
    public void SnapshotPathStrategy_UsesHashAndIndexPatternForLongName()
    {
        var settings = new SnapshotSettings();
        var context = new SnapshotPathContext(
            SourceFilePath: FullPath.FromPath("C:\\temp\\snapshot-tests.cs"),
            ClassName: null,
            MethodName: new string('a', 120),
            LineNumber: 42,
            Type: SnapshotType.Default,
            Index: 2,
            Extension: "png",
            TestContext: null,
            Settings: settings,
            SnapshotCount: 3);

        var path = settings.SnapshotPathStrategy(context);
        Assert.Matches(new Regex("^[A-Za-z0-9._-]+_[0-9a-f]{8}_2\\.verified\\.png$", RegexOptions.CultureInvariant, matchTimeout: TimeSpan.FromSeconds(1)), path.Name);
        Assert.True(path.Name.Length <= settings.MaxSnapshotFileNameLength);
    }

    [Theory]
    [InlineData("snapshot.verified")]
    [InlineData("snapshot.actual")]
    public void SnapshotPathStrategy_UsesHashAndIndexPatternForReservedNames(string testName)
    {
        var settings = new SnapshotSettings();
        var context = new SnapshotPathContext(
            SourceFilePath: FullPath.FromPath("C:\\temp\\snapshot-tests.cs"),
            ClassName: null,
            MethodName: "MethodName",
            LineNumber: 42,
            Type: SnapshotType.Default,
            Index: 2,
            Extension: "png",
            TestContext: new SnapshotTestContext(TestName: testName),
            Settings: settings,
            SnapshotCount: 3);

        var path = settings.SnapshotPathStrategy(context);
        Assert.Matches(new Regex("^[A-Za-z0-9._-]+_[0-9a-f]{8}_2\\.verified\\.png$", RegexOptions.CultureInvariant, matchTimeout: TimeSpan.FromSeconds(1)), path.Name);
        Assert.True(path.Name.Length <= settings.MaxSnapshotFileNameLength);
    }

    [Fact]
    public void Validate_UsesSnapshotTypeExtension()
    {
        using var directory = TemporaryDirectory.Create();
        var snapshotType = SnapshotType.Png;
        var settings = new SnapshotSettings()
        {
            AutoDetectContinuousEnvironment = false,
            SnapshotUpdateStrategy = SnapshotUpdateStrategy.OverwriteWithoutFailure,
            AssertionExceptionCreator = new FixedAssertionExceptionBuilder(),
            SnapshotPathStrategy = context => directory / (context.Type.Type + "_" + context.Index.ToString(CultureInfo.InvariantCulture) + ".verified." + context.Extension),
        };

        settings.Serializers.Add(new SnapshotTypeSerializer());
        Snapshot.Validate("sample", snapshotType, settings);

        var filePath = directory / "png_0.verified.png";
        Assert.True(File.Exists(filePath));
        Assert.Equal("png", File.ReadAllText(filePath));
    }

    [Fact]
    public void SnapshotType_DefaultsExposeOptionalMetadata()
    {
        Assert.Equal("text/plain", SnapshotType.Default.MimeType);
        Assert.Equal("Text", SnapshotType.Default.DisplayName);
        Assert.Equal("image/png", SnapshotType.Png.MimeType);
        Assert.Equal("PNG image", SnapshotType.Png.DisplayName);
        Assert.Equal("image/svg+xml", SnapshotType.Svg.MimeType);
        Assert.Equal("SVG image", SnapshotType.Svg.DisplayName);
        Assert.Equal("image/gif", SnapshotType.Gif.MimeType);
        Assert.Equal("GIF image", SnapshotType.Gif.DisplayName);
    }

    [Fact]
    public void SnapshotType_EqualityUsesTypeOnly()
    {
        var pngA = SnapshotType.Create("png", mimeType: "image/png", displayName: "Portable Network Graphics");
        var pngB = SnapshotType.Create("png", mimeType: "application/octet-stream", displayName: "Png");

        Assert.Equal(pngA, pngB);
        Assert.Equal(pngA.GetHashCode(), pngB.GetHashCode());
    }

    [Fact]
    public void SnapshotType_CreateReturnsCachedInstance()
    {
        var pngA = SnapshotType.Create("png");
        var pngB = SnapshotType.Create("png");
        var svgA = SnapshotType.Create("svg");
        var svgB = SnapshotType.Create("svg");

        Assert.Same(pngA, pngB);
        Assert.Same(svgA, svgB);
        Assert.Same(SnapshotType.Svg, svgA);
    }

    [Fact]
    public void DefaultSerializer_HandlesByteArrayAsBinary()
    {
        var snapshotType = SnapshotType.Png;
        var expectedBytes = "binary-data"u8.ToArray();
        var data = new SnapshotSettings().Serializers.Serialize(snapshotType, expectedBytes);

        var snapshot = Assert.Single(data.Data);
        Assert.Equal(snapshotType.FileExtension, snapshot.Extension);
        Assert.Equal(expectedBytes, snapshot.Data);
    }

    [Fact]
    public void DefaultSerializer_HandlesStreamAsBinary()
    {
        var snapshotType = SnapshotType.Png;
        var expectedBytes = "stream-binary-data"u8.ToArray();
        using var stream = new MemoryStream(expectedBytes);
        var data = new SnapshotSettings().Serializers.Serialize(snapshotType, stream);

        var snapshot = Assert.Single(data.Data);
        Assert.Equal(snapshotType.FileExtension, snapshot.Extension);
        Assert.Equal(expectedBytes, snapshot.Data);
        Assert.Equal(stream.Length, stream.Position);
    }

    [Fact]
    public void DefaultSerializer_HandlesGifByteArrayAsSingleBinarySnapshot()
    {
        var snapshotType = SnapshotType.Gif;
        var expectedBytes = CreateTwoFrameGif();
        var data = new SnapshotSettings().Serializers.Serialize(snapshotType, expectedBytes);

        var snapshot = Assert.Single(data.Data);
        Assert.Equal(snapshotType.FileExtension, snapshot.Extension);
        Assert.Equal(expectedBytes, snapshot.Data);
    }

    [Fact]
    public void AddGifSerializer_SerializesGifByteArrayAsOneSnapshotPerFrame()
    {
        var snapshotType = SnapshotType.Gif;
        var settings = new SnapshotSettings();
        settings.Serializers.AddGifSerializer();
        var data = settings.Serializers.Serialize(snapshotType, CreateTwoFrameGif());

        Assert.Equal(2, data.Data.Count);
        Assert.All(data.Data, snapshot => Assert.Equal(snapshotType.FileExtension, snapshot.Extension));
        Assert.Equal(CreateSingleFrameGif(), data.Data[0].Data);
        Assert.Equal(CreateSingleFrameGif(), data.Data[1].Data);
    }

    [Fact]
    public void AddGifSerializer_FallsBackToBinarySerializerWhenPayloadIsNotGif()
    {
        var snapshotType = SnapshotType.Gif;
        var payload = "not-a-gif"u8.ToArray();
        var settings = new SnapshotSettings();
        settings.Serializers.AddGifSerializer();
        var data = settings.Serializers.Serialize(snapshotType, payload);

        var snapshot = Assert.Single(data.Data);
        Assert.Equal(snapshotType.FileExtension, snapshot.Extension);
        Assert.Equal(payload, snapshot.Data);
    }

    [Fact]
    public async Task Image_LoadAsync_Stream_DecodesBmpPixels()
    {
        var imageData = CreateBmp24(
            width: 2,
            height: 1,
            pixels:
            [
                0xFFFF0000u,
                0xFF00FF00u,
            ],
            pixelsPerMeter: 2835);

        using var stream = new MemoryStream(imageData);
        var image = await Image.LoadAsync(stream);

        Assert.Equal(2, image.Width);
        Assert.Equal(1, image.Height);
        Assert.Equal(
        [
            new Argb(0xFFFF0000u),
            new Argb(0xFF00FF00u),
        ], image.Pixels.ToArray());
    }

    [Fact]
    public async Task Image_LoadAsync_Path_DecodesBmpPixels()
    {
        using var directory = TemporaryDirectory.Create();
        var path = directory / "sample.bmp";
        var imageData = CreateBmp24(
            width: 1,
            height: 1,
            pixels:
            [
                0xFF112233u,
            ],
            pixelsPerMeter: 2835);

        File.WriteAllBytes(path, imageData);
        var image = await Image.LoadAsync(path.Value);

        Assert.Equal(1, image.Width);
        Assert.Equal(1, image.Height);
        Assert.Equal([new Argb(0xFF112233u)], image.Pixels.ToArray());
    }

    [Fact]
    public async Task Image_LoadAsync_Stream_DecodesPngPixels()
    {
        var imageData = CreatePngRgba32(
            width: 2,
            height: 1,
            pixels:
            [
                0xFFFF0000u,
                0x800000FFu,
            ]);

        using var stream = new MemoryStream(imageData);
        var image = await Image.LoadAsync(stream);

        Assert.Equal(2, image.Width);
        Assert.Equal(1, image.Height);
        Assert.Equal(
        [
            new Argb(0xFFFF0000u),
            new Argb(0x800000FFu),
        ], image.Pixels.ToArray());
    }

    [Fact]
    public async Task Image_LoadAsync_ThrowsWhenFormatIsNotSupported()
    {
        var ex = await Assert.ThrowsAsync<NotSupportedException>(() => Image.LoadAsync(new MemoryStream("not-a-bmp"u8.ToArray())));
        Assert.Contains("Only BMP and PNG are currently supported.", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AddImageComparer_ComparesBmpSnapshotsByPixels()
    {
        var expectedData = CreateBmp24(
            width: 1,
            height: 1,
            pixels:
            [
                0xFF010203u,
            ],
            pixelsPerMeter: 2835);
        var actualData = CreateBmp24(
            width: 1,
            height: 1,
            pixels:
            [
                0xFF010203u,
            ],
            pixelsPerMeter: 3780);

        var settings = new SnapshotSettings();
        settings.Comparers.AddImageComparer();
        var comparer = settings.Comparers.Get(SnapshotType.Bmp);
        Assert.True(comparer.Equals(new SnapshotData("bmp", expectedData), new SnapshotData("bmp", actualData)));
    }

    [Fact]
    public void AddImageComparer_DetectsBmpPixelDifferences()
    {
        var expectedData = CreateBmp24(
            width: 1,
            height: 1,
            pixels:
            [
                0xFF010203u,
            ],
            pixelsPerMeter: 2835);
        var actualData = CreateBmp24(
            width: 1,
            height: 1,
            pixels:
            [
                0xFF040506u,
            ],
            pixelsPerMeter: 2835);

        var settings = new SnapshotSettings();
        settings.Comparers.AddImageComparer();
        var comparer = settings.Comparers.Get(SnapshotType.Bmp);
        Assert.False(comparer.Equals(new SnapshotData("bmp", expectedData), new SnapshotData("bmp", actualData)));
    }

    [Fact]
    public void AddImageComparer_ComparesPngSnapshotsByPixels()
    {
        var expectedData = CreatePngRgba32(
            width: 1,
            height: 1,
            pixels:
            [
                0xFF010203u,
            ],
            gamma: 0.45455f);
        var actualData = CreatePngRgba32(
            width: 1,
            height: 1,
            pixels:
            [
                0xFF010203u,
            ],
            gamma: 1.0f);

        var settings = new SnapshotSettings();
        settings.Comparers.AddImageComparer();
        var comparer = settings.Comparers.Get(SnapshotType.Png);
        Assert.True(comparer.Equals(new SnapshotData("png", expectedData), new SnapshotData("png", actualData)));
    }

    [Fact]
    public void AddImageComparer_DetectsPngPixelDifferences()
    {
        var expectedData = CreatePngRgba32(
            width: 1,
            height: 1,
            pixels:
            [
                0xFF010203u,
            ]);
        var actualData = CreatePngRgba32(
            width: 1,
            height: 1,
            pixels:
            [
                0xFF040506u,
            ]);

        var settings = new SnapshotSettings();
        settings.Comparers.AddImageComparer();
        var comparer = settings.Comparers.Get(SnapshotType.Png);
        Assert.False(comparer.Equals(new SnapshotData("png", expectedData), new SnapshotData("png", actualData)));
    }

    [Fact]
    public void ImageComparer_WithSimilarityThreshold_AllowsSmallDifferences()
    {
        var expectedData = CreateBmp24(
            width: 1,
            height: 1,
            pixels:
            [
                0xFF000000u,
            ],
            pixelsPerMeter: 2835);
        var actualData = CreateBmp24(
            width: 1,
            height: 1,
            pixels:
            [
                0xFF010000u,
            ],
            pixelsPerMeter: 2835);

        var comparer = new ImageComparer(new ImageComparisonSettings
        {
            SimilarityThreshold = 0.95f,
        });

        Assert.True(comparer.Equals(new SnapshotData("bmp", expectedData), new SnapshotData("bmp", actualData)));
    }

    [Fact]
    public void Validate_CreatesActualFileWhenSnapshotChanged()
    {
        using var directory = TemporaryDirectory.Create();
        var settings = CreateDeterministicSnapshotSettings(directory, "actual");
        var expectedPath = directory.GetFullPath("snapshot.verified.txt");
        var actualPath = directory.GetFullPath("snapshot.actual.txt");

        File.WriteAllText(expectedPath, "expected");
        Assert.Throws<SnapshotAssertionException>(() => Snapshot.Validate("sample", settings));

        Assert.True(File.Exists(actualPath));
        Assert.Equal("actual", File.ReadAllText(actualPath));
    }

    [Fact]
    public async Task Validate_RetriesWhenActualFileIsLocked()
    {
        using var directory = TemporaryDirectory.Create();
        var settings = CreateDeterministicSnapshotSettings(directory, "actual");
        var expectedPath = directory.GetFullPath("snapshot.verified.txt");
        var actualPath = directory.GetFullPath("snapshot.actual.txt");
        await File.WriteAllTextAsync(expectedPath, "expected");
        await File.WriteAllTextAsync(actualPath, "locked");

        await using var lockStream = new FileStream(actualPath, FileMode.Open, FileAccess.Read, FileShare.None);
        var releaseTask = Task.Run(() =>
        {
            Thread.Sleep(250);
            lockStream.Dispose();
        });

        try
        {
            Assert.Throws<SnapshotAssertionException>(() => Snapshot.Validate("sample", settings));
        }
        finally
        {
            await releaseTask;
        }

        Assert.Equal("actual", await File.ReadAllTextAsync(actualPath));
    }

    [Fact]
    public void Validate_ErrorMessageIncludesVerifiedAndActualPaths_ForAllChangedFiles()
    {
        using var directory = TemporaryDirectory.Create();
        var settings = new SnapshotSettings()
        {
            AutoDetectContinuousEnvironment = false,
            SnapshotUpdateStrategy = SnapshotUpdateStrategy.Disallow,
            AssertionExceptionCreator = new FixedAssertionExceptionBuilder(),
            SnapshotPathStrategy = context => directory / ("snapshot_" + context.Index.ToString(CultureInfo.InvariantCulture) + ".verified.txt"),
        };
        settings.Serializers.Add(new FixedCountSerializer(count: 2));

        var verifiedPath0 = directory.GetFullPath("snapshot_0.verified.txt");
        var verifiedPath1 = directory.GetFullPath("snapshot_1.verified.txt");
        File.WriteAllText(verifiedPath0, "old_0");
        File.WriteAllText(verifiedPath1, "old_1");

        var exception = Assert.Throws<SnapshotAssertionException>(() => Snapshot.Validate("sample", settings));
        var actualPath0 = directory.GetFullPath("snapshot_0.actual.txt");
        var actualPath1 = directory.GetFullPath("snapshot_1.actual.txt");

        Assert.Contains("Snapshots do not match.", exception.Message, StringComparison.Ordinal);
        Assert.Contains("Verified: " + verifiedPath0.Value, exception.Message, StringComparison.Ordinal);
        Assert.Contains("Actual:   " + actualPath0.Value, exception.Message, StringComparison.Ordinal);
        Assert.Contains("Verified: " + verifiedPath1.Value, exception.Message, StringComparison.Ordinal);
        Assert.Contains("Actual:   " + actualPath1.Value, exception.Message, StringComparison.Ordinal);
        Assert.Contains("Resolution guidance:", exception.Message, StringComparison.Ordinal);
        Assert.Contains("If the new behavior is correct, copy each .actual file to its .verified file.", exception.Message, StringComparison.Ordinal);
        Assert.Contains("To update snapshots automatically, re-run the test with SNAPSHOTTESTING_STRATEGY=Overwrite (or OverwriteWithoutFailure).", exception.Message, StringComparison.Ordinal);
        Assert.True(File.Exists(actualPath0));
        Assert.True(File.Exists(actualPath1));
    }

    [Theory]
    [InlineData("DISALLOW", nameof(SnapshotUpdateStrategy.Disallow))]
    [InlineData("overwrite", nameof(SnapshotUpdateStrategy.Overwrite))]
    [InlineData("mErGeToOlSyNc", nameof(SnapshotUpdateStrategy.MergeToolSync))]
    [InlineData("OverwriteWithoutFailure", nameof(SnapshotUpdateStrategy.OverwriteWithoutFailure))]
    public void SnapshotUpdateStrategy_Default_CanBeConfiguredUsingEnvironmentVariable(string value, string expectedStrategyName)
    {
        using var _ = new EnvironmentVariableScope(SnapshotUpdateStrategyEnvironmentVariableName, value);

        var settings = new SnapshotSettings();

        Assert.Same(GetSnapshotUpdateStrategy(expectedStrategyName), settings.SnapshotUpdateStrategy);
    }

    [Fact]
    public void SnapshotUpdateStrategy_Default_InvalidEnvironmentVariableValue_UsesDisallow()
    {
        using var _ = new EnvironmentVariableScope(SnapshotUpdateStrategyEnvironmentVariableName, "invalid");

        var settings = new SnapshotSettings();

        Assert.Same(SnapshotUpdateStrategy.Disallow, settings.SnapshotUpdateStrategy);
    }

    [Fact]
    public void SnapshotUpdateStrategy_ExplicitSetting_HasPriorityOverEnvironmentVariable()
    {
        using var _ = new EnvironmentVariableScope(SnapshotUpdateStrategyEnvironmentVariableName, nameof(SnapshotUpdateStrategy.Overwrite));

        var settings = new SnapshotSettings()
        {
            SnapshotUpdateStrategy = SnapshotUpdateStrategy.Disallow,
        };

        Assert.Same(SnapshotUpdateStrategy.Disallow, settings.SnapshotUpdateStrategy);
    }

    [Fact]
    public void Validate_SucceedsWhenMultipleSnapshotFilesMatch()
    {
        using var directory = TemporaryDirectory.Create();
        var baseSettings = new SnapshotSettings()
        {
            AutoDetectContinuousEnvironment = false,
            AssertionExceptionCreator = new FixedAssertionExceptionBuilder(),
            SnapshotPathStrategy = context => directory / ("snapshot_fixed_" + context.Index.ToString(CultureInfo.InvariantCulture) + ".verified." + (context.Extension ?? "txt")),
            SnapshotUpdateStrategy = SnapshotUpdateStrategy.OverwriteWithoutFailure,
        };

        ValidateWithSerializerCount(baseSettings, count: 2);

        var validateSettings = baseSettings with
        {
            SnapshotUpdateStrategy = SnapshotUpdateStrategy.Disallow,
        };

        ValidateWithSerializerCount(validateSettings, count: 2);
    }

    [Fact]
    public void ScrubLinesMatching_Regex()
    {
        using var directory = TemporaryDirectory.Create();
        var settings = CreateScrubberSnapshotSettings(directory);
        settings.ScrubLinesMatching(new Regex("Line[2]", RegexOptions.None, TimeSpan.FromSeconds(10)));
        settings.Serializers.Add(new FixedValueSerializer("Line1\nLine2\nLine3"));

        Snapshot.Validate("sample", settings);

        Assert.Equal(["Line1", "Line3"], ReadVerifiedSnapshotLines(directory));
    }

    [Fact]
    public void ScrubLinesMatching_Pattern()
    {
        using var directory = TemporaryDirectory.Create();
        var settings = CreateScrubberSnapshotSettings(directory);
        settings.ScrubLinesMatching("Line[2]");
        settings.Serializers.Add(new FixedValueSerializer("Line1\nLine2\nLine3"));

        Snapshot.Validate("sample", settings);

        Assert.Equal(["Line1", "Line3"], ReadVerifiedSnapshotLines(directory));
    }

    [Fact]
    public void ScrubLinesContaining_StringComparison_OrdinalIgnoreCase()
    {
        using var directory = TemporaryDirectory.Create();
        var settings = CreateScrubberSnapshotSettings(directory);
        settings.ScrubLinesContaining(StringComparison.OrdinalIgnoreCase, "line2");
        settings.Serializers.Add(new FixedValueSerializer("Line1\nLine2\nLine3"));

        Snapshot.Validate("sample", settings);

        Assert.Equal(["Line1", "Line3"], ReadVerifiedSnapshotLines(directory));
    }

    [Fact]
    public void ScrubLinesContaining_StringComparison_Ordinal()
    {
        using var directory = TemporaryDirectory.Create();
        var settings = CreateScrubberSnapshotSettings(directory);
        settings.ScrubLinesContaining(StringComparison.Ordinal, "line2");
        settings.Serializers.Add(new FixedValueSerializer("Line1\nLine2\nLine3"));

        Snapshot.Validate("sample", settings);

        Assert.Equal(["Line1", "Line2", "Line3"], ReadVerifiedSnapshotLines(directory));
    }

    [Fact]
    public void ScrubLinesWithReplace()
    {
        using var directory = TemporaryDirectory.Create();
        var settings = CreateScrubberSnapshotSettings(directory);
        settings.ScrubLinesWithReplace(line => line.ToLowerInvariant());
        settings.Serializers.Add(new FixedValueSerializer("Line1\nLine2\nLine3"));

        Snapshot.Validate("sample", settings);

        Assert.Equal(["line1", "line2", "line3"], ReadVerifiedSnapshotLines(directory));
    }

    [Fact]
    public void ScrubLinesWithReplace_RemoveLine()
    {
        using var directory = TemporaryDirectory.Create();
        var settings = CreateScrubberSnapshotSettings(directory);
        settings.ScrubLinesWithReplace(line => line == "Line2" ? null : line);
        settings.Serializers.Add(new FixedValueSerializer("Line1\nLine2\nLine3"));

        Snapshot.Validate("sample", settings);

        Assert.Equal(["Line1", "Line3"], ReadVerifiedSnapshotLines(directory));
    }

    [Fact]
    public void Scrub_Guid()
    {
        using var directory = TemporaryDirectory.Create();
        var settings = CreateScrubberSnapshotSettings(directory);
        settings.ConfigureHumanReadableSerializer(options => options.ScrubGuid());
        var guids = new[]
        {
            new Guid("43164674-b264-42b8-a7e5-6565667360b0"),
            new Guid("43164674-b264-42b8-a7e5-6565667360b0"),
            new Guid("6ff5182f-7644-4bc1-a3a4-38092cb3663a"),
            Guid.Empty,
        };

        Snapshot.Validate(guids, settings);

        Assert.Equal(
        [
            "- 00000000-0000-0000-0000-000000000001",
            "- 00000000-0000-0000-0000-000000000001",
            "- 00000000-0000-0000-0000-000000000002",
            "- 00000000-0000-0000-0000-000000000000",
        ], ReadVerifiedSnapshotLines(directory));
    }

    [Fact]
    public void Scrub_UseRelativeTimeSpan()
    {
        using var directory = TemporaryDirectory.Create();
        var settings = CreateScrubberSnapshotSettings(directory);
        var origin = TimeSpan.FromSeconds(1);
        settings.ConfigureHumanReadableSerializer(options => options.UseRelativeTimeSpan(origin));
        var values = new[]
        {
            TimeSpan.FromSeconds(0),
            TimeSpan.FromSeconds(1),
            TimeSpan.FromSeconds(2),
        };

        Snapshot.Validate(values, settings);

        Assert.Equal(
        [
            "- -00:00:01",
            "- 00:00:00",
            "- 00:00:01",
        ], ReadVerifiedSnapshotLines(directory));
    }

    [Fact]
    public void Scrub_UseRelativeDateTime()
    {
        using var directory = TemporaryDirectory.Create();
        var settings = CreateScrubberSnapshotSettings(directory);
        var origin = new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        settings.ConfigureHumanReadableSerializer(options => options.UseRelativeDateTime(origin));
        var values = new[]
        {
            origin,
            origin.AddSeconds(1),
            origin.AddSeconds(2),
        };

        Snapshot.Validate(values, settings);

        Assert.Equal(
        [
            "- 00:00:00",
            "- 00:00:01",
            "- 00:00:02",
        ], ReadVerifiedSnapshotLines(directory));
    }

    [Theory]
    [InlineData("ping", "ping", "")]
    [InlineData("ping ", "ping", "")]
    [InlineData("ping a b", "ping", "a b")]
    [InlineData("\"ping\"", "ping", "")]
    [InlineData("\"ping\" ", "ping", "")]
    [InlineData("\"ping\" a b", "ping", "a b")]
    public void GitTool_ParseCommand(string value, string command, string arguments)
    {
        Assert.Equal((command, arguments), GitTool.ParseCommandFromConfiguration(value));
    }

    private static void ValidateWithSerializerCount(SnapshotSettings settings, int count)
    {
        settings.Serializers.Add(new FixedCountSerializer(count));
        Snapshot.Validate("sample", settings);
    }

    private sealed class FixedCountSerializer(int count) : ISnapshotSerializer
    {
        public bool TrySerialize(SnapshotType type, object? value, [NotNullWhen(true)] out SerializedSnapshot? result)
        {
            if (type != SnapshotType.Default)
            {
                result = null;
                return false;
            }

            var data = new List<SnapshotData>(count);
            for (var i = 0; i < count; i++)
            {
                data.Add(new SnapshotData("txt", Encoding.UTF8.GetBytes("value_" + i.ToString(CultureInfo.InvariantCulture))));
            }

            result = new SerializedSnapshot(data);
            return true;
        }
    }

    private static SnapshotSettings CreateDeterministicSnapshotSettings(TemporaryDirectory directory, string serializedValue)
    {
        var settings = new SnapshotSettings()
        {
            AutoDetectContinuousEnvironment = false,
            SnapshotUpdateStrategy = SnapshotUpdateStrategy.Disallow,
            AssertionExceptionCreator = new FixedAssertionExceptionBuilder(),
            SnapshotPathStrategy = _ => directory / "snapshot.verified.txt",
        };

        settings.Serializers.Add(new FixedValueSerializer(serializedValue));
        return settings;
    }

    private static SnapshotSettings CreateScrubberSnapshotSettings(TemporaryDirectory directory)
    {
        return new SnapshotSettings
        {
            AutoDetectContinuousEnvironment = false,
            SnapshotUpdateStrategy = SnapshotUpdateStrategy.OverwriteWithoutFailure,
            AssertionExceptionCreator = new FixedAssertionExceptionBuilder(),
            SnapshotPathStrategy = _ => directory / "snapshot.verified.txt",
        };
    }

    private static string[] ReadVerifiedSnapshotLines(TemporaryDirectory directory)
    {
        var path = directory / "snapshot.verified.txt";
        return File.ReadAllLines(path);
    }

    private static byte[] CreateBmp24(int width, int height, IReadOnlyList<uint> pixels, int pixelsPerMeter)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);

        if (pixels.Count != checked(width * height))
            throw new ArgumentOutOfRangeException(nameof(pixels));

        const int FileHeaderSize = 14;
        const int InfoHeaderSize = 40;
        var rowSizeWithoutPadding = checked(width * 3);
        var rowStride = (rowSizeWithoutPadding + 3) & ~3;
        var pixelDataSize = checked(rowStride * height);
        var data = new byte[FileHeaderSize + InfoHeaderSize + pixelDataSize];

        data[0] = (byte)'B';
        data[1] = (byte)'M';
        WriteUInt32LittleEndian(data, 2, (uint)data.Length);
        WriteUInt32LittleEndian(data, 10, FileHeaderSize + InfoHeaderSize);
        WriteUInt32LittleEndian(data, 14, InfoHeaderSize);
        WriteInt32LittleEndian(data, 18, width);
        WriteInt32LittleEndian(data, 22, height);
        WriteUInt16LittleEndian(data, 26, 1);
        WriteUInt16LittleEndian(data, 28, 24);
        WriteUInt32LittleEndian(data, 30, 0);
        WriteUInt32LittleEndian(data, 34, (uint)pixelDataSize);
        WriteInt32LittleEndian(data, 38, pixelsPerMeter);
        WriteInt32LittleEndian(data, 42, pixelsPerMeter);

        for (var y = 0; y < height; y++)
        {
            var sourceRow = height - y - 1;
            var sourceOffset = sourceRow * width;
            var destinationOffset = FileHeaderSize + InfoHeaderSize + y * rowStride;
            for (var x = 0; x < width; x++)
            {
                var pixel = pixels[sourceOffset + x];
                data[destinationOffset + x * 3] = (byte)(pixel & 0xFF);
                data[destinationOffset + x * 3 + 1] = (byte)((pixel >> 8) & 0xFF);
                data[destinationOffset + x * 3 + 2] = (byte)((pixel >> 16) & 0xFF);
            }
        }

        return data;
    }

    private static byte[] CreatePngRgba32(int width, int height, IReadOnlyList<uint> pixels, float? gamma = null)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);

        if (pixels.Count != checked(width * height))
            throw new ArgumentOutOfRangeException(nameof(pixels));

        var rowStride = checked(width * 4 + 1);
        var imageData = new byte[checked(rowStride * height)];
        for (var y = 0; y < height; y++)
        {
            var rowOffset = y * rowStride;
            imageData[rowOffset] = 0;
            for (var x = 0; x < width; x++)
            {
                var pixel = pixels[y * width + x];
                var pixelOffset = rowOffset + 1 + x * 4;
                imageData[pixelOffset] = (byte)((pixel >> 16) & 0xFF);
                imageData[pixelOffset + 1] = (byte)((pixel >> 8) & 0xFF);
                imageData[pixelOffset + 2] = (byte)(pixel & 0xFF);
                imageData[pixelOffset + 3] = (byte)(pixel >> 24);
            }
        }

        byte[] compressedData;
        using (var compressedStream = new MemoryStream())
        {
            using (var zlib = new ZLibStream(compressedStream, CompressionLevel.SmallestSize, leaveOpen: true))
            {
                zlib.Write(imageData);
            }

            compressedData = compressedStream.ToArray();
        }

        using var stream = new MemoryStream();
        stream.Write([137, 80, 78, 71, 13, 10, 26, 10]);

        Span<byte> ihdrData = stackalloc byte[13];
        BinaryPrimitives.WriteUInt32BigEndian(ihdrData, (uint)width);
        BinaryPrimitives.WriteUInt32BigEndian(ihdrData[4..], (uint)height);
        ihdrData[8] = 8;
        ihdrData[9] = 6;
        ihdrData[10] = 0;
        ihdrData[11] = 0;
        ihdrData[12] = 0;
        WritePngChunk(stream, "IHDR"u8, ihdrData);

        if (gamma is not null)
        {
            Span<byte> gammaData = stackalloc byte[4];
            var gammaValue = checked((uint)Math.Round(gamma.Value * 100000f, MidpointRounding.AwayFromZero));
            BinaryPrimitives.WriteUInt32BigEndian(gammaData, gammaValue);
            WritePngChunk(stream, "gAMA"u8, gammaData);
        }

        WritePngChunk(stream, "IDAT"u8, compressedData);
        WritePngChunk(stream, "IEND"u8, ReadOnlySpan<byte>.Empty);
        return stream.ToArray();
    }

    private static void WritePngChunk(Stream stream, ReadOnlySpan<byte> type, ReadOnlySpan<byte> data)
    {
        Span<byte> uintBuffer = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(uintBuffer, (uint)data.Length);
        stream.Write(uintBuffer);
        stream.Write(type);
        stream.Write(data);

        var crc = ComputePngCrc32(type, data);
        BinaryPrimitives.WriteUInt32BigEndian(uintBuffer, crc);
        stream.Write(uintBuffer);
    }

    private static uint ComputePngCrc32(ReadOnlySpan<byte> type, ReadOnlySpan<byte> data)
    {
        var crc = uint.MaxValue;
        crc = UpdatePngCrc32(crc, type);
        crc = UpdatePngCrc32(crc, data);
        return ~crc;
    }

    private static uint UpdatePngCrc32(uint crc, ReadOnlySpan<byte> data)
    {
        foreach (var value in data)
        {
            crc ^= value;
            for (var i = 0; i < 8; i++)
            {
                crc = (crc & 1) == 0 ? crc >> 1 : 0xEDB88320u ^ (crc >> 1);
            }
        }

        return crc;
    }

    private static void WriteUInt32LittleEndian(byte[] data, int offset, uint value)
    {
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(offset, 4), value);
    }

    private static void WriteInt32LittleEndian(byte[] data, int offset, int value)
    {
        BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(offset, 4), value);
    }

    private static void WriteUInt16LittleEndian(byte[] data, int offset, ushort value)
    {
        BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(offset, 2), value);
    }

    private static SnapshotUpdateStrategy GetSnapshotUpdateStrategy(string name)
    {
        return name switch
        {
            nameof(SnapshotUpdateStrategy.Disallow) => SnapshotUpdateStrategy.Disallow,
            nameof(SnapshotUpdateStrategy.MergeTool) => SnapshotUpdateStrategy.MergeTool,
            nameof(SnapshotUpdateStrategy.MergeToolSync) => SnapshotUpdateStrategy.MergeToolSync,
            nameof(SnapshotUpdateStrategy.Overwrite) => SnapshotUpdateStrategy.Overwrite,
            nameof(SnapshotUpdateStrategy.OverwriteWithoutFailure) => SnapshotUpdateStrategy.OverwriteWithoutFailure,
            _ => throw new ArgumentOutOfRangeException(nameof(name)),
        };
    }

    private static byte[] CreateSingleFrameGif()
    {
        return
        [
            0x47, 0x49, 0x46, 0x38, 0x39, 0x61,
            0x01, 0x00, 0x01, 0x00,
            0x80, 0x01, 0x00,
            0xFF, 0xFF, 0xFF,
            0x00, 0x00, 0x00,
            0x2C,
            0x00, 0x00, 0x00, 0x00,
            0x01, 0x00, 0x01, 0x00,
            0x00,
            0x02,
            0x02, 0x44, 0x01,
            0x00,
            0x3B,
        ];
    }

    private static byte[] CreateTwoFrameGif()
    {
        return
        [
            0x47, 0x49, 0x46, 0x38, 0x39, 0x61,
            0x01, 0x00, 0x01, 0x00,
            0x80, 0x01, 0x00,
            0xFF, 0xFF, 0xFF,
            0x00, 0x00, 0x00,
            0x2C,
            0x00, 0x00, 0x00, 0x00,
            0x01, 0x00, 0x01, 0x00,
            0x00,
            0x02,
            0x02, 0x44, 0x01,
            0x00,
            0x2C,
            0x00, 0x00, 0x00, 0x00,
            0x01, 0x00, 0x01, 0x00,
            0x00,
            0x02,
            0x02, 0x44, 0x01,
            0x00,
            0x3B,
        ];
    }

    private sealed class SnapshotTypeSerializer : ISnapshotSerializer
    {
        public bool TrySerialize(SnapshotType type, object? value, [NotNullWhen(true)] out SerializedSnapshot? result)
        {
            if (type != SnapshotType.Png)
            {
                result = null;
                return false;
            }

            result = new SerializedSnapshot([new SnapshotData("txt", Encoding.UTF8.GetBytes(type.Type))]);
            return true;
        }
    }

    private sealed class FixedValueSerializer(string value) : ISnapshotSerializer
    {
        public bool TrySerialize(SnapshotType type, object? value_, [NotNullWhen(true)] out SerializedSnapshot? result)
        {
            if (type != SnapshotType.Default)
            {
                result = null;
                return false;
            }

            result = new SerializedSnapshot([new SnapshotData("txt", Encoding.UTF8.GetBytes(value))]);
            return true;
        }
    }

    private sealed class FixedAssertionExceptionBuilder : AssertionExceptionBuilder
    {
        public override Exception CreateException(string message)
        {
            return new SnapshotAssertionException(message);
        }
    }

    private sealed class EnvironmentVariableScope : IDisposable
    {
        private readonly string _name;
        private readonly string? _previousValue;

        public EnvironmentVariableScope(string name, string? value)
        {
            _name = name;
            _previousValue = Environment.GetEnvironmentVariable(name);
            Environment.SetEnvironmentVariable(name, value);
        }

        public void Dispose()
        {
            Environment.SetEnvironmentVariable(_name, _previousValue);
        }
    }
}
