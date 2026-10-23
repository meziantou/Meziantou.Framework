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
    }

    /// <summary>Gets the indirect objects of the document.</summary>
    public PdfIndirectObjectCollection Objects { get; }

    /// <summary>Gets the trailer dictionary (ISO 32000-2, 7.5.5): <c>/Root</c>, <c>/Info</c> and <c>/ID</c>. The writer computes the other entries.</summary>
    public PdfDictionary Trailer { get; }

    /// <summary>Gets the document catalog (ISO 32000-2, 7.7.2).</summary>
    public PdfDictionary Catalog => Trailer.Get<PdfDictionary>(PdfNames.Root) ?? throw new InvalidOperationException("The document has no catalog: the /Root entry of the trailer is missing");

    /// <summary>Gets or sets the version of the document: the version of the header, or of the <c>/Version</c> entry of the catalog when it is later.</summary>
    public PdfVersion Version { get; set; }

    /// <summary>Gets a value indicating whether the cross-reference information of the file was wrong and was rebuilt by scanning the file. A repaired document cannot be saved incrementally.</summary>
    public bool IsRepaired => Reader?.IsRepaired ?? false;

    /// <summary>Gets the pages of the document.</summary>
    public PdfPageCollection Pages => _pages ??= new PdfPageCollection(this);

    /// <summary>Gets the document information dictionary (ISO 32000-2, 14.3.3): title, author, dates.</summary>
    public PdfDocumentInformation Information => _information ??= new PdfDocumentInformation(this);

    /// <summary>Gets the top-level items of the document outline, the bookmarks (ISO 32000-2, 12.3.3).</summary>
    public PdfOutlineCollection Outlines => _outlines ??= PdfOutlineCollection.Load(this);

    internal PdfFileReader? Reader { get; }

    internal Dictionary<object, PdfReference> SharedResources { get; } = [];

    /// <summary>Reads a document.</summary>
    /// <param name="data">The content of the file.</param>
    /// <param name="options">The options, or <see langword="null"/> for <see cref="PdfReadOptions.Default"/>.</param>
    /// <returns>The document. It keeps <paramref name="data"/>: the array must not change while the document is used.</returns>
    /// <exception cref="InvalidPdfContentException">The data is not a PDF document, or it cannot be recovered.</exception>
    /// <exception cref="UnsupportedPdfFeatureException">The document uses a feature the library does not support.</exception>
    /// <exception cref="PdfResourceLimitException">The document exceeds a limit of the options.</exception>
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
    public static PdfDocument Open(ReadOnlyMemory<byte> data, PdfReadOptions? options = null)
    {
        options ??= PdfReadOptions.Default;
        var reader = PdfFileReader.Open(data, options);
        if (reader.Trailer.ContainsKey(PdfNames.Encrypt))
            throw new UnsupportedPdfFeatureException("The document is encrypted: encryption is not supported yet");

        return new PdfDocument(reader);
    }

    /// <summary>Reads a document from a stream. The stream is read to its end and is not closed.</summary>
    /// <param name="stream">The stream.</param>
    /// <param name="options">The options, or <see langword="null"/> for <see cref="PdfReadOptions.Default"/>.</param>
    /// <returns>The document.</returns>
    /// <exception cref="InvalidPdfContentException">The data is not a PDF document, or it cannot be recovered.</exception>
    /// <exception cref="UnsupportedPdfFeatureException">The document uses a feature the library does not support.</exception>
    /// <exception cref="PdfResourceLimitException">The document exceeds a limit of the options.</exception>
    public static PdfDocument Open(Stream stream, PdfReadOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(stream);
        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        return Open(buffer.ToArray(), options);
    }

    /// <summary>Reads a document from a file.</summary>
    /// <param name="path">The path of the file.</param>
    /// <param name="options">The options, or <see langword="null"/> for <see cref="PdfReadOptions.Default"/>.</param>
    /// <returns>The document.</returns>
    /// <exception cref="InvalidPdfContentException">The data is not a PDF document, or it cannot be recovered.</exception>
    /// <exception cref="UnsupportedPdfFeatureException">The document uses a feature the library does not support.</exception>
    /// <exception cref="PdfResourceLimitException">The document exceeds a limit of the options.</exception>
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
    public static async Task<PdfDocument> OpenAsync(Stream stream, PdfReadOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(stream);
        using var buffer = new MemoryStream();
        await stream.CopyToAsync(buffer, cancellationToken).ConfigureAwait(false);
        return Open(buffer.ToArray(), options);
    }

    /// <summary>Reads a document from a file.</summary>
    /// <param name="path">The path of the file.</param>
    /// <param name="options">The options, or <see langword="null"/> for <see cref="PdfReadOptions.Default"/>.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>The document.</returns>
    /// <exception cref="InvalidPdfContentException">The data is not a PDF document, or it cannot be recovered.</exception>
    /// <exception cref="UnsupportedPdfFeatureException">The document uses a feature the library does not support.</exception>
    /// <exception cref="PdfResourceLimitException">The document exceeds a limit of the options.</exception>
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

    /// <summary>Writes what the high-level members keep apart from the objects: the outline.</summary>
    private void PrepareForSave()
    {
        _outlines?.WriteTo(this);
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
