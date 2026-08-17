using Meziantou.Framework.HumanReadable;
using SkiaSharp;

namespace Meziantou.Framework.SnapshotTesting.SkiaSharp;

internal sealed class SKColorHumanReadableConverter : HumanReadableConverter<SKColor>
{
    protected override void WriteValue(HumanReadableTextWriter writer, SKColor value, HumanReadableSerializerOptions options)
        => writer.WriteValue($"#{value.Red:X2}{value.Green:X2}{value.Blue:X2}{value.Alpha:X2}");
}
