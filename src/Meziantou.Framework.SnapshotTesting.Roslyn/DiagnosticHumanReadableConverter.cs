using System.Diagnostics;
using Meziantou.Framework.HumanReadable;
using Microsoft.CodeAnalysis;

namespace Meziantou.Framework.SnapshotTesting.Roslyn;

internal sealed class DiagnosticHumanReadableConverter : HumanReadableConverter<Diagnostic>
{
    protected override void WriteValue(HumanReadableTextWriter writer, Diagnostic? value, HumanReadableSerializerOptions options)
    {
        Debug.Assert(value is not null);
        writer.WriteValue(RoslynFormatter.FormatDiagnostic(value));
    }
}
