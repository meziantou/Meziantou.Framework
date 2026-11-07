using Microsoft.CodeAnalysis;

namespace Meziantou.Framework.SnapshotTesting.Roslyn;

internal sealed class DiagnosticSnapshotSerializer : ISnapshotSerializer
{
    public static ISnapshotSerializer Instance { get; } = new DiagnosticSnapshotSerializer();

    public bool TrySerialize(SnapshotType type, object? value, [NotNullWhen(true)] out SerializedSnapshot? result)
    {
        // Diagnostics are sorted so the snapshot does not depend on the order the caller collected them in.
        // Roslyn makes no ordering guarantee for most of the APIs that produce them, so keeping the incoming
        // order would make the snapshot depend on analyzer scheduling.
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
        foreach (var diagnostic in diagnostics.OrderBy(static diagnostic => diagnostic, RoslynFormatter.DiagnosticComparer))
        {
            RoslynFormatter.AppendDiagnostic(report, diagnostic);
            report.Append('\n');
        }

        // An empty file is the point when no diagnostic is expected, so it is not skipped here.
        result = new SerializedSnapshot([new SnapshotData(Extension: null, Encoding.UTF8.GetBytes(report.ToString()))]);
        return true;
    }
}
