using Meziantou.Framework.InlineSnapshotTesting.Serialization;

namespace Meziantou.Framework.InlineSnapshotTesting.Tests;

// Some tests set the INLINESNAPSHOTTESTING_STRATEGY environment variable, which is process-wide and is inherited by the
// processes started by the other tests, so this class does not run in parallel.
[TestClass(DisableParallelization = true)]
public sealed class InlineSnapshotSettingsTests
{
    private const string SnapshotUpdateStrategyEnvironmentVariableName = "INLINESNAPSHOTTESTING_STRATEGY";

    [Fact]
    public void Clone()
    {
        var settings = new InlineSnapshotSettings()
        {
            AllowedStringFormats = CSharpStringFormats.LeftAlignedRaw,
            AssertionExceptionCreator = new AssertionExceptionBuilder(),
            AutoDetectContinuousEnvironment = false,
            EndOfLine = "\r\n",
            FileEncoding = Encoding.ASCII,
            ValidateSourceFilePathUsingPdbInfoWhenAvailable = true,
            ForceUpdateSnapshots = false,
            ValidateLineNumberUsingPdbInfoWhenAvailable = false,
            SnapshotUpdateStrategy = SnapshotUpdateStrategy.Disallow,
            SnapshotSerializer = new HumanReadableSnapshotSerializer(),
            MergeTools = [MergeTool.VisualStudioCode],
        };

        settings.ScrubLinesContaining(StringComparison.Ordinal, "test");

        var clone = settings.Clone();

        Assert.Same(settings.SnapshotSerializer, clone.SnapshotSerializer);
        Assert.Same(settings.AssertionExceptionCreator, clone.AssertionExceptionCreator);
        Assert.Same(settings.SnapshotUpdateStrategy, clone.SnapshotUpdateStrategy);
        Assert.Equal(settings.AllowedStringFormats, clone.AllowedStringFormats);
        Assert.Equal(settings.AutoDetectContinuousEnvironment, clone.AutoDetectContinuousEnvironment);
        Assert.Equal(settings.EndOfLine, clone.EndOfLine);
        Assert.Equal(settings.FileEncoding, clone.FileEncoding);
        Assert.Equal(settings.ValidateSourceFilePathUsingPdbInfoWhenAvailable, clone.ValidateSourceFilePathUsingPdbInfoWhenAvailable);
        Assert.Equal(settings.ForceUpdateSnapshots, clone.ForceUpdateSnapshots);
        Assert.Equal(settings.ValidateLineNumberUsingPdbInfoWhenAvailable, clone.ValidateLineNumberUsingPdbInfoWhenAvailable);
        Assert.Equal(settings.MergeTools, clone.MergeTools);

        Assert.Equal(settings.Scrubbers, clone.Scrubbers);
        Assert.NotSame(settings.Scrubbers, clone.Scrubbers);
    }

    [Fact]
    public void AssertSnapshot_ShouldContainResolutionGuidance()
    {
        var settings = new InlineSnapshotSettings();

        var exception = Assert.ThrowsAny<Exception>(() => settings.AssertSnapshot("old", "new"));
        Assert.StartsWith("Snapshots do not match:\n", exception.Message);
        Assert.Contains("Resolution guidance:", exception.Message);
        Assert.Contains("- If the new behavior is correct, update the inline snapshot in source code:", exception.Message);
        Assert.Contains("  - remove lines starting with '-' from the snapshot", exception.Message);
        Assert.Contains("  - add lines starting with '+' to the snapshot", exception.Message);
        Assert.Contains("  - To update snapshots automatically, re-run the test with INLINESNAPSHOTTESTING_STRATEGY=Overwrite (or OverwriteWithoutFailure).", exception.Message);
        Assert.Contains("- Re-run the test.", exception.Message);
    }

    [Theory]
    [InlineData("DISALLOW", nameof(SnapshotUpdateStrategy.Disallow))]
    [InlineData("overwrite", nameof(SnapshotUpdateStrategy.Overwrite))]
    [InlineData("mErGeToOlSyNc", nameof(SnapshotUpdateStrategy.MergeToolSync))]
    [InlineData("OverwriteWithoutFailure", nameof(SnapshotUpdateStrategy.OverwriteWithoutFailure))]
    public void SnapshotUpdateStrategy_Default_CanBeConfiguredUsingEnvironmentVariable(string value, string expectedStrategyName)
    {
        using var _ = new EnvironmentVariableScope(SnapshotUpdateStrategyEnvironmentVariableName, value);

        var settings = new InlineSnapshotSettings();

        Assert.Same(GetSnapshotUpdateStrategy(expectedStrategyName), settings.SnapshotUpdateStrategy);
    }

    [Fact]
    public void SnapshotUpdateStrategy_Default_InvalidEnvironmentVariableValue_UsesDisallow()
    {
        using var _ = new EnvironmentVariableScope(SnapshotUpdateStrategyEnvironmentVariableName, "invalid");

        var settings = new InlineSnapshotSettings();

        Assert.Same(SnapshotUpdateStrategy.Disallow, settings.SnapshotUpdateStrategy);
    }

    [Fact]
    public void SnapshotUpdateStrategy_ExplicitSetting_HasPriorityOverEnvironmentVariable()
    {
        using var _ = new EnvironmentVariableScope(SnapshotUpdateStrategyEnvironmentVariableName, nameof(SnapshotUpdateStrategy.Overwrite));

        var settings = new InlineSnapshotSettings()
        {
            SnapshotUpdateStrategy = SnapshotUpdateStrategy.Disallow,
        };

        Assert.Same(SnapshotUpdateStrategy.Disallow, settings.SnapshotUpdateStrategy);
    }

    [Fact]
    public void DiffToolFromEnvironmentVariable_NamingItself_DoesNotRecurse()
    {
        // The reflection lookup used to resolve this very instance and call Start on it again.
        using var _ = new EnvironmentVariableScope("DiffEngine_Tool", nameof(MergeTool.DiffToolFromEnvironmentVariable));

        Assert.Null(MergeTool.DiffToolFromEnvironmentVariable.Start("current.cs", "new.cs"));
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

    private sealed class EnvironmentVariableScope : IDisposable
    {
        private readonly string _name;
        private readonly string? _previousValue;

        public EnvironmentVariableScope(string name, string? value)
        {
            // InlineSnapshotSettings.Default is created by the type initializer, which reads the environment variable.
            // Force it to run now, so the other tests of the assembly never observe the value set by this scope.
            _ = InlineSnapshotSettings.Default;

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
