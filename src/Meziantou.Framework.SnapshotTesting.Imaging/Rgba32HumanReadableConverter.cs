using Meziantou.Framework.HumanReadable;
using Meziantou.Framework.Imaging;

namespace Meziantou.Framework.SnapshotTesting.Imaging;

internal sealed class Rgba32HumanReadableConverter : HumanReadableConverter<Rgba32>
{
    protected override void WriteValue(HumanReadableTextWriter writer, Rgba32 value, HumanReadableSerializerOptions options)
        => writer.WriteValue($"#{value.R:X2}{value.G:X2}{value.B:X2}{value.A:X2}");
}
