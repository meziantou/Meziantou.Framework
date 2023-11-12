namespace Meziantou.Framework.InlineSnapshotTesting.Samples;

public sealed class Srub_replace_line_content
{
    [Fact]
    public void Demo()
    {
        var settings = InlineSnapshotSettings.Default with { }; // clone the default settings

        // Replace content of a line
        settings.ScrubLinesWithReplace(line => line.Replace("abc", "123", StringComparison.Ordinal));

        InlineSnapshot.WithSettings(settings).Validate(
            """
            line 1
            line abc
            line 3
            """,
            """
            line 1
            line 123
            line 3
            """);
    }
}