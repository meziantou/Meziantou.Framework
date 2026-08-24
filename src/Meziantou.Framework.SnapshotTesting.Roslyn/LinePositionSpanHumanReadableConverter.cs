using Meziantou.Framework.HumanReadable;
using Microsoft.CodeAnalysis.Text;

namespace Meziantou.Framework.SnapshotTesting.Roslyn;

internal sealed class LinePositionSpanHumanReadableConverter : HumanReadableConverter<LinePositionSpan>
{
    protected override void WriteValue(HumanReadableTextWriter writer, LinePositionSpan value, HumanReadableSerializerOptions options)
        => writer.WriteValue(RoslynFormatter.FormatSpan(value));
}
