using Meziantou.Framework.Pdf.Content;
using Meziantou.Framework.Pdf.Objects;
using Meziantou.Framework.Pdf.Text;

namespace Meziantou.Framework.Pdf;

/// <summary>A page of a document (ISO 32000-2, 7.7.3.3). The members read and write the page dictionary; the inheritable entries are looked up in the page tree.</summary>
public sealed partial class PdfPage
{
    private const int MaxInheritanceDepth = 64;
    private static readonly PdfRectangle DefaultMediaBox = new(0, 0, 612, 792);

    private static readonly PdfName TrimBoxName = PdfName.Create("TrimBox");
    private static readonly PdfName BleedBoxName = PdfName.Create("BleedBox");
    private static readonly PdfName ArtBoxName = PdfName.Create("ArtBox");

    private PdfAnnotationCollection? _annotations;
    private PdfStream? _saveStream;
    private PdfDictionary? _ownResources;

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
    /// <exception cref="ArgumentException">A coordinate of the value is not a finite number.</exception>
    public PdfRectangle MediaBox
    {
        get => PdfRectangle.TryRead(GetInherited(PdfNames.MediaBox) as PdfArray, out var box) ? box : DefaultMediaBox;
        set => SetBox(PdfNames.MediaBox, value);
    }

    /// <summary>Gets or sets the crop box: the region of the page that is displayed or printed. It is inherited from the page tree; the default is the media box.</summary>
    /// <exception cref="ArgumentException">A coordinate of the value is not a finite number.</exception>
    public PdfRectangle CropBox
    {
        get => PdfRectangle.TryRead(GetInherited(PdfNames.CropBox) as PdfArray, out var box) ? box : MediaBox;
        set => SetBox(PdfNames.CropBox, value);
    }

    /// <summary>
    /// Gets or sets the bleed box (ISO 32000-2, 14.11.2): the region the content is clipped to in a production
    /// environment, which includes the bleed area of the trimmed page. It is not inherited; the default is the crop box.
    /// </summary>
    /// <exception cref="ArgumentException">A coordinate of the value is not a finite number.</exception>
    public PdfRectangle BleedBox
    {
        get => PdfRectangle.TryRead(Dictionary.Get<PdfArray>(BleedBoxName), out var box) ? box : CropBox;
        set => SetBox(BleedBoxName, value);
    }

    /// <summary>Gets or sets the trim box (ISO 32000-2, 14.11.2): the intended dimensions of the finished page after trimming. It is not inherited; the default is the crop box.</summary>
    /// <exception cref="ArgumentException">A coordinate of the value is not a finite number.</exception>
    public PdfRectangle TrimBox
    {
        get => PdfRectangle.TryRead(Dictionary.Get<PdfArray>(TrimBoxName), out var box) ? box : CropBox;
        set => SetBox(TrimBoxName, value);
    }

    /// <summary>Gets or sets the art box (ISO 32000-2, 14.11.2): the extent of the meaningful content of the page. It is not inherited; the default is the crop box.</summary>
    /// <exception cref="ArgumentException">A coordinate of the value is not a finite number.</exception>
    public PdfRectangle ArtBox
    {
        get => PdfRectangle.TryRead(Dictionary.Get<PdfArray>(ArtBoxName), out var box) ? box : CropBox;
        set => SetBox(ArtBoxName, value);
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

    /// <summary>Gets the size of the user space unit in 1/72 inch (ISO 32000-2, 14.11.2). The default is 1, which is also the value of a page whose <c>/UserUnit</c> is not a positive number.</summary>
    public double UserUnit
    {
        get
        {
            var value = Dictionary.GetNumber(PdfNames.UserUnit, 1);
            return double.IsFinite(value) && value > 0 ? value : 1;
        }
    }

    /// <summary>
    /// Gets the size of the page as it is displayed, in 1/72 inch: the visible region (the crop box clipped to the media
    /// box) rotated by <see cref="Rotation"/> and scaled by <see cref="UserUnit"/>.
    /// </summary>
    public PdfSize DisplaySize
    {
        get
        {
            var box = VisibleBox;
            var unit = UserUnit;
            return Rotation is 90 or 270 ? new PdfSize(box.Height * unit, box.Width * unit) : new PdfSize(box.Width * unit, box.Height * unit);
        }
    }

    /// <summary>
    /// Gets the matrix from the display space of the page to its default user space. In the display space, the origin is
    /// the lower-left corner of the page as it is displayed (cropped and rotated), x increases to the right, y upward,
    /// and the unit is 1/72 inch.
    /// </summary>
    /// <remarks>
    /// Concatenate it to the transformation matrix (<see cref="PdfContentBuilder.Transform"/>) to draw on a rotated or
    /// cropped page as on an upright page of <see cref="DisplaySize"/>; transform the points of the display, such as the
    /// position of a click, with it to get the coordinates of the annotations.
    /// </remarks>
    public PdfMatrix DisplayToUserSpace
    {
        get
        {
            var box = VisibleBox;
            var (width, height) = (box.Width, box.Height);
            var rotation = Rotation switch
            {
                90 => new PdfMatrix(0, 1, -1, 0, box.Left + width, box.Bottom),
                180 => new PdfMatrix(-1, 0, 0, -1, box.Left + width, box.Bottom + height),
                270 => new PdfMatrix(0, -1, 1, 0, box.Left, box.Bottom + height),
                _ => new PdfMatrix(1, 0, 0, 1, box.Left, box.Bottom),
            };
            var unit = UserUnit;
            return PdfMatrix.CreateScale(1 / unit, 1 / unit) * rotation;
        }
    }

    /// <summary>Gets the matrix from the default user space of the page to its display space: the inverse of <see cref="DisplayToUserSpace"/>.</summary>
    public PdfMatrix UserSpaceToDisplay
    {
        get
        {
            DisplayToUserSpace.TryInvert(out var result);
            return result;
        }
    }

    /// <summary>Gets the resource dictionary of the page, inherited from the page tree, or <see langword="null"/> when there is none.</summary>
    public PdfDictionary? Resources => GetInherited(PdfNames.Resources) as PdfDictionary;

    /// <summary>Gets the label of the page (ISO 32000-2, 12.4.2), such as <c>iii</c> or <c>A-2</c>, or <see langword="null"/> when the document has no page labels or the page is not in it.</summary>
    public string? Label => Document.Pages.IndexOf(this) is var index and >= 0 ? Document.PageLabels.GetLabel(index) : null;

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
    /// Creates a builder that appends a content stream to the page. When the page already has content, including the
    /// content of the builders created before, it is wrapped between <c>q</c> and <c>Q</c> first, so that the new content
    /// starts with the default graphics state.
    /// </summary>
    /// <returns>The builder.</returns>
    /// <remarks>
    /// The isolation relies on the content of each builder being balanced: call <see cref="PdfContentBuilder.Complete"/>
    /// to check it. The resources the builder adds go to a resource dictionary of the page itself: the resources the page
    /// shares with other pages (an indirect dictionary, or a category of the page tree) are copied first, so that the
    /// other pages do not change.
    /// </remarks>
    public PdfContentBuilder CreateContentBuilder() => CreateContentBuilder(PdfContentPlacement.Over);

    /// <summary>
    /// Creates a builder that adds a content stream to the page, painted over or under its content. The content of the
    /// builder starts with the default graphics state, and does not change the graphics state of the content around it.
    /// </summary>
    /// <param name="placement">
    /// <see cref="PdfContentPlacement.Over"/> to paint over the content of the page (as <see cref="CreateContentBuilder()"/>),
    /// <see cref="PdfContentPlacement.Under"/> to paint under it, such as a background or a watermark that the content
    /// covers. The content of each new builder under the page is painted under the content of the builders created before.
    /// </param>
    /// <returns>The builder.</returns>
    /// <remarks>
    /// The isolation relies on the content of each builder being balanced: call <see cref="PdfContentBuilder.Complete"/>
    /// to check it. The resources the builder adds go to a resource dictionary of the page itself, as with
    /// <see cref="CreateContentBuilder()"/>.
    /// </remarks>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="placement"/> is not a value of <see cref="PdfContentPlacement"/>.</exception>
    public PdfContentBuilder CreateContentBuilder(PdfContentPlacement placement)
    {
        if (placement is not (PdfContentPlacement.Over or PdfContentPlacement.Under))
            throw new ArgumentOutOfRangeException(nameof(placement), placement, "The placement is not valid");

        var streams = GetContentReferences();
        if (placement == PdfContentPlacement.Under)
        {
            // q (content of the builder) Q (content of the page): the page starts with the default graphics state
            var underlay = new PdfStream();
            underlay.AppendData("q\n"u8);
            var restore = new PdfStream();
            restore.AppendData("Q\n"u8);
            streams.Insert(0, Document.Objects.Add(restore));
            streams.Insert(0, Document.Objects.Add(underlay));
            Dictionary[PdfNames.Contents] = new PdfArray(streams);
            return new PdfContentBuilder(Document, GetOrCreateResources(), underlay, this);
        }

        // Each builder saves the state at the start of the content and restores it before its own content:
        // q q ... (content before) Q (first builder) Q (second builder)
        var stream = new PdfStream();
        if (streams.Count > 0)
        {
            if (_saveStream is not null && ReferenceEquals(PdfReference.ResolveIfReference(streams[0]), _saveStream))
            {
                _saveStream.AppendData("q\n"u8);
            }
            else
            {
                _saveStream = new PdfStream();
                _saveStream.AppendData("q\n"u8);
                streams.Insert(0, Document.Objects.Add(_saveStream));
            }

            stream.AppendData("Q\n"u8);
        }

        var streamReference = Document.Objects.Add(stream);
        streams.Add(streamReference);
        Dictionary[PdfNames.Contents] = streams.Count == 1 ? streamReference : new PdfArray(streams);
        return new PdfContentBuilder(Document, GetOrCreateResources(), stream, this);
    }

    /// <summary>Gets the references to the content streams of the page.</summary>
    private List<PdfObject> GetContentReferences()
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

        return streams;
    }

    /// <summary>
    /// Gets the images the content of the page paints (ISO 32000-2, 8.9): the image XObjects, painted by the content or
    /// by the forms it paints, and the inline images, in the order they are painted, with the matrix they are painted
    /// with. The images of the annotations, of the patterns and of the soft masks are not included.
    /// </summary>
    /// <returns>The images.</returns>
    /// <exception cref="InvalidPdfContentException">A content stream cannot be decoded.</exception>
    public IReadOnlyList<PdfImageOccurrence> GetImages() => PdfImageExtractor.Extract(this);

    /// <summary>Extracts the text of the page, in the order of the content, with a line break between the lines.</summary>
    /// <returns>The text.</returns>
    /// <exception cref="PdfResourceLimitException">The content runs more operators and glyphs than the limit (forms that paint forms).</exception>
    public string ExtractText() => TextExtractor.Extract(this, PdfTextExtractionOptions.Default);

    /// <summary>Extracts the text of the page, in the order of the content, with a line break between the lines.</summary>
    /// <param name="options">The options, or <see langword="null"/> for <see cref="PdfTextExtractionOptions.Default"/>.</param>
    /// <returns>The text.</returns>
    /// <exception cref="PdfResourceLimitException">The content runs more operators and glyphs than the limit (forms that paint forms).</exception>
    public string ExtractText(PdfTextExtractionOptions? options) => TextExtractor.Extract(this, options ?? PdfTextExtractionOptions.Default);

    /// <summary>Extracts the text of the page with the position of its lines, words and glyphs, in the default user space of the page.</summary>
    /// <param name="options">The options, or <see langword="null"/> for <see cref="PdfTextExtractionOptions.Default"/>.</param>
    /// <returns>The lines of the page; its <see cref="PdfTextLayout.Text"/> is the result of <see cref="ExtractText(PdfTextExtractionOptions?)"/>.</returns>
    /// <exception cref="PdfResourceLimitException">The content runs more operators and glyphs than the limit (forms that paint forms).</exception>
    public PdfTextLayout ExtractTextLayout(PdfTextExtractionOptions? options = null) => TextExtractor.ExtractLayout(this, options ?? PdfTextExtractionOptions.Default);

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

    /// <summary>
    /// Gets a resource dictionary that belongs to the page alone, so that resources can be added to it: a direct
    /// dictionary of the page whose categories are direct dictionaries. A dictionary that can be shared (inherited from
    /// the page tree, an indirect object, or with indirect categories) is copied first (copy-on-write); the resources it
    /// holds stay shared.
    /// </summary>
    internal PdfDictionary GetOrCreateResources()
    {
        if (_ownResources is not null && ReferenceEquals(Dictionary[PdfNames.Resources], _ownResources))
            return _ownResources;

        if (Dictionary[PdfNames.Resources] is PdfDictionary direct && IsOwned(direct))
            return _ownResources = direct;

        var resources = new PdfDictionary();
        if (GetInherited(PdfNames.Resources) is PdfDictionary inherited)
        {
            foreach (var entry in inherited)
            {
                resources[entry.Key] = PdfReference.ResolveIfReference(entry.Value) is PdfDictionary category ? category.CloneShallow() : entry.Value;
            }
        }

        Dictionary[PdfNames.Resources] = resources;
        return _ownResources = resources;

        static bool IsOwned(PdfDictionary resources)
        {
            foreach (var entry in resources)
            {
                if (entry.Value is PdfReference reference && reference.Resolve() is PdfDictionary)
                    return false;
            }

            return true;
        }
    }

    /// <summary>Gets the visible region of the page: the crop box clipped to the media box (14.11.2).</summary>
    internal PdfRectangle VisibleBox
    {
        get
        {
            var crop = CropBox;
            var media = MediaBox;
            var left = Math.Max(crop.Left, media.Left);
            var bottom = Math.Max(crop.Bottom, media.Bottom);
            var right = Math.Min(crop.Right, media.Right);
            var top = Math.Min(crop.Top, media.Top);
            return right > left && top > bottom ? new PdfRectangle(left, bottom, right, top) : media;
        }
    }

    private void SetBox(PdfName key, PdfRectangle value)
    {
        if (!double.IsFinite(value.Left) || !double.IsFinite(value.Bottom) || !double.IsFinite(value.Right) || !double.IsFinite(value.Top))
            throw new ArgumentException("The coordinates of a page box must be finite numbers", nameof(value));

        Dictionary[key] = value.ToArray();
    }

    internal PdfObject? GetInherited(PdfName key) => GetInherited(Dictionary, key);

    /// <summary>Gets the value of an inheritable entry (7.7.3.4) as it is stored in the page or the node it is inherited from: a reference is not resolved.</summary>
    internal PdfObject? GetInheritedValue(PdfName key)
    {
        var node = Dictionary;
        for (var depth = 0; depth < MaxInheritanceDepth && node is not null; depth++)
        {
            if (node.GetResolved(key) is not null and not PdfNull)
                return node[key];

            node = node.Get<PdfDictionary>(PdfNames.Parent);
        }

        return null;
    }

    /// <summary>Gets the value of an inheritable entry (7.7.3.4), resolved: the value of the node or of the first of its ancestors that has the entry.</summary>
    internal static PdfObject? GetInherited(PdfDictionary node, PdfName key)
    {
        PdfDictionary? current = node;
        for (var depth = 0; depth < MaxInheritanceDepth && current is not null; depth++)
        {
            if (current.GetResolved(key) is { } value)
                return value;

            current = current.Get<PdfDictionary>(PdfNames.Parent);
        }

        return null;
    }
}
