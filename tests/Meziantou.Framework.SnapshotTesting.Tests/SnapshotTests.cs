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
