using Meziantou.Framework.Pdf.Content;
using Meziantou.Framework.Pdf.Objects;
using Meziantou.Framework.Pdf.Text;

namespace Meziantou.Framework.Pdf;

/// <summary>A page of a document (ISO 32000-2, 7.7.3.3). The members read and write the page dictionary; the inheritable entries are looked up in the page tree.</summary>
public sealed class PdfPage
{
    private const int MaxInheritanceDepth = 64;
    private static readonly PdfRectangle DefaultMediaBox = new(0, 0, 612, 792);

    private PdfAnnotationCollection? _annotations;
    private bool _existingContentIsolated;

    internal PdfPage(PdfDocument document, PdfReference reference, PdfDictionary dictionary)
    {
        Document = document;
        Reference = reference;
        Dictionary = dictionary;
    }

    /// <summary>Gets the document of the page.</summary>
    public PdfDocument Document { get; }

    /// <summary>Gets the reference to the page object.</summary>
    public PdfReference Reference { get; }

    /// <summary>Gets the page dictionary.</summary>
    public PdfDictionary Dictionary { get; }

    /// <summary>Gets or sets the media box: the boundaries of the medium the page is printed on. It is inherited from the page tree; the default is US Letter.</summary>
    public PdfRectangle MediaBox
    {
        get => PdfRectangle.TryRead(GetInherited(PdfNames.MediaBox) as PdfArray, out var box) ? box : DefaultMediaBox;
        set => Dictionary[PdfNames.MediaBox] = value.ToArray();
    }

    /// <summary>Gets or sets the crop box: the region of the page that is displayed or printed. It is inherited from the page tree; the default is the media box.</summary>
    public PdfRectangle CropBox
    {
        get => PdfRectangle.TryRead(GetInherited(PdfNames.CropBox) as PdfArray, out var box) ? box : MediaBox;
        set => Dictionary[PdfNames.CropBox] = value.ToArray();
    }

    /// <summary>Gets or sets the clockwise rotation of the page when it is displayed: 0, 90, 180 or 270 degrees. It is inherited from the page tree.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is not a multiple of 90.</exception>
    public int Rotation
    {
        get
        {
            var value = GetInherited(PdfNames.Rotate) is PdfNumber number ? (long)number.DoubleValue : 0;
            if (value % 90 != 0)
                return 0;

            return (int)(((value % 360) + 360) % 360);
        }

        set
        {
            if (value % 90 != 0)
                throw new ArgumentOutOfRangeException(nameof(value), value, "The rotation must be a multiple of 90");

            Dictionary[PdfNames.Rotate] = PdfInteger.Get(((value % 360) + 360) % 360);
        }
    }

    /// <summary>Gets the size of the user space unit in 1/72 inch (ISO 32000-2, 14.11.2). The default is 1.</summary>
    public double UserUnit => Dictionary.GetNumber(PdfNames.UserUnit, 1);

    /// <summary>Gets the resource dictionary of the page, inherited from the page tree, or <see langword="null"/> when there is none.</summary>
    public PdfDictionary? Resources => GetInherited(PdfNames.Resources) as PdfDictionary;

    /// <summary>Gets the annotations of the page (ISO 32000-2, 12.5).</summary>
    public PdfAnnotationCollection Annotations => _annotations ??= new PdfAnnotationCollection(this);

    /// <summary>Gets the content of the page: its content streams decoded and joined (ISO 32000-2, 7.8.2).</summary>
    /// <returns>The content.</returns>
    public byte[] GetContentData()
    {
        var output = new MemoryStream();
        foreach (var stream in GetContentStreams())
        {
            if (output.Length > 0)
            {
                output.WriteByte((byte)'\n');
            }

            output.Write(stream.GetDecodedData());
        }

        return output.ToArray();
    }

    /// <summary>
    /// Creates a builder that appends a content stream to the page. When the page already has content, it is wrapped
    /// between <c>q</c> and <c>Q</c> first, so that the new content starts with the default graphics state.
    /// </summary>
    /// <returns>The builder.</returns>
    public PdfContentBuilder CreateContentBuilder()
    {
        var streams = new List<PdfObject>();
        switch (Dictionary[PdfNames.Contents])
        {
            case PdfArray array:
                streams.AddRange(array);
                break;
            case PdfReference reference when reference.Resolve() is PdfArray array:
                streams.AddRange(array);
                break;
            case PdfReference reference:
                streams.Add(reference);
                break;
        }

        var stream = new PdfStream();
        if (streams.Count > 0 && !_existingContentIsolated)
        {
            var save = new PdfStream();
            save.AppendData("q\n"u8);
            streams.Insert(0, Document.Objects.Add(save));
            stream.AppendData("Q\n"u8);
        }

        _existingContentIsolated = true;
        var streamReference = Document.Objects.Add(stream);
        streams.Add(streamReference);
        Dictionary[PdfNames.Contents] = streams.Count == 1 ? streamReference : new PdfArray(streams);
        return new PdfContentBuilder(Document, GetOrCreateResources(), stream);
    }

    /// <summary>Extracts the text of the page, in the order of the content, with a line break between the lines.</summary>
    /// <returns>The text.</returns>
    public string ExtractText() => TextExtractor.Extract(this);

    internal IEnumerable<PdfStream> GetContentStreams()
    {
        switch (Dictionary.GetResolved(PdfNames.Contents))
        {
            case PdfStream stream:
                yield return stream;
                break;
            case PdfArray array:
                for (var i = 0; i < array.Count; i++)
                {
                    if (array.GetResolved(i) is PdfStream item)
                        yield return item;
                }

                break;
        }
    }

    /// <summary>Gets the resource dictionary of the page itself, copying the inherited one when needed, so that resources can be added to it.</summary>
    internal PdfDictionary GetOrCreateResources()
    {
        if (Dictionary.Get<PdfDictionary>(PdfNames.Resources) is { } own)
            return own;

        var resources = new PdfDictionary();
        if (GetInherited(PdfNames.Resources) is PdfDictionary inherited)
        {
            foreach (var entry in inherited)
            {
                resources[entry.Key] = PdfReference.ResolveIfReference(entry.Value) is PdfDictionary category ? category.CloneShallow() : entry.Value;
            }
        }

        Dictionary[PdfNames.Resources] = resources;
        return resources;
    }

    internal PdfObject? GetInherited(PdfName key)
    {
        var node = Dictionary;
        for (var depth = 0; depth < MaxInheritanceDepth && node is not null; depth++)
        {
            if (node.GetResolved(key) is { } value)
                return value;

            node = node.Get<PdfDictionary>(PdfNames.Parent);
        }

        return null;
    }
}
