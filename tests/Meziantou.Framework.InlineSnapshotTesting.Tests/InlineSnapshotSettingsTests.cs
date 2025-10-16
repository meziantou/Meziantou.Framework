using Meziantou.Framework.InlineSnapshotTesting.Serialization;
using Meziantou.Framework.InlineSnapshotTesting.SnapshotUpdateStrategies;
using TestUtilities;
using Xunit;

namespace Meziantou.Framework.InlineSnapshotTesting.Tests;
public sealed class InlineSnapshotSettingsTests
{
    [Fact, RunIf(FactOperatingSystem.Windows)]
    public void UpdateStrategy_Windows_Prompt()
    {
        var settings = new InlineSnapshotSettings();
        Assert.IsType<PromptStrategy>(settings.SnapshotUpdateStrategy);
    }

    [Fact, RunIf(FactOperatingSystem.All & ~FactOperatingSystem.Windows)]
    public void UpdateStrategy_NonWindows_Prompt()
    {
        var settings = new InlineSnapshotSettings();
        Assert.IsType<MergeToolStrategy>(settings.SnapshotUpdateStrategy);
    }

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

        var clone = settings with { };

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
}
