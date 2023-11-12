namespace Meziantou.Framework.InlineSnapshotTesting.Samples;

public sealed class Scrub_lines_matching_a_regex
{
    [Fact]
    public void Demo()
    {
        var settings = InlineSnapshotSettings.Default with { }; // clone the default settings

        // Remove all lines matching the regex "\d{2,}" (2 or more consecutive digits)
        settings.ScrubLinesMatching(@"\d{2,}");

        InlineSnapshot.WithSettings(settings).Validate(
            """
            line 1
            To be removed as it matches the regex: line 123
            line 3
            """,
            """
            line 1
            line 3
            """);
    }
}