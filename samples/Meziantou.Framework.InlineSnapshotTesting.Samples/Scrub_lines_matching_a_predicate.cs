namespace Meziantou.Framework.InlineSnapshotTesting.Samples;

public sealed class Scrub_lines_matching_a_predicate
{
    [Fact]
    public void Demo()
    {
        var settings = InlineSnapshotSettings.Default with { }; // clone the default settings

        // Remove all lines starting with "To be removed:"
        settings.ScrubLines(line => line.StartsWith("To be removed:", StringComparison.OrdinalIgnoreCase));

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