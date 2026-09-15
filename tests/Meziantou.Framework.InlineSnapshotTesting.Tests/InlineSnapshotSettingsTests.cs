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
    public void ScrubMachineName_ReplacesTheMachineName()
    {
        var settings = new InlineSnapshotSettings();
        settings.ScrubMachineName();

        var scrubber = Assert.Single(settings.Scrubbers);
        Assert.Equal("host TheMachineName end", scrubber.Scrub($"host {Environment.MachineName} end"));
    }

    [Fact]
    public void ScrubUserName_ReplacesTheUserName()
    {
        var settings = new InlineSnapshotSettings();
        settings.ScrubUserName();

        var scrubber = Assert.Single(settings.Scrubbers);
        Assert.Equal("user TheUserName end", scrubber.Scrub($"user {Environment.UserName} end"));
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

    [Fact]
    public void AssertSnapshot_ExplainsThatTheDetectedEnvironmentDisabledUpdates()
    {
        var settings = new InlineSnapshotSettings { AutoDetectContinuousEnvironment = true };

        ContinuousEnvironmentDetector.DescriptionOverride = () => "an LLM agent (ClaudeCode)";
        try
        {
            var exception = Assert.ThrowsAny<Exception>(() => settings.AssertSnapshot("old", "new"));
            Assert.Contains("Snapshot updates are disabled because an LLM agent (ClaudeCode) was detected.", exception.Message);
            Assert.Contains("set the INLINESNAPSHOTTESTING_AUTODETECT_CONTINUOUS_ENVIRONMENT environment variable to false, or set InlineSnapshotSettings.AutoDetectContinuousEnvironment to false", exception.Message);

            settings.AutoDetectContinuousEnvironment = false;
            exception = Assert.ThrowsAny<Exception>(() => settings.AssertSnapshot("old", "new"));
            Assert.DoesNotContain("Snapshot updates are disabled", exception.Message);
        }
        finally
        {
            ContinuousEnvironmentDetector.DescriptionOverride = null;
        }
    }

    [Fact]
    public void Validate_WhenTheEnvironmentDisablesUpdates_ReportsTheDifferenceWithoutLocatingTheCall()
    {
        // Locating the call used to run first, and its failure (here, an invalid path) replaced the snapshot difference
        var settings = InlineSnapshotSettings.Default with
        {
            AutoDetectContinuousEnvironment = true,
            SnapshotUpdateStrategy = SnapshotUpdateStrategy.Overwrite,
        };

        ContinuousEnvironmentDetector.DescriptionOverride = () => "a build server (CI)";
        try
        {
            var exception = Assert.Throws<InlineSnapshotAssertionException>(() => InlineSnapshot.Validate(new object(), settings, "invalid snapshot", "invalid\0path", 1));
            Assert.Contains("- invalid snapshot", exception.Message);
        }
        finally
        {
            ContinuousEnvironmentDetector.DescriptionOverride = null;
        }
    }

    [Fact]
    public void AssertSnapshot_ReportsAnUnknownStrategyInTheEnvironmentVariable()
    {
        using var _ = new EnvironmentVariableScope(SnapshotUpdateStrategyEnvironmentVariableName, "Overwirte");

        var exception = Assert.Throws<InlineSnapshotAssertionException>(() => new InlineSnapshotSettings().AssertSnapshot("old", "new"));
        Assert.Contains("The INLINESNAPSHOTTESTING_STRATEGY environment variable is ignored: 'Overwirte' is not a known strategy.", exception.Message);
        Assert.Contains("Disallow, MergeTool, MergeToolSync, Overwrite, OverwriteWithoutFailure", exception.Message);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" overwrite ")]
    [InlineData("MergeToolSync")]
    public void GetUnknownStrategyMessage_KnownOrMissingStrategy_ReturnsNull(string? value)
    {
        Assert.Null(SnapshotUpdateStrategy.GetUnknownStrategyMessage(value));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void MergeToolStrategy_WithoutMergeTools_ReportsTheSnapshotDifference(bool empty)
    {
        // The documentation said that no merge tools meant the default ones, but no merge tool could be started instead
        using var _ = new EnvironmentVariableScope("DiffEngine_Disabled", value: null);

        var directory = Path.Combine(Path.GetTempPath(), "meziantou-inline-snapshot", Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture));
        Directory.CreateDirectory(directory);
        try
        {
            var filePath = Path.Combine(directory, "Snapshot.cs");
            File.WriteAllText(filePath, "InlineSnapshot.Validate(new object(), settings, \"not the snapshot\");" + Environment.NewLine);

            var settings = InlineSnapshotSettings.Default with
            {
                SnapshotUpdateStrategy = SnapshotUpdateStrategy.MergeTool,
                MergeTools = empty ? [] : null,
                AutoDetectContinuousEnvironment = false,
                ValidateSourceFilePathUsingPdbInfoWhenAvailable = false,
                ValidateLineNumberUsingPdbInfoWhenAvailable = false,
            };

            var exception = Assert.Throws<InlineSnapshotAssertionException>(() => InlineSnapshot.Validate(new object(), settings, "not the snapshot", filePath, lineNumber: 1));

            Assert.StartsWith("Snapshots do not match:\n", exception.Message);
            Assert.Contains("\"not the snapshot\"", File.ReadAllText(filePath));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Theory]
    [InlineData(null, true)]
    [InlineData("true", true)]
    [InlineData("false", false)]
    [InlineData("0", false)]
    [InlineData("off", false)]
    public void AutoDetectContinuousEnvironment_Default_CanBeConfiguredUsingEnvironmentVariable(string? value, bool expected)
    {
        using var _ = new EnvironmentVariableScope("INLINESNAPSHOTTESTING_AUTODETECT_CONTINUOUS_ENVIRONMENT", value);

        Assert.Equal(expected, new InlineSnapshotSettings().AutoDetectContinuousEnvironment);
    }

    [Theory]
    [InlineData(nameof(SnapshotUpdateStrategy.Disallow))]
    [InlineData(nameof(SnapshotUpdateStrategy.Overwrite))]
    [InlineData(nameof(SnapshotUpdateStrategy.OverwriteWithoutFailure))]
    public void SnapshotUpdateStrategy_ToString_ReturnsThePublicName(string strategyName)
    {
        Assert.Equal(strategyName, GetSnapshotUpdateStrategy(strategyName).ToString());
    }

    [Theory]
    [InlineData("true")]
    [InlineData("TRUE")]
    [InlineData("1")]
    public void MergeToolStrategy_WhenDiffToolsAreDisabled_ReportsTheSnapshotDifference(string value)
    {
        // Diff tools switched off used to surface as InlineSnapshotException("Cannot start the merge tool"),
        // which replaced the diff and the resolution guidance with a message about the tool.
        using var _ = new EnvironmentVariableScope("DiffEngine_Disabled", value);

        // The file the snapshot lives in is written for this test instead of using this very file through
        // CallerFilePath: a deterministic build reports the compile-time path ("/_/tests/..."), which does not exist
        // on the machine running the tests, and the strategy would fail reading it before it could report anything.
        var directory = Path.Combine(Path.GetTempPath(), "meziantou-inline-snapshot", Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture));
        Directory.CreateDirectory(directory);
        try
        {
            var filePath = Path.Combine(directory, "Snapshot.cs");
            File.WriteAllText(filePath, "InlineSnapshot.Validate(new object(), settings, \"not the snapshot\");" + Environment.NewLine);

            var settings = InlineSnapshotSettings.Default with
            {
                SnapshotUpdateStrategy = SnapshotUpdateStrategy.MergeTool,
                AutoDetectContinuousEnvironment = false,
                ValidateSourceFilePathUsingPdbInfoWhenAvailable = false,
                ValidateLineNumberUsingPdbInfoWhenAvailable = false,
            };

            var exception = Assert.ThrowsAny<Exception>(() => InlineSnapshot.Validate(new object(), settings, "not the snapshot", filePath, lineNumber: 1));

            Assert.StartsWith("Snapshots do not match:\n", exception.Message);
            Assert.Contains("Resolution guidance:", exception.Message);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
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
    public void SnapshotUpdateStrategy_Default_EnvironmentVariableNamingDefault_FallsBackToDisallow()
    {
        // "Default" is one of the static property names, so resolving it by reflection used to re-enter the
        // Default getter and kill the process with a StackOverflowException instead of failing this assertion.
        using var _ = new EnvironmentVariableScope(SnapshotUpdateStrategyEnvironmentVariableName, nameof(SnapshotUpdateStrategy.Default));

        Assert.Same(SnapshotUpdateStrategy.Disallow, SnapshotUpdateStrategy.Default);
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

    [Fact]
    public void MergeToolStrategy_WhenNoMergeToolStarts_ReportsTheFailures()
    {
        using var _ = new EnvironmentVariableScope("DiffEngine_Disabled", value: null);

        var directory = Path.Combine(Path.GetTempPath(), "meziantou-inline-snapshot", Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture));
        Directory.CreateDirectory(directory);
        try
        {
            var filePath = Path.Combine(directory, "Snapshot.cs");
            // The trailing comment keeps the invocation under the column of the call below, which is how the snapshot is located.
            File.WriteAllText(filePath, "InlineSnapshot.Validate(new object(), settings, \"not the snapshot\" /* covers the column of the call site */);" + Environment.NewLine);

            var settings = InlineSnapshotSettings.Default with
            {
                SnapshotUpdateStrategy = SnapshotUpdateStrategy.MergeTool,
                MergeTools = [new ThrowingMergeTool()],
                AutoDetectContinuousEnvironment = false,
                ValidateSourceFilePathUsingPdbInfoWhenAvailable = false,
                ValidateLineNumberUsingPdbInfoWhenAvailable = false,
            };

            var exception = Assert.Throws<InlineSnapshotException>(() => InlineSnapshot.Validate(new object(), settings, "not the snapshot", filePath, lineNumber: 1));

            Assert.Contains("  * Source file:  " + filePath, exception.Message);
            Assert.Contains("  - BrokenMergeTool: The merge tool is broken.", exception.Message);
            Assert.Contains("DiffEngine_Tool", exception.Message);
            Assert.Contains("DiffEngine_Disabled", exception.Message);
            Assert.Contains("\"not the snapshot\"", File.ReadAllText(filePath));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void MergeTool_IsDisabled_IgnoresTheDetectedEnvironmentsWhenAutoDetectionIsOff()
    {
        using var _ = new EnvironmentVariableScope("DiffEngine_Disabled", value: null);

        Assert.False(MergeTool.IsDisabled(autoDetectContinuousEnvironment: false));
    }

    [Theory]
    [InlineData("rider")]
    [InlineData("RIDER")]
    [InlineData(" Rider ")]
    public void DiffToolFromEnvironmentVariable_IgnoresTheCase(string value)
    {
        using var _ = new EnvironmentVariableScope("DiffEngine_Tool", value);

        Assert.Same(MergeTool.Rider, MergeTools.MergeToolFromEnvironment.GetTool());
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

    private sealed class ThrowingMergeTool : MergeTool
    {
        public override MergeToolResult? Start(string currentFilePath, string newFilePath) => throw new InvalidOperationException("The merge tool is broken.");

        public override string ToString() => "BrokenMergeTool";
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
