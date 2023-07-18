using Meziantou.Framework.HumanReadable;
using Meziantou.Framework.InlineSnapshotTesting.Scrubbers.HumanReadableSerializerScrubbers;

namespace Meziantou.Framework.InlineSnapshotTesting;

public static class HumanReadableSerializerScrubExtensions
{
    public static void ScrubGuid(this HumanReadableSerializerOptions options)
    {
        options.Converters.Add(new ScrubbedGuidConverter());
    }
}
