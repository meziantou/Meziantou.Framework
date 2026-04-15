#nullable enable

using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Meziantou.Framework;

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

    private sealed class FixedAssertionExceptionBuilder : AssertionExceptionBuilder
    {
        public override Exception CreateException(string message)
        {
            return new SnapshotAssertionException(message);
        }
    }
}

