using System.Collections;
using Meziantou.Framework.Pdf.Objects;

namespace Meziantou.Framework.Pdf;

/// <summary>
/// The files embedded in a document: the <c>/EmbeddedFiles</c> name tree of the name dictionary of the catalog
/// (ISO 32000-2, 7.7.4, 7.11.4). The files added are also associated with the document (<c>/AF</c> of the catalog,
/// 14.13), as PDF/A-3 and PDF/A-4f require.
/// </summary>
public sealed class PdfEmbeddedFileCollection : IReadOnlyList<PdfEmbeddedFile>
{
    private static readonly PdfName EmbeddedFilesName = PdfName.Create("EmbeddedFiles");

    private readonly PdfDocument _document;

    internal PdfEmbeddedFileCollection(PdfDocument document)
    {
        _document = document;
    }

    /// <summary>Gets the number of files.</summary>
    public int Count => GetItems().Count;

    /// <summary>Gets a file.</summary>
    /// <param name="index">The index, in the order of the keys of the name tree.</param>
    /// <returns>The file.</returns>
    public PdfEmbeddedFile this[int index] => GetItems()[index];

    /// <summary>Embeds a file in the document.</summary>
    /// <param name="fileName">The name of the file, the key of the name tree: it is unique in the document.</param>
    /// <param name="data">The content of the file.</param>
    /// <param name="options">The attributes of the file, or <see langword="null"/> for the defaults.</param>
    /// <returns>The file.</returns>
    /// <exception cref="ArgumentException"><paramref name="fileName"/> is empty or the name of another file of the document, or the MIME type is not valid.</exception>
    public PdfEmbeddedFile Add(string fileName, ReadOnlySpan<byte> data, PdfEmbeddedFileOptions? options = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(fileName);
        var tree = GetTree(create: false);
        var entries = tree is null ? [] : PdfNameTree.GetEntries(tree);
        if (entries.Any(entry => string.Equals(entry.Key.ToText(), fileName, StringComparison.Ordinal)))
            throw new ArgumentException($"The document already has a file named '{fileName}'", nameof(fileName));

        var (dictionary, reference) = PdfEmbeddedFile.Create(_document, fileName, data, options);
        entries.Add((PdfString.FromText(fileName), reference));
        PdfNameTree.SetEntries(tree ?? GetTree(create: true)!, entries);

        if (_document.Catalog.Get<PdfArray>(PdfEmbeddedFile.AFName) is not { } associated)
        {
            associated = [];
            _document.Catalog[PdfEmbeddedFile.AFName] = associated;
        }

        associated.Add(reference);
        return new PdfEmbeddedFile(_document, dictionary, reference, fileName);
    }

    /// <summary>Removes a file from the document: from the name tree and from the files associated with the document.</summary>
    /// <param name="file">The file.</param>
    /// <returns><see langword="true"/> when the file was a file of the document.</returns>
    public bool Remove(PdfEmbeddedFile file)
    {
        ArgumentNullException.ThrowIfNull(file);
        if (GetTree(create: false) is not { } tree)
            return false;

        var entries = PdfNameTree.GetEntries(tree);
        var index = entries.FindIndex(entry => IsFile(entry.Value, file));
        if (index < 0)
            return false;

        entries.RemoveAt(index);
        if (entries.Count == 0)
        {
            var names = _document.Catalog.Get<PdfDictionary>(PdfNames.Names)!;
            names.Remove(EmbeddedFilesName);
            if (names.Count == 0)
            {
                _document.Catalog.Remove(PdfNames.Names);
            }
        }
        else
        {
            PdfNameTree.SetEntries(tree, entries);
        }

        if (_document.Catalog.Get<PdfArray>(PdfEmbeddedFile.AFName) is { } associated)
        {
            for (var i = associated.Count - 1; i >= 0; i--)
            {
                if (IsFile(associated[i], file))
                {
                    associated.RemoveAt(i);
                }
            }

            if (associated.Count == 0)
            {
                _document.Catalog.Remove(PdfEmbeddedFile.AFName);
            }
        }

        return true;
    }

    /// <summary>Returns an enumerator over the files.</summary>
    /// <returns>The enumerator.</returns>
    public IEnumerator<PdfEmbeddedFile> GetEnumerator() => GetItems().GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    private static bool IsFile(PdfObject value, PdfEmbeddedFile file) => (file.Reference is not null && file.Reference.Equals(value)) || ReferenceEquals(PdfReference.ResolveIfReference(value), file.Dictionary);

    private PdfDictionary? GetTree(bool create)
    {
        if (_document.Catalog.Get<PdfDictionary>(PdfNames.Names) is not { } names)
        {
            if (!create)
                return null;

            names = [];
            _document.Catalog[PdfNames.Names] = names;
        }

        if (names.Get<PdfDictionary>(EmbeddedFilesName) is { } tree)
            return tree;

        if (!create)
            return null;

        tree = [];
        names[EmbeddedFilesName] = _document.Objects.Add(tree);
        return tree;
    }

    private List<PdfEmbeddedFile> GetItems()
    {
        var result = new List<PdfEmbeddedFile>();
        if (GetTree(create: false) is { } tree)
        {
            foreach (var (key, value) in PdfNameTree.GetEntries(tree))
            {
                if (PdfReference.ResolveIfReference(value) is PdfDictionary dictionary)
                {
                    result.Add(new PdfEmbeddedFile(_document, dictionary, value as PdfReference, key.ToText()));
                }
            }
        }

        return result;
    }
}
