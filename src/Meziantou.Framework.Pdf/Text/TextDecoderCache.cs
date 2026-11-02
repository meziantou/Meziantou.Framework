using Meziantou.Framework.Pdf.Objects;

namespace Meziantou.Framework.Pdf.Text;

/// <summary>
/// The decoders of the fonts of a document, shared by the extraction of its pages: the pages that use the same font
/// parse its ToUnicode CMap and its widths once.
/// </summary>
/// <remarks>
/// The objects of a document can change: each decoder keeps a snapshot of the objects it was read from (the font
/// dictionary and what it refers to, the version of the data of the streams), and is created again when they differ.
/// The snapshot compares the objects by reference, which costs a fraction of reading them again.
/// </remarks>
internal sealed class TextDecoderCache
{
    private const int MaxFonts = 256;

    // The objects a snapshot may hold: a font with more (a huge /W array) is decoded for each page
    private const int MaxSnapshotLength = 1 << 18;

    private const int MaxSnapshotDepth = 16;

    // The entries the decoders do not read: the font programs, the glyph procedures of Type 3 fonts and their resources
    private static readonly HashSet<PdfName> IgnoredKeys = [PdfNames.FontFile, PdfNames.FontFile2, PdfNames.FontFile3, PdfNames.CharProcs, PdfNames.Resources, PdfNames.Metadata];

    private readonly Dictionary<PdfDictionary, (PdfFontDecoder Decoder, (object? Value, int Version)[] Snapshot)> _decoders = new(ReferenceEqualityComparer.Instance);
    private readonly List<(object? Value, int Version)> _scratch = [];
    private readonly HashSet<PdfObject> _visited = new(ReferenceEqualityComparer.Instance);

    public PdfFontDecoder Get(PdfDictionary font)
    {
        var snapshot = TakeSnapshot(font);
        if (_decoders.TryGetValue(font, out var entry) && snapshot && entry.Snapshot.AsSpan().SequenceEqual(System.Runtime.InteropServices.CollectionsMarshal.AsSpan(_scratch), SnapshotComparer.Instance))
            return entry.Decoder;

        var decoder = PdfFontDecoder.Create(font);
        if (snapshot)
        {
            if (_decoders.Count >= MaxFonts)
            {
                _decoders.Clear();
            }

            _decoders[font] = (decoder, [.. _scratch]);
        }
        else
        {
            _decoders.Remove(font);
        }

        return decoder;
    }

    /// <summary>Lists the objects reachable from the font dictionary in a deterministic order into <see cref="_scratch"/>; <see langword="false"/> when the font is too large to be compared.</summary>
    private bool TakeSnapshot(PdfDictionary font)
    {
        _scratch.Clear();
        _visited.Clear();
        try
        {
            return Add(font, depth: 0);
        }
        catch (Exception ex) when (ex is InvalidPdfContentException or UnsupportedPdfFeatureException or PdfResourceLimitException)
        {
            return false;
        }
    }

    private bool Add(PdfObject value, int depth)
    {
        if (_scratch.Count >= MaxSnapshotLength || depth > MaxSnapshotDepth)
            return false;

        if (value is PdfReference reference)
        {
            // The reference and the object it resolves to: an object replaced in the document is another object
            _scratch.Add((reference, 0));
            value = PdfReference.ResolveIfReference(reference);
        }

        _scratch.Add((value, value is PdfStream data ? data.DataVersion : 0));
        if (value is not (PdfDictionary or PdfArray or PdfStream) || !_visited.Add(value))
            return true;

        switch (value)
        {
            case PdfStream stream:
                return Add(stream.Dictionary, depth + 1);
            case PdfDictionary dictionary:
                _scratch.Add((null, dictionary.Count));
                foreach (var (key, item) in dictionary)
                {
                    _scratch.Add((key, 0));
                    if (IgnoredKeys.Contains(key))
                    {
                        _scratch.Add((item, 0));
                    }
                    else if (!Add(item, depth + 1))
                    {
                        return false;
                    }
                }

                return true;
            case PdfArray array:
                _scratch.Add((null, array.Count));
                for (var i = 0; i < array.Count; i++)
                {
                    if (!Add(array[i], depth + 1))
                        return false;
                }

                return true;
        }

        return true;
    }

    private sealed class SnapshotComparer : IEqualityComparer<(object? Value, int Version)>
    {
        public static SnapshotComparer Instance { get; } = new();

        public bool Equals((object? Value, int Version) x, (object? Value, int Version) y) => ReferenceEquals(x.Value, y.Value) && x.Version == y.Version;

        public int GetHashCode((object? Value, int Version) obj) => HashCode.Combine(System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(obj.Value), obj.Version);
    }
}
