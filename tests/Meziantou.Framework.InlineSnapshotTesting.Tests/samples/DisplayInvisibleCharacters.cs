using Meziantou.Framework.InlineSnapshotTesting.Serialization;

namespace Meziantou.Framework.InlineSnapshotTesting.Samples;

public sealed class DisplayInvisibleCharacters
{
    [Fact]
    public void Demo()
    {
        var settings = InlineSnapshotSettings.Default with
        {
            SnapshotSerializer = new HumanReadableSnapshotSerializer(options => options.ShowInvisibleCharactersInValues = true),
        };

        // Invisible characters are visible in the snapshot
        InlineSnapshot.WithSettings(settings).Validate("line 1\r\nline\t2", """
            line␠1␍␊
            line␉2
            """);
    }
}