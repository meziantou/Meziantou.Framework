using System.Security.Cryptography;
using Meziantou.Framework.Pdf.Objects;

namespace Meziantou.Framework.Pdf;

/// <summary>
/// A summary of the structure of a document: its pages, and the graph of the objects reachable from the trailer, with a
/// digest that does not depend on the numbers of the objects.
/// </summary>
/// <remarks>
/// Two documents with the same digest have the same objects, linked the same way, whatever their object numbers, their
/// cross-reference sections and their object streams. The digest ignores the entries of the trailer that describe the
/// file rather than the document (<c>/ID</c>, <c>/Size</c>, <c>/Prev</c>, <c>/XRefStm</c>, <c>/Encrypt</c>), and compares
/// the streams by their encoded data and dictionary.
/// </remarks>
public sealed class PdfDocumentSummary
{
    private static readonly PdfName[] IgnoredTrailerKeys = [PdfNames.ID, PdfNames.Size, PdfNames.Prev, PdfNames.XRefStm, PdfNames.Encrypt];

    private PdfDocumentSummary(IReadOnlyList<PdfPageSummary> pages, int objectCount, IReadOnlyDictionary<PdfObjectKind, int> objectKinds, IReadOnlyDictionary<string, int> objectTypes, string digest)
    {
        Pages = pages;
        ObjectCount = objectCount;
        ObjectKinds = objectKinds;
        ObjectTypes = objectTypes;
        Digest = digest;
    }

    /// <summary>Gets the summaries of the pages, in order.</summary>
    public IReadOnlyList<PdfPageSummary> Pages { get; }

    /// <summary>Gets the number of indirect objects reachable from the trailer.</summary>
    public int ObjectCount { get; }

    /// <summary>Gets the number of indirect objects reachable from the trailer, by kind.</summary>
    public IReadOnlyDictionary<PdfObjectKind, int> ObjectKinds { get; }

    /// <summary>Gets the number of indirect dictionaries and streams reachable from the trailer, by their <c>/Type</c> (the dictionaries without type are not counted).</summary>
    public IReadOnlyDictionary<string, int> ObjectTypes { get; }

    /// <summary>Gets the SHA-256 digest of the object graph, in hexadecimal, independent of the object numbers.</summary>
    public string Digest { get; }

    /// <summary>Summarizes a document.</summary>
    /// <param name="document">The document.</param>
    /// <returns>The summary.</returns>
    /// <exception cref="PdfException">The page tree of the document cannot be read.</exception>
    public static PdfDocumentSummary Create(PdfDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        var pages = new List<PdfPageSummary>(document.Pages.Count);
        foreach (var page in document.Pages)
        {
            pages.Add(PdfPageSummary.Create(page));
        }

        var walker = new GraphWalker();
        walker.Walk(document.Trailer);
        return new PdfDocumentSummary(pages, walker.ObjectCount, walker.Kinds, walker.Types, walker.GetDigest());
    }

    /// <summary>Walks the object graph breadth first, numbering the indirect objects in the order they are reached.</summary>
    private sealed class GraphWalker
    {
        private readonly Dictionary<PdfReference, int> _ids = [];
        private readonly Queue<PdfReference> _pending = new();
        private readonly IncrementalHash _hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        private readonly byte[] _buffer = new byte[16];

        public int ObjectCount => _ids.Count;

        public Dictionary<PdfObjectKind, int> Kinds { get; } = [];

        public SortedDictionary<string, int> Types { get; } = new(StringComparer.Ordinal);

        public void Walk(PdfDictionary trailer)
        {
            AppendTag('T');
            foreach (var (key, value) in trailer)
            {
                if (Array.IndexOf(IgnoredTrailerKeys, key) >= 0)
                    continue;

                AppendName(key);
                Append(value, depth: 0);
            }

            while (_pending.TryDequeue(out var reference))
            {
                PdfObject value;
                try
                {
                    value = reference.Resolve();
                }
                catch (PdfException)
                {
                    value = PdfNull.Instance;
                }

                AppendTag('O');
                AppendInt32(_ids[reference]);
                Kinds[value.Kind] = Kinds.GetValueOrDefault(value.Kind) + 1;
                var dictionary = value switch
                {
                    PdfDictionary d => d,
                    PdfStream s => s.Dictionary,
                    _ => null,
                };
                if (dictionary?.Get<PdfName>(PdfNames.Type) is { } type)
                {
                    Types[type.Value] = Types.GetValueOrDefault(type.Value) + 1;
                }

                Append(value, depth: 0);
            }
        }

        public string GetDigest() => Convert.ToHexStringLower(_hash.GetHashAndReset());

        private void Append(PdfObject value, int depth)
        {
            if (depth > 256)
            {
                AppendTag('!');
                return;
            }

            switch (value)
            {
                case PdfReference reference:
                    if (!_ids.TryGetValue(reference, out var id))
                    {
                        id = _ids.Count;
                        _ids.Add(reference, id);
                        _pending.Enqueue(reference);
                    }

                    AppendTag('R');
                    AppendInt32(id);
                    break;

                case PdfNull:
                    AppendTag('n');
                    break;

                case PdfBoolean boolean:
                    AppendTag(boolean.Value ? 't' : 'f');
                    break;

                case PdfInteger integer:
                    AppendTag('i');
                    BitConverter.TryWriteBytes(_buffer, integer.Value);
                    _hash.AppendData(_buffer, 0, 8);
                    break;

                case PdfReal real:
                    AppendTag('r');
                    BitConverter.TryWriteBytes(_buffer, real.Value);
                    _hash.AppendData(_buffer, 0, 8);
                    break;

                case PdfName name:
                    AppendName(name);
                    break;

                case PdfString text:
                    AppendTag('s');
                    AppendBytes(text.Bytes.Span);
                    break;

                case PdfArray array:
                    AppendTag('[');
                    AppendInt32(array.Count);
                    foreach (var item in array)
                    {
                        Append(item, depth + 1);
                    }

                    break;

                case PdfDictionary dictionary:
                    AppendDictionary(dictionary, depth);
                    break;

                case PdfStream stream:
                    AppendTag('S');
                    AppendDictionary(stream.Dictionary, depth);
                    AppendBytes(stream.EncodedData.Span);
                    break;
            }
        }

        private void AppendDictionary(PdfDictionary dictionary, int depth)
        {
            AppendTag('<');
            AppendInt32(dictionary.Count);
            foreach (var (key, item) in dictionary)
            {
                AppendName(key);
                Append(item, depth + 1);
            }
        }

        private void AppendName(PdfName name)
        {
            AppendTag('/');
            AppendBytes(System.Text.Encoding.UTF8.GetBytes(name.Value));
        }

        private void AppendBytes(ReadOnlySpan<byte> bytes)
        {
            AppendInt32(bytes.Length);
            _hash.AppendData(bytes);
        }

        private void AppendTag(char tag)
        {
            _buffer[0] = (byte)tag;
            _hash.AppendData(_buffer, 0, 1);
        }

        private void AppendInt32(int value)
        {
            BitConverter.TryWriteBytes(_buffer, value);
            _hash.AppendData(_buffer, 0, 4);
        }
    }
}
