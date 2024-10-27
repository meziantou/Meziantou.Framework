using System.Globalization;
using Meziantou.Framework.HumanReadable;

namespace Meziantou.Framework.InlineSnapshotTesting.Scrubbers.HumanReadableSerializerScrubbers;

internal sealed class ScrubbedGuidConverter : HumanReadableConverter<Guid>
{
    private const string Prefix = "00000000-0000-0000-0000-";

    protected override void WriteValue(HumanReadableTextWriter writer, Guid value, HumanReadableSerializerOptions options)
    {
        var dict = options.GetOrSetSerializationData(nameof(ScrubbedGuidConverter), () => new Dictionary<Guid, string>());
        if (!dict.TryGetValue(value, out var guid))
        {
            guid = GetScrubbedValue(dict.Count + 1);
            dict.Add(value, guid);
        }

        writer.WriteValue(guid);
    }

    private static string GetScrubbedValue(int index)
    {
#if NET6_0_OR_GREATER
        Span<char> data = stackalloc char[36];
        Prefix.AsSpan().CopyTo(data);
        _ = index.TryFormat(data[Prefix.Length..], out _, "000000000000", CultureInfo.InvariantCulture);
        return data.ToString();
#else
        return Prefix + index.ToString("000000000000", CultureInfo.InvariantCulture);
#endif
    }
}
