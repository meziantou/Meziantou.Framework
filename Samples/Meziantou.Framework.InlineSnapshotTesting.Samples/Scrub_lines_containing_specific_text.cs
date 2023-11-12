namespace Meziantou.Framework.InlineSnapshotTesting.Samples;

public sealed class Scrub_lines_containing_specific_text
{
    [Fact]
    public void Demo()
    {
        var settings = InlineSnapshotSettings.Default with { }; // clone the default settings

        // Remove all lines containing "line 2"
        settings.ScrubLinesContaining(StringComparison.Ordinal, "line 2");

        InlineSnapshot.WithSettings(settings).Validate(
            """
            line 1
            To be removed: line 2
            line 3
            """,
            """
            line 1
            line 3
            """);
    }
}