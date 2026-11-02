using Meziantou.Framework.Pdf.Objects;

namespace Meziantou.Framework.Pdf;

/// <summary>
/// Copies objects of a document into another one: the indirect objects they reach are copied once (an object imported
/// again is shared), with their references renumbered in the target. The copy stops at the pages: a reference to a page
/// of the source becomes the reference to its copy, as soon as the page is imported, or the null object.
/// </summary>
internal sealed class PdfObjectImporter
{
    private const int MaxDepth = 256;

    private readonly PdfDocument _source;
    private readonly PdfDocument _target;
    private readonly Dictionary<PdfReference, PdfReference> _references = [];
    private readonly Dictionary<PdfReference, List<Action<PdfReference>>> _pendingPages = [];

    public PdfObjectImporter(PdfDocument source, PdfDocument target)
    {
        _source = source;
        _target = target;
    }

    /// <summary>Records the copy of a page, so that the references to the source page become references to it.</summary>
    public void MapPage(PdfReference sourcePage, PdfReference targetPage)
    {
        _references[sourcePage] = targetPage;
        if (_pendingPages.Remove(sourcePage, out var fixups))
        {
            foreach (var fixup in fixups)
            {
                fixup(targetPage);
            }
        }
    }

    /// <summary>Copies an object. A direct object is copied, an indirect object is copied once.</summary>
    public PdfObject Copy(PdfObject value) => Copy(value, depth: 0, fixup: null);

    private PdfObject Copy(PdfObject value, int depth, Action<PdfReference>? fixup)
    {
        if (depth > MaxDepth)
            throw new InvalidPdfContentException("The objects of the page are nested too deeply to be imported");

        switch (value)
        {
            case PdfReference reference:
                return CopyReference(reference, depth, fixup);

            case PdfDictionary dictionary:
                var copy = new PdfDictionary(dictionary.Count);
                CopyEntries(dictionary, copy, depth);
                return copy;

            case PdfArray array:
                var items = new PdfArray();
                for (var i = 0; i < array.Count; i++)
                {
                    var index = i;
                    items.Add(Copy(array[i], depth + 1, target => items[index] = target));
                }

                return items;

            case PdfStream stream:
                var streamCopy = new PdfStream();
                FillStream(stream, streamCopy, depth);
                return streamCopy;

            default:
                // Names, numbers, strings, booleans and null are not changed once created
                return value;
        }
    }

    private PdfObject CopyReference(PdfReference reference, int depth, Action<PdfReference>? fixup)
    {
        if (_references.TryGetValue(reference, out var existing))
            return existing;

        var resolved = reference.Resolve();
        if (resolved is PdfDictionary pageNode && (pageNode.IsType(PdfNames.Page) || pageNode.IsType(PdfNames.Pages)))
        {
            // A page that is not imported yet: the reference is set when it is
            if (fixup is not null && pageNode.IsType(PdfNames.Page))
            {
                if (!_pendingPages.TryGetValue(reference, out var fixups))
                {
                    fixups = [];
                    _pendingPages[reference] = fixups;
                }

                fixups.Add(fixup);
            }

            return PdfNull.Instance;
        }

        switch (resolved)
        {
            case PdfDictionary dictionary:
            {
                // Registered before its entries are copied, so that a cycle reaches the copy
                var copy = new PdfDictionary(dictionary.Count);
                var target = _target.Objects.Add(copy);
                _references[reference] = target;
                CopyEntries(dictionary, copy, depth);
                return target;
            }

            case PdfStream stream:
            {
                var copy = new PdfStream();
                var target = _target.Objects.Add(copy);
                _references[reference] = target;
                FillStream(stream, copy, depth);
                return target;
            }

            case PdfArray array:
            {
                var copy = new PdfArray();
                var target = _target.Objects.Add(copy);
                _references[reference] = target;
                for (var i = 0; i < array.Count; i++)
                {
                    var index = i;
                    copy.Add(Copy(array[i], depth + 1, page => copy[index] = page));
                }

                return target;
            }

            case PdfNull:
                return PdfNull.Instance;

            default:
            {
                var target = _target.Objects.Add(resolved);
                _references[reference] = target;
                return target;
            }
        }
    }

    private void CopyEntries(PdfDictionary source, PdfDictionary target, int depth)
    {
        foreach (var (key, value) in source)
        {
            var entryKey = key;
            target[key] = Copy(value, depth + 1, page => target[entryKey] = page);
        }
    }

    private void FillStream(PdfStream source, PdfStream target, int depth)
    {
        CopyEntries(source.Dictionary, target.Dictionary, depth);

        // The data, decrypted when the source was read, is kept encoded with its filters
        var encoded = source.EncodedData;
        target.SetData(encoded, compressible: source.IsCompressible && !source.Dictionary.ContainsKey(PdfNames.Filter));
        target.DecodeLimits = source.DecodeLimits;
    }

    /// <summary>Gets the importer of a source document into a target document: the same for every import, so that the objects imported several times are shared.</summary>
    public static PdfObjectImporter Get(PdfDocument source, PdfDocument target)
    {
        // The objects of the embedded fonts of a document are written when it is saved: the import needs them now
        foreach (var font in source.EmbeddedFonts.Values)
        {
            font.Write(source);
        }

        if (!target.Importers.TryGetValue(source, out var importer))
        {
            importer = new PdfObjectImporter(source, target);
            target.Importers.Add(source, importer);
        }

        return importer;
    }

    /// <summary>Gets the source document.</summary>
    public PdfDocument Source => _source;
}
