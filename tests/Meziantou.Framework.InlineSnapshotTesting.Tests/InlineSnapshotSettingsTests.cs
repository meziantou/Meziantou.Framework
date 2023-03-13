using FluentAssertions;
using Meziantou.Framework.InlineSnapshotTesting.SnapshotUpdateStrategies;
using TestUtilities;

namespace Meziantou.Framework.InlineSnapshotTesting.Tests;
public sealed class InlineSnapshotSettingsTests
{
    [RunIfFact(FactOperatingSystem.Windows)]
    public void UpdateStrategy_Windows_Prompt()
    {
        var settings = new InlineSnapshotSettings();
        settings.SnapshotUpdateStrategy.Should().BeOfType<PromptStrategy>();
    }

    [RunIfFact(FactOperatingSystem.All & ~FactOperatingSystem.Windows)]
    public void UpdateStrategy_NonWindows_Prompt()
    {
        var settings = new InlineSnapshotSettings();
        settings.SnapshotUpdateStrategy.Should().BeOfType<MergeToolStrategy>();
    }
}
