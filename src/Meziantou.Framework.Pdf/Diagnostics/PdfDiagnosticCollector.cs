namespace Meziantou.Framework.Pdf;

/// <summary>
/// Collects the diagnostics of a document as the reader finds them. A problem is reported once per kind, object and
/// offset (a damaged object can be parsed several times), and the number of diagnostics is bounded. The pages can be read
/// on several threads: the collector is thread-safe.
/// </summary>
internal sealed class PdfDiagnosticCollector
{
    internal const int MaxDiagnostics = 1000;

    private readonly Lock _lock = new();
    private readonly List<PdfDiagnostic> _items = [];
    private readonly HashSet<(PdfDiagnosticKind Kind, long? Offset, int? ObjectNumber)> _keys = [];
    private readonly HashSet<object> _sources = new(ReferenceEqualityComparer.Instance);
    private PdfDiagnostic[] _snapshot = [];

    public IReadOnlyList<PdfDiagnostic> Items
    {
        get
        {
            lock (_lock)
            {
                if (_snapshot.Length != _items.Count)
                {
                    _snapshot = [.. _items];
                }

                return _snapshot;
            }
        }
    }

    public void Report(PdfDiagnosticKind kind, string message, long? offset = null, int? objectNumber = null)
    {
        lock (_lock)
        {
            if (_items.Count < MaxDiagnostics && _keys.Add((kind, offset, objectNumber)))
            {
                _items.Add(new PdfDiagnostic(kind, message, offset, objectNumber));
            }
        }
    }

    /// <summary>Reports a problem of an object that has no number nor offset, such as a stream: once per object.</summary>
    public void Report(object source, PdfDiagnosticKind kind, string message)
    {
        lock (_lock)
        {
            if (_items.Count < MaxDiagnostics && _sources.Add(source))
            {
                _items.Add(new PdfDiagnostic(kind, message, offset: null, objectNumber: null));
            }
        }
    }
}
