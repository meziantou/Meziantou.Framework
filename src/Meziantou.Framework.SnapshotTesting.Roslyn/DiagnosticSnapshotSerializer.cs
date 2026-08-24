using Microsoft.CodeAnalysis;

namespace Meziantou.Framework.SnapshotTesting.Roslyn;

internal sealed class DiagnosticSnapshotSerializer : ISnapshotSerializer
{
    public static ISnapshotSerializer Instance { get; } = new DiagnosticSnapshotSerializer();

    public bool TrySerialize(SnapshotType type, object? value, [NotNullWhen(true)] out SerializedSnapshot? result)
    {
        // The order is the caller's: unlike the generators of a driver run, an assertion on a list of
        // diagnostics is usually about the order they were reported in. Sort them before validating
        // when the source of the list does not guarantee one.
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
