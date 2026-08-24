using System.Diagnostics;
using Meziantou.Framework.HumanReadable;
using Microsoft.CodeAnalysis;

namespace Meziantou.Framework.SnapshotTesting.Roslyn;

internal sealed class LocationHumanReadableConverter : HumanReadableConverter<Location>
{
    protected override void WriteValue(HumanReadableTextWriter writer, Location? value, HumanReadableSerializerOptions options)
    {
        Debug.Assert(value is not null);
        if (!value.IsInSource)
        {
            writer.WriteValue(value.Kind.ToString());
            return;
        }

        var lineSpan = value.GetLineSpan();
        writer.WriteValue(lineSpan.Path + RoslynFormatter.FormatSpan(lineSpan.Span));
    }
}
