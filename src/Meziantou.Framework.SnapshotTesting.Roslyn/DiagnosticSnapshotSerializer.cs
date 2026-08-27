using Microsoft.CodeAnalysis;

namespace Meziantou.Framework.SnapshotTesting.Roslyn;

internal sealed class DiagnosticSnapshotSerializer : ISnapshotSerializer
{
    public static ISnapshotSerializer Instance { get; } = new DiagnosticSnapshotSerializer();

    public bool TrySerialize(SnapshotType type, object? value, [NotNullWhen(true)] out SerializedSnapshot? result)
    {
        // Diagnostics are written in the order they are given. Unlike the generators of a driver run, whose
        // order is not a contract, an assertion on a list of diagnostics is usually about the order they were
        // reported in, so this serializer deliberately does not reorder them. A caller whose source does not
        // guarantee an order is the one that should sort before calling.
        IEnumerable<Diagnostic>? diagnostics = value switch
        {
            Diagnostic diagnostic => [diagnostic],
            IEnumerable<Diagnostic> collection => collection,
            _ => null,
        };

        if (diagnostics is null)
        {
            result = null;
            return false;
        }

        var report = new StringBuilder();
        foreach (var diagnostic in diagnostics)
        {
            RoslynFormatter.AppendDiagnostic(report, diagnostic);
            report.Append('\n');
        }

        // An empty file is the point when no diagnostic is expected, so it is not skipped here.
        result = new SerializedSnapshot([new SnapshotData(Extension: null, Encoding.UTF8.GetBytes(report.ToString()))]);
        return true;
    }
}
