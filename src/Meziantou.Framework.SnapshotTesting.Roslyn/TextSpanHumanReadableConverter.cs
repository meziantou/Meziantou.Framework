using Meziantou.Framework.HumanReadable;
using Microsoft.CodeAnalysis.Text;

namespace Meziantou.Framework.SnapshotTesting.Roslyn;

internal sealed class TextSpanHumanReadableConverter : HumanReadableConverter<TextSpan>
{
    protected override void WriteValue(HumanReadableTextWriter writer, TextSpan value, HumanReadableSerializerOptions options)
        => writer.WriteValue(RoslynFormatter.FormatSpan(value));
}
