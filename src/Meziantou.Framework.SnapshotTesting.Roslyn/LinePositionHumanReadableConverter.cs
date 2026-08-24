using Meziantou.Framework.HumanReadable;
using Microsoft.CodeAnalysis.Text;

namespace Meziantou.Framework.SnapshotTesting.Roslyn;

internal sealed class LinePositionHumanReadableConverter : HumanReadableConverter<LinePosition>
{
    protected override void WriteValue(HumanReadableTextWriter writer, LinePosition value, HumanReadableSerializerOptions options)
        => writer.WriteValue(RoslynFormatter.FormatPosition(value));
}
