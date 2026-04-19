#nullable enable

using System.Text.RegularExpressions;

namespace Meziantou.Framework.SnapshotTesting.Tests;

public sealed class SnapshotTests
{
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
        original.SetSnapshotSerializer(SnapshotType.Create("dummy"), new FixedCountSerializer(count: 1));
        original.SetSnapshotComparer(SnapshotType.Create("dummy"), ByteArraySnapshotComparer.Default);

        var clone = original with { };
        clone.SetSnapshotSerializer(SnapshotType.Create("new"), new FixedCountSerializer(count: 2));

        Assert.NotSame(original.Serializers, clone.Serializers);
        Assert.NotSame(original.Comparers, clone.Comparers);
        Assert.True(clone.Serializers.ContainsKey(SnapshotType.Create("new")));
        Assert.False(original.Serializers.ContainsKey(SnapshotType.Create("new")));
    }

    [Fact]
    public void SnapshotPathStrategy_UsesIndexPatternForShortName()
    {
        var settings = new SnapshotSettings();
        var context = new SnapshotPathContext(
            SourceFilePath: FullPath.FromPath("C:\\temp\\snapshot-tests.cs"),
            MethodName: "MethodName",
            MemberName: "MemberName",
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
            MethodName: "MethodName",
            MemberName: "MemberName",
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
    public void SnapshotPathStrategy_UsesHashAndIndexPatternForLongName()
    {
        var settings = new SnapshotSettings();
        var context = new SnapshotPathContext(
            SourceFilePath: FullPath.FromPath("C:\\temp\\snapshot-tests.cs"),
            MethodName: new string('a', 120),
            MemberName: "MemberName",
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
            MethodName: "MethodName",
            MemberName: "MemberName",
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

        settings.SetSnapshotSerializer(snapshotType, new SnapshotTypeSerializer());
        Snapshot.Validate(snapshotType, "sample", settings);

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

        Assert.Same(pngA, pngB);
    }

    [Fact]
    public void DefaultSerializer_HandlesByteArrayAsBinary()
    {
        var serializer = new DefaultSnapshotSerializer();
        var snapshotType = SnapshotType.Png;
        var expectedBytes = "binary-data"u8.ToArray();

        var data = serializer.Serialize(snapshotType, expectedBytes);

        var snapshot = Assert.Single(data);
        Assert.Equal(snapshotType.Type, snapshot.Extension);
        Assert.Equal(expectedBytes, snapshot.Data);
    }

    [Fact]
    public void DefaultSerializer_HandlesStreamAsBinary()
    {
        var serializer = new DefaultSnapshotSerializer();
        var snapshotType = SnapshotType.Png;
        var expectedBytes = "stream-binary-data"u8.ToArray();
        using var stream = new MemoryStream(expectedBytes);
        stream.Position = 5;

        var data = serializer.Serialize(snapshotType, stream);

        var snapshot = Assert.Single(data);
        Assert.Equal(snapshotType.Type, snapshot.Extension);
        Assert.Equal(expectedBytes, snapshot.Data);
        Assert.Equal(5, stream.Position);
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

    private static void ValidateWithSerializerCount(SnapshotSettings settings, int count)
    {
        settings.SetSnapshotSerializer(SnapshotType.Default, new FixedCountSerializer(count));
        Snapshot.Validate("sample", settings);
    }

    private sealed class FixedCountSerializer(int count) : ISnapshotSerializer
    {
        public IReadOnlyList<SnapshotData> Serialize(SnapshotType type, object? value)
        {
            var result = new List<SnapshotData>(count);
            for (var i = 0; i < count; i++)
            {
                result.Add(new SnapshotData("txt", Encoding.UTF8.GetBytes("value_" + i.ToString(CultureInfo.InvariantCulture))));
            }

            return result;
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

        settings.SetSnapshotSerializer(SnapshotType.Default, new FixedValueSerializer(serializedValue));
        return settings;
    }

    private sealed class SnapshotTypeSerializer : ISnapshotSerializer
    {
        public IReadOnlyList<SnapshotData> Serialize(SnapshotType type, object? value)
        {
            return [new SnapshotData("txt", Encoding.UTF8.GetBytes(type.Type))];
        }
    }

    private sealed class FixedValueSerializer(string value) : ISnapshotSerializer
    {
        public IReadOnlyList<SnapshotData> Serialize(SnapshotType type, object? value_)
        {
            return [new SnapshotData("txt", Encoding.UTF8.GetBytes(value))];
        }
    }

    private sealed class FixedAssertionExceptionBuilder : AssertionExceptionBuilder
    {
        public override Exception CreateException(string message)
        {
            return new SnapshotAssertionException(message);
        }
    }
}


