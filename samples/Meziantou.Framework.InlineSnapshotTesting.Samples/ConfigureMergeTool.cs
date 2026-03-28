namespace Meziantou.Framework.InlineSnapshotTesting.Samples;

public sealed class ConfigureMergeTool
{
    [Fact]
    public void Demo()
    {
        var settings = InlineSnapshotSettings.Default with
        {
            MergeTools = [MergeTool.GitMergeTool],
        };

        // Invisible characters are visible in the snapshot
        InlineSnapshot.WithSettings(settings).Validate("line 1", "line 1");
    }
}