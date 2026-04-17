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
            PathStrategy = context => directory / context.FileName,
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
            PathStrategy = context => directory / context.FileName,
            FileNameStrategy = context => $"snapshot_fixed_{context.Index}.{context.Extension ?? "txt"}",
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
        original.SetSnapshotSerializer(new SnapshotType("dummy"), new FixedCountSerializer(count: 1));
        original.SetSnapshotComparer(new SnapshotType("dummy"), ByteArraySnapshotComparer.Default);

        var clone = original with { };
        clone.SetSnapshotSerializer(new SnapshotType("new"), new FixedCountSerializer(count: 2));

        Assert.NotSame(original.Serializers, clone.Serializers);
        Assert.NotSame(original.Comparers, clone.Comparers);
        Assert.True(clone.Serializers.ContainsKey(new SnapshotType("new")));
        Assert.False(original.Serializers.ContainsKey(new SnapshotType("new")));
    }

    [Fact]
    public void FileNameStrategy_UsesHashAndIndexPattern()
    {
        var settings = new SnapshotSettings();
        var context = new SnapshotFileNameContext(
            SourceFilePath: FullPath.FromPath("C:\\temp\\snapshot-tests.cs"),
            MethodName: "MethodName",
            MemberName: "MemberName",
            LineNumber: 42,
            Type: SnapshotType.Default,
            Index: 2,
            Extension: "png",
            TestContext: new SnapshotTestContext(TestName: "Image snapshot"),
            Settings: settings);

        var fileName = settings.FileNameStrategy(context);
        Assert.Matches(new Regex("^[A-Za-z0-9._-]+_[0-9a-f]{10}_2\\.png$", RegexOptions.CultureInvariant, matchTimeout: TimeSpan.FromSeconds(1)), fileName);
        Assert.True(fileName.Length <= settings.MaxSnapshotFileNameLength);
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
            FileNameStrategy = context => $"{context.Type.Type}_{context.Index}.{context.Extension}",
            PathStrategy = context => directory / context.FileName,
        };

        settings.SetSnapshotSerializer(snapshotType, new SnapshotTypeSerializer());
        Snapshot.Validate(snapshotType, "sample", settings);

        var filePath = directory / "png_0.png";
        Assert.True(File.Exists(filePath));
        Assert.Equal("png", File.ReadAllText(filePath));
    }

    [Fact]
    public void DefaultSerializer_HandlesByteArrayAsBinary()
    {
        var serializer = new HumanReadableSnapshotSerializer();
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
        var serializer = new HumanReadableSnapshotSerializer();
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
    public void Validate_CreatesReceivedFileWhenSnapshotChanged()
    {
        using var directory = TemporaryDirectory.Create();
        var settings = CreateDeterministicSnapshotSettings(directory, "actual");
        var expectedPath = directory.GetFullPath("snapshot_0.txt");
        var receivedPath = directory.GetFullPath("snapshot_0.received.txt");

        File.WriteAllText(expectedPath, "expected");
        Assert.Throws<SnapshotAssertionException>(() => Snapshot.Validate("sample", settings));

        Assert.True(File.Exists(receivedPath));
        Assert.Equal("actual", File.ReadAllText(receivedPath));
    }

    [Fact]
    public async Task Validate_RetriesWhenReceivedFileIsLocked()
    {
        using var directory = TemporaryDirectory.Create();
        var settings = CreateDeterministicSnapshotSettings(directory, "actual");
        var expectedPath = directory.GetFullPath("snapshot_0.txt");
        var receivedPath = directory.GetFullPath("snapshot_0.received.txt");
        await File.WriteAllTextAsync(expectedPath, "expected");
        await File.WriteAllTextAsync(receivedPath, "locked");

        await using var lockStream = new FileStream(receivedPath, FileMode.Open, FileAccess.Read, FileShare.None);
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

        Assert.Equal("actual", await File.ReadAllTextAsync(receivedPath));
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
            FileNameStrategy = _ => "snapshot_0.txt",
            PathStrategy = context => directory / context.FileName,
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

