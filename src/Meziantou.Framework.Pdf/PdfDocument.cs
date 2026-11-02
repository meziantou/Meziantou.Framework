using Meziantou.Framework.Pdf.Fonts;
using Meziantou.Framework.Pdf.Objects;
using Meziantou.Framework.Pdf.Parsing;
using Meziantou.Framework.Pdf.Writing;

namespace Meziantou.Framework.Pdf;

/// <summary>A PDF document: its pages, metadata and outline over the object model (ISO 32000-2).</summary>
/// <remarks>
/// <para>
/// A document is read with <see cref="Open(byte[], PdfReadOptions?)"/> or created with the constructor, and written with
/// <see cref="Save(Stream, PdfSaveOptions?)"/> or, for a document that was read, appended to with
/// <see cref="SaveIncremental(Stream, PdfSaveOptions?)"/>. The high-level members (<see cref="Pages"/>,
/// <see cref="Information"/>, <see cref="Outlines"/>) read and write the objects of <see cref="Objects"/>: changes made
/// through either are visible through both.
/// </para>
/// <para>A document supports one operation at a time; it is not thread-safe.</para>
/// </remarks>
public sealed partial class PdfDocument
{
    private PdfPageCollection? _pages;
    private PdfDocumentInformation? _information;
    private PdfOutlineCollection? _outlines;
    private PdfStructureTree? _structureTree;
    private PdfEmbeddedFileCollection? _embeddedFiles;
    private PdfPageLabelCollection? _pageLabels;
    private PdfInteractiveForm? _form;

    /// <summary>Initializes a new instance of the <see cref="PdfDocument"/> class: an empty PDF 1.7 document without pages.</summary>
    public PdfDocument()
    {
        Objects = new PdfIndirectObjectCollection();
        var pageTree = new PdfDictionary
        {
            [PdfNames.Type] = PdfNames.Pages,
            [PdfNames.Kids] = new PdfArray(),
            [PdfNames.Count] = PdfInteger.Get(0),
        };
        var catalog = new PdfDictionary
        {
            [PdfNames.Type] = PdfNames.Catalog,
        };
        var catalogReference = Objects.Add(catalog);
        catalog[PdfNames.Pages] = Objects.Add(pageTree);
        Trailer = new PdfDictionary { [PdfNames.Root] = catalogReference };
        Version = PdfVersion.Pdf17;
    }

    private PdfDocument(PdfFileReader reader)
    {
        Reader = reader;
        Objects = reader.Objects;
        Trailer = reader.Trailer;
        var version = reader.HeaderVersion;
        if (Catalog.GetName(PdfNames.Version) is { } catalogVersion && PdfVersion.TryParse(catalogVersion.Value, out var parsed) && parsed > version)
        {
            version = parsed;
        }

        Version = version;
        if (reader.Security is { } security)
        {
            Encryption = new PdfEncryption(security);
        }
    }

    /// <summary>Gets the indirect objects of the document.</summary>
    public PdfIndirectObjectCollection Objects { get; }

    /// <summary>Gets the trailer dictionary (ISO 32000-2, 7.5.5): <c>/Root</c>, <c>/Info</c> and <c>/ID</c>. The writer computes the other entries.</summary>
    public PdfDictionary Trailer { get; }

    /// <summary>Gets the document catalog (ISO 32000-2, 7.7.2).</summary>
    public PdfDictionary Catalog => Trailer.Get<PdfDictionary>(PdfNames.Root) ?? throw new InvalidOperationException("The document has no catalog: the /Root entry of the trailer is missing");

    /// <summary>Gets a value indicating whether the file the document was read from is encrypted (ISO 32000-2, 7.6).</summary>
    public bool IsEncrypted => Reader?.Security is not null;

    /// <summary>
    /// Gets the operations the password the document was opened with allows: every operation for the owner password or
    /// a file that is not encrypted, the permissions of the file for the user password. The library does not enforce them.
    /// </summary>
    public PdfPermissions Permissions => Reader?.Security is { IsOwner: false } security ? security.Permissions : PdfPermissions.All;

    /// <summary>
    /// Gets or sets the encryption the document is saved with, or <see langword="null"/> to save it without encryption.
    /// A document read from an encrypted file has the encryption of the file, which a full save keeps unless it is
    /// replaced or removed; an incremental save cannot change it.
    /// </summary>
    public PdfEncryption? Encryption { get; set; }

    /// <summary>
    /// Gets or sets the PDF/A conformance level the document is saved with. The save checks what it can (no encryption,
    /// embedded fonts, a structure tree for the level A, the actions, the annotation appearances, the device color
    /// spaces) and writes the output intent (sRGB, unless the document has one) and the XMP identification; the content
    /// must follow the other rules of the level.
    /// </summary>
    public PdfAConformance PdfAConformance { get; set; }

    /// <summary>
    /// Gets or sets the PDF/UA conformance level the document is saved with. The save checks the structure tree, the
    /// title, the language and the fonts, and writes the XMP identification and the viewer preferences; every content
    /// must be tagged or an artifact, and every figure must have an alternate description.
    /// </summary>
    public PdfUAConformance PdfUAConformance { get; set; }

    /// <summary>Gets or sets the natural language of the document (<c>/Lang</c> of the catalog, ISO 32000-2, 14.9.2): a BCP 47 tag such as <c>en-US</c>.</summary>
    public string? Language
    {
        get => Catalog.GetText(PdfName.Create("Lang"));
        set => Catalog[PdfName.Create("Lang")] = value is null ? null : PdfString.FromText(value);
    }

    /// <summary>Gets the structure tree of the document (ISO 32000-2, 14.7): the logical structure of a tagged document.</summary>
    public PdfStructureTree StructureTree => _structureTree ??= new PdfStructureTree(this);

    /// <summary>Gets or sets the version of the document: the version of the header, or of the <c>/Version</c> entry of the catalog when it is later.</summary>
    public PdfVersion Version { get; set; }

    /// <summary>Gets a value indicating whether the cross-reference information of the file was wrong and was rebuilt by scanning the file. A repaired document cannot be saved incrementally.</summary>
    public bool IsRepaired => Reader?.IsRepaired ?? false;

    /// <summary>Gets the pages of the document.</summary>
    public PdfPageCollection Pages => _pages ??= new PdfPageCollection(this);

    /// <summary>Gets the page labels of the document (ISO 32000-2, 12.4.2): the labels viewers show instead of the page numbers, such as <c>i</c>, <c>ii</c> for the front matter.</summary>
    public PdfPageLabelCollection PageLabels => _pageLabels ??= new PdfPageLabelCollection(this);

    /// <summary>Gets the interactive form of the document (ISO 32000-2, 12.7): its fields, to read, fill or flatten them.</summary>
    public PdfInteractiveForm Form => _form ??= new PdfInteractiveForm(this);

    /// <summary>Gets the files embedded in the document (ISO 32000-2, 7.11.4): the <c>/EmbeddedFiles</c> name tree of the catalog.</summary>
    public PdfEmbeddedFileCollection EmbeddedFiles => _embeddedFiles ??= new PdfEmbeddedFileCollection(this);

    /// <summary>Gets the document information dictionary (ISO 32000-2, 14.3.3): title, author, dates.</summary>
    public PdfDocumentInformation Information => _information ??= new PdfDocumentInformation(this);

    /// <summary>Creates a form XObject (ISO 32000-2, 8.10): a content painted with <see cref="PdfContentBuilder.DrawForm"/>, or the group of a soft mask.</summary>
    /// <param name="boundingBox">The bounding box of the form, in its own coordinate space: the content is clipped to it.</param>
    /// <param name="matrix">The matrix from the space of the form to the user space it is painted in; <see langword="null"/> for the identity.</param>
    /// <param name="group">The attributes of the transparency group of the form; <see langword="null"/> when it is not a group.</param>
    /// <returns>The form.</returns>
    public PdfForm CreateForm(PdfRectangle boundingBox, PdfMatrix? matrix = null, PdfTransparencyGroup? group = null) => new(this, boundingBox, matrix, group);

    /// <summary>
    /// Creates a form XObject (ISO 32000-2, 8.10) whose content is the content of a page, to stamp it on other pages, as an
    /// overlay or an underlay (<see cref="PdfContentBuilder.DrawForm"/>). The page can be a page of another document: the
    /// objects it uses are copied, once per source document.
    /// </summary>
    /// <param name="page">The page.</param>
    /// <returns>
    /// The form: its bounding box is the visible region of the page (the crop box clipped to the media box), and its
    /// matrix is <see cref="PdfPage.UserSpaceToDisplay"/>, so that it is painted upright, with its lower-left corner at
    /// the origin and the size <see cref="PdfPage.DisplaySize"/>, whatever the rotation of the page. The annotations of
    /// the page are not part of it.
    /// </returns>
    /// <exception cref="InvalidPdfContentException">A content stream of the page cannot be decoded.</exception>
    public PdfForm CreateFormFromPage(PdfPage page)
    {
        ArgumentNullException.ThrowIfNull(page);
        var importer = page.Document == this ? null : PdfObjectImporter.Get(page.Document, this);
        var resources = new PdfDictionary();
        if (page.GetInherited(PdfNames.Resources) is PdfDictionary pageResources)
        {
            // The categories are copied, so that the content builders of the form and of the page do not change each other
            foreach (var (key, value) in pageResources)
            {
                var copy = importer is null ? value : importer.Copy(value);
                resources[key] = PdfReference.ResolveIfReference(copy) is PdfDictionary category ? category.CloneShallow() : copy;
            }
        }

        var box = page.VisibleBox;
        var stream = new PdfStream();
        stream.SetData(page.GetContentData());
        var matrix = page.UserSpaceToDisplay;
        var dictionary = stream.Dictionary;
        dictionary[PdfNames.Type] = PdfNames.XObject;
        dictionary[PdfNames.Subtype] = PdfNames.Form;
        dictionary[PdfNames.BBox] = box.ToArray();
        if (matrix != PdfMatrix.Identity)
        {
            dictionary[PdfNames.Matrix] = PdfArray.FromNumbers(matrix.A, matrix.B, matrix.C, matrix.D, matrix.E, matrix.F);
        }

        // The transparency group of the page is the group of the form (8.10.3, 11.6.6)
        if (page.Dictionary[PdfNames.Group] is { } group)
        {
            dictionary[PdfNames.Group] = importer is null ? group : importer.Copy(group);
        }

        dictionary[PdfNames.Resources] = resources;
        return new PdfForm(this, stream, box, resources);
    }

    /// <summary>Creates a tiling pattern (ISO 32000-2, 8.7.3).</summary>
    /// <param name="cell">The bounding box of the cell, in the pattern space: the content of the cell is clipped to it.</param>
    /// <param name="xStep">The horizontal spacing of the cells.</param>
    /// <param name="yStep">The vertical spacing of the cells.</param>
    /// <param name="isColored"><see langword="true"/> when the cell sets its colors; <see langword="false"/> for a stencil painted with the color given when the pattern is set.</param>
    /// <param name="matrix">The matrix from the pattern space to the default coordinate space of the page; <see langword="null"/> for the identity.</param>
    /// <returns>The pattern.</returns>
    public PdfTilingPattern CreateTilingPattern(PdfRectangle cell, double xStep, double yStep, bool isColored = true, PdfMatrix? matrix = null) => new(this, cell, xStep, yStep, isColored, matrix);

    /// <summary>Gets the top-level items of the document outline, the bookmarks (ISO 32000-2, 12.3.3).</summary>
    public PdfOutlineCollection Outlines => _outlines ??= PdfOutlineCollection.Load(this);

    internal PdfFileReader? Reader { get; }

    /// <summary>Gets the outline when it was read, or <see langword="null"/>.</summary>
    internal PdfOutlineCollection? LoadedOutlines => _outlines;

    /// <summary>Gets the importers of the pages of other documents into this one, by source document (<see cref="PdfPageCollection.Insert(int, PdfPage)"/>).</summary>
    internal System.Runtime.CompilerServices.ConditionalWeakTable<PdfDocument, PdfObjectImporter> Importers { get; } = new();

    internal Dictionary<object, PdfReference> SharedResources { get; } = [];

    /// <summary>Gets the embedded fonts the content of the document uses, whose objects are written when it is saved.</summary>
    internal Dictionary<EmbeddedPdfFont, EmbeddedFontUsage> EmbeddedFonts { get; } = [];

    /// <summary>Reads a document.</summary>
    /// <param name="data">The content of the file.</param>
    /// <param name="options">The options, or <see langword="null"/> for <see cref="PdfReadOptions.Default"/>.</param>
    /// <returns>The document. It keeps <paramref name="data"/>: the array must not change while the document is used.</returns>
    /// <exception cref="InvalidPdfContentException">The data is not a PDF document, or it cannot be recovered.</exception>
    /// <exception cref="UnsupportedPdfFeatureException">The document uses a feature the library does not support.</exception>
    /// <exception cref="PdfResourceLimitException">The document exceeds a limit of the options.</exception>
    /// <exception cref="PdfPasswordException">The document is encrypted, and <see cref="PdfReadOptions.Password"/> is neither its user nor its owner password.</exception>
    public static PdfDocument Open(byte[] data, PdfReadOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(data);
        return Open(data.AsMemory(), options);
    }

    /// <summary>Reads a document.</summary>
    /// <param name="data">The content of the file. It must not change while the document is used.</param>
    /// <param name="options">The options, or <see langword="null"/> for <see cref="PdfReadOptions.Default"/>.</param>
    /// <returns>The document.</returns>
    /// <exception cref="InvalidPdfContentException">The data is not a PDF document, or it cannot be recovered.</exception>
    /// <exception cref="UnsupportedPdfFeatureException">The document uses a feature the library does not support.</exception>
    /// <exception cref="PdfResourceLimitException">The document exceeds a limit of the options.</exception>
    /// <exception cref="PdfPasswordException">The document is encrypted, and <see cref="PdfReadOptions.Password"/> is neither its user nor its owner password.</exception>
    public static PdfDocument Open(ReadOnlyMemory<byte> data, PdfReadOptions? options = null)
    {
        options ??= PdfReadOptions.Default;
        var reader = PdfFileReader.Open(data, options);
        return new PdfDocument(reader);
    }

    /// <summary>Reads a document from a stream. The stream is read to its end and is not closed.</summary>
    /// <param name="stream">The stream.</param>
    /// <param name="options">The options, or <see langword="null"/> for <see cref="PdfReadOptions.Default"/>.</param>
    /// <returns>The document.</returns>
    /// <exception cref="InvalidPdfContentException">The data is not a PDF document, or it cannot be recovered.</exception>
    /// <exception cref="UnsupportedPdfFeatureException">The document uses a feature the library does not support.</exception>
    /// <exception cref="PdfResourceLimitException">The document exceeds a limit of the options.</exception>
    /// <exception cref="PdfPasswordException">The document is encrypted, and <see cref="PdfReadOptions.Password"/> is neither its user nor its owner password.</exception>
    public static PdfDocument Open(Stream stream, PdfReadOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(stream);
        return Open(PdfStreamReader.ReadToEnd(stream), options);
    }

    /// <summary>
    /// Reads a document from a seekable stream without copying it to memory: the objects are read from the stream when
    /// they are first used, so that the memory does not grow with the size of the file, and a file can be larger than 2 GB.
    /// </summary>
    /// <param name="stream">
    /// The stream, positioned at the start of the file. It belongs to the caller: the document does not close it. It must
    /// stay open and unchanged while the document is used, and it must not be used by anything else meanwhile: the document
    /// moves its position.
    /// </param>
    /// <param name="options">The options, or <see langword="null"/> for <see cref="PdfReadOptions.Default"/>.</param>
    /// <returns>The document.</returns>
    /// <exception cref="ArgumentException">The stream cannot seek or cannot be read.</exception>
    /// <exception cref="InvalidPdfContentException">The data is not a PDF document, or it cannot be recovered.</exception>
    /// <exception cref="UnsupportedPdfFeatureException">The document uses a feature the library does not support.</exception>
    /// <exception cref="PdfResourceLimitException">The document exceeds a limit of the options.</exception>
    /// <exception cref="PdfPasswordException">The document is encrypted, and <see cref="PdfReadOptions.Password"/> is neither its user nor its owner password.</exception>
    /// <remarks>The errors of the stream (<see cref="IOException"/>, <see cref="ObjectDisposedException"/>) are not caught: they can occur whenever an object is first used.</remarks>
    /// <example>
    /// <code xmlns:ref="urn:meziantou:file-referencer" ref:source="../../tests/Meziantou.Framework.Pdf.Tests/PdfDocumentTests.cs" ref:region="Pdf_OpenSeekable" ref:dedent="true">
    /// // The objects are read from the stream when they are first used: it stays open while the document is used
    /// using var stream = File.OpenRead(path);
    /// var document = PdfDocument.OpenSeekable(stream);
    /// var text = document.Pages[0].ExtractText();
    /// </code>
    /// </example>
    public static PdfDocument OpenSeekable(Stream stream, PdfReadOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(stream);
        if (!stream.CanSeek || !stream.CanRead)
            throw new ArgumentException("The stream must be readable and seekable", nameof(stream));

        options ??= PdfReadOptions.Default;
        var reader = PdfFileReader.Open(new StreamPdfInput(stream), options);
        return new PdfDocument(reader);
    }

    /// <summary>Reads a document from a file.</summary>
    /// <param name="path">The path of the file.</param>
    /// <param name="options">The options, or <see langword="null"/> for <see cref="PdfReadOptions.Default"/>.</param>
    /// <returns>The document.</returns>
    /// <exception cref="InvalidPdfContentException">The data is not a PDF document, or it cannot be recovered.</exception>
    /// <exception cref="UnsupportedPdfFeatureException">The document uses a feature the library does not support.</exception>
    /// <exception cref="PdfResourceLimitException">The document exceeds a limit of the options.</exception>
    /// <exception cref="PdfPasswordException">The document is encrypted, and <see cref="PdfReadOptions.Password"/> is neither its user nor its owner password.</exception>
    public static PdfDocument Open(string path, PdfReadOptions? options = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        return Open(File.ReadAllBytes(path), options);
    }

    /// <summary>Reads a document from a stream. The stream is read to its end and is not closed.</summary>
    /// <param name="stream">The stream.</param>
    /// <param name="options">The options, or <see langword="null"/> for <see cref="PdfReadOptions.Default"/>.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>The document.</returns>
    /// <exception cref="InvalidPdfContentException">The data is not a PDF document, or it cannot be recovered.</exception>
    /// <exception cref="UnsupportedPdfFeatureException">The document uses a feature the library does not support.</exception>
    /// <exception cref="PdfResourceLimitException">The document exceeds a limit of the options.</exception>
    /// <exception cref="PdfPasswordException">The document is encrypted, and <see cref="PdfReadOptions.Password"/> is neither its user nor its owner password.</exception>
    public static async Task<PdfDocument> OpenAsync(Stream stream, PdfReadOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(stream);
        return Open(await PdfStreamReader.ReadToEndAsync(stream, cancellationToken).ConfigureAwait(false), options);
    }

    /// <summary>Reads a document from a file.</summary>
    /// <param name="path">The path of the file.</param>
    /// <param name="options">The options, or <see langword="null"/> for <see cref="PdfReadOptions.Default"/>.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>The document.</returns>
    /// <exception cref="InvalidPdfContentException">The data is not a PDF document, or it cannot be recovered.</exception>
    /// <exception cref="UnsupportedPdfFeatureException">The document uses a feature the library does not support.</exception>
    /// <exception cref="PdfResourceLimitException">The document exceeds a limit of the options.</exception>
    /// <exception cref="PdfPasswordException">The document is encrypted, and <see cref="PdfReadOptions.Password"/> is neither its user nor its owner password.</exception>
    public static async Task<PdfDocument> OpenAsync(string path, PdfReadOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        return Open(await File.ReadAllBytesAsync(path, cancellationToken).ConfigureAwait(false), options);
    }

    /// <summary>Writes the whole document: the objects reachable from the catalog, renumbered in the order they are reached.</summary>
    /// <param name="stream">The stream to write to. It is not closed.</param>
    /// <param name="options">The options, or <see langword="null"/> for <see cref="PdfSaveOptions.Default"/>.</param>
    public void Save(Stream stream, PdfSaveOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(stream);
        stream.Write(SaveCore(options, incremental: false).WrittenSpan);
    }

    /// <summary>Writes the whole document to a file.</summary>
    /// <param name="path">The path of the file. An existing file is replaced.</param>
    /// <param name="options">The options, or <see langword="null"/> for <see cref="PdfSaveOptions.Default"/>.</param>
    public void Save(string path, PdfSaveOptions? options = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        var output = SaveCore(options, incremental: false);
        using var stream = File.Create(path);
        stream.Write(output.WrittenSpan);
    }

    /// <summary>Writes the whole document to a stream.</summary>
    /// <param name="stream">The stream to write to. It is not closed.</param>
    /// <param name="options">The options, or <see langword="null"/> for <see cref="PdfSaveOptions.Default"/>.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>A task that completes when the document is written.</returns>
    public async Task SaveAsync(Stream stream, PdfSaveOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(stream);
        var output = SaveCore(options, incremental: false);
        await stream.WriteAsync(output.WrittenMemory, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Writes the whole document to an array.</summary>
    /// <param name="options">The options, or <see langword="null"/> for <see cref="PdfSaveOptions.Default"/>.</param>
    /// <returns>The content of the file.</returns>
    public byte[] ToByteArray(PdfSaveOptions? options = null) => SaveCore(options, incremental: false).ToArray();

    /// <summary>
    /// Writes the original file followed by an incremental update (ISO 32000-2, 7.5.6): the objects that were added or
    /// changed, a cross-reference section of the kind of the original file, and a trailer that points to the original one.
    /// The bytes of the original file are kept, so its signatures stay valid.
    /// </summary>
    /// <param name="stream">The stream to write to. It is not closed.</param>
    /// <param name="options">The options, or <see langword="null"/> for <see cref="PdfSaveOptions.Default"/>. Object streams are not used.</param>
    /// <exception cref="InvalidOperationException">The document was not read from a file, or its cross-reference information was rebuilt (<see cref="IsRepaired"/>).</exception>
    public void SaveIncremental(Stream stream, PdfSaveOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(stream);
        stream.Write(SaveCore(options, incremental: true).WrittenSpan);
    }

    /// <summary>Writes the original file followed by an incremental update (ISO 32000-2, 7.5.6).</summary>
    /// <param name="stream">The stream to write to. It is not closed.</param>
    /// <param name="options">The options, or <see langword="null"/> for <see cref="PdfSaveOptions.Default"/>.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>A task that completes when the document is written.</returns>
    /// <exception cref="InvalidOperationException">The document was not read from a file, or its cross-reference information was rebuilt (<see cref="IsRepaired"/>).</exception>
    public async Task SaveIncrementalAsync(Stream stream, PdfSaveOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(stream);
        var output = SaveCore(options, incremental: true);
        await stream.WriteAsync(output.WrittenMemory, cancellationToken).ConfigureAwait(false);
    }

    private PdfByteWriter SaveCore(PdfSaveOptions? options, bool incremental)
    {
        options ??= PdfSaveOptions.Default;
        if (incremental && Reader is null)
            throw new InvalidOperationException("Only a document read from a file can be saved incrementally");

        PrepareForSave();
        _structureTree?.Write();
        Conformance.ConformanceWriter.Apply(this, options);
        var output = new PdfByteWriter(64 * 1024);
        if (incremental)
        {
            PdfFileWriter.WriteIncremental(this, options, output);
        }
        else
        {
            PdfFileWriter.WriteFull(this, options, output);
        }

        return output;
    }

    /// <summary>Writes what the high-level members keep apart from the objects: the outline and the subsets of the embedded fonts.</summary>
    private void PrepareForSave()
    {
        _outlines?.WriteTo(this);
        foreach (var font in EmbeddedFonts.Values)
        {
            font.Write(this);
        }

        if (Reader is not null && Version > Reader.HeaderVersion)
        {
            var current = Catalog.GetName(PdfNames.Version);
            if (current is null || !PdfVersion.TryParse(current.Value, out var catalogVersion) || catalogVersion < Version)
            {
                Catalog[PdfNames.Version] = PdfName.Create(Version.ToString());
            }
        }
    }
}
