using Meziantou.Framework.InlineSnapshotTesting.Serialization;

namespace Meziantou.Framework.InlineSnapshotTesting.Samples;

public sealed class Scrub_guid
{
    [Fact]
    public void Demo()
    {
        var settings = InlineSnapshotSettings.Default with
        {
            SnapshotSerializer = new HumanReadableSnapshotSerializer(options =>
            {
                // Replace guids with incremented values
                options.ScrubGuid();
            }),
        };

        var guid1 = Guid.NewGuid();
        var guid2 = Guid.NewGuid();
        Guid[] guids = [guid1, guid1, guid2];
        InlineSnapshot.WithSettings(settings).Validate(guids,
            """
            - 00000000-0000-0000-0000-000000000001
            - 00000000-0000-0000-0000-000000000001
            - 00000000-0000-0000-0000-000000000002
            """);
    }
}