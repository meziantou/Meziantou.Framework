using System.Runtime.InteropServices;
using Meziantou.Framework.HumanReadable;

namespace Meziantou.Framework.InlineSnapshotTesting.Scrubbers.HumanReadableSerializerScrubbers;
internal sealed class ScrubbedGuidConverter : HumanReadableConverter<Guid>
{
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
#if NETSTANDARD2_0 || NET472 || NET48
        var data = new byte[16];
        MemoryMarshal.Write(data.AsSpan(12), ref index);
        data.AsSpan(12).Reverse();
#else
        Span<byte> data = stackalloc byte[16];
#if NET8_0_OR_GREATER
        MemoryMarshal.Write(data.Slice(12), in index);
#else
        MemoryMarshal.Write(data.Slice(12), ref index);
#endif
        data.Slice(12).Reverse();
#endif
        return new Guid(data).ToString();
    }
}
