using Meziantou.Framework.Pdf.Objects;

namespace Meziantou.Framework.Pdf.Optimization;

/// <summary>
/// The objects reachable from the trailer of a document: the indirect objects reached, and the containers of each
/// dictionary and stream (the dictionaries, streams and arrays that hold it, directly or through a reference).
/// </summary>
internal sealed class ObjectGraph
{
    private const int MaxDepth = 512;

    private readonly Dictionary<PdfObject, List<PdfObject>> _containers = new(ReferenceEqualityComparer.Instance);

    private ObjectGraph()
    {
    }

    /// <summary>Gets the numbers of the indirect objects reached.</summary>
    public HashSet<int> ReachedObjectNumbers { get; } = [];

    /// <summary>Gets the streams reached, in the order they are reached, with their reference when they are indirect.</summary>
    public List<(PdfStream Stream, PdfReference? Reference)> Streams { get; } = [];

    public static ObjectGraph Build(PdfDocument document)
    {
        var graph = new ObjectGraph();
        var visited = new HashSet<PdfObject>(ReferenceEqualityComparer.Instance);
        var pending = new Stack<(PdfObject Value, PdfObject? Container, PdfReference? Reference, int Depth)>();
        pending.Push((document.Trailer, null, null, 0));
        while (pending.TryPop(out var item))
        {
            var value = item.Value;
            var reference = item.Reference;
            if (value is PdfReference indirect)
            {
                graph.ReachedObjectNumbers.Add(indirect.ObjectNumber);
                reference = indirect;
                try
                {
                    value = indirect.Resolve();
                }
                catch (PdfException)
                {
                    continue;
                }
            }

            if (value is not (PdfDictionary or PdfArray or PdfStream))
                continue;

            if (item.Container is not null)
            {
                graph.AddContainer(value, item.Container);
            }

            if (!visited.Add(value) || item.Depth > MaxDepth)
                continue;

            switch (value)
            {
                case PdfDictionary dictionary:
                    foreach (var (_, child) in dictionary)
                    {
                        pending.Push((child, dictionary, null, item.Depth + 1));
                    }

                    break;

                case PdfStream stream:
                    graph.Streams.Add((stream, reference));
                    foreach (var (_, child) in stream.Dictionary)
                    {
                        pending.Push((child, stream, null, item.Depth + 1));
                    }

                    break;

                case PdfArray array:
                    foreach (var child in array)
                    {
                        pending.Push((child, array, null, item.Depth + 1));
                    }

                    break;
            }
        }

        return graph;
    }

    /// <summary>Gets the containers of a dictionary, a stream or an array: the objects that hold it.</summary>
    public IReadOnlyList<PdfObject> GetContainers(PdfObject value) => _containers.TryGetValue(value, out var list) ? list : [];

    private void AddContainer(PdfObject value, PdfObject container)
    {
        if (!_containers.TryGetValue(value, out var list))
        {
            list = [];
            _containers.Add(value, list);
        }

        if (!list.Contains(container, ReferenceEqualityComparer.Instance))
        {
            list.Add(container);
        }
    }
}
