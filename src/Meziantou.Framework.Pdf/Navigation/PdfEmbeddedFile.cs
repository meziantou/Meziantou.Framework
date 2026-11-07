using System.Text;
using Meziantou.Framework.Pdf.Objects;
using Meziantou.Framework.Pdf.Text;

namespace Meziantou.Framework.Pdf;

/// <summary>
/// A file embedded in a document (ISO 32000-2, 7.11.4): a file specification dictionary whose embedded file stream
/// holds the bytes of the file. The files of the document are in <see cref="PdfDocument.EmbeddedFiles"/>, the files of
/// a page in its file attachment annotations (<see cref="PdfFileAttachmentAnnotation"/>).
/// </summary>
public sealed class PdfEmbeddedFile
{
    internal static readonly PdfName FilespecName = PdfName.Create("Filespec");
    internal static readonly PdfName EmbeddedFileName = PdfName.Create("EmbeddedFile");
    internal static readonly PdfName EFName = PdfName.Create("EF");
    internal static readonly PdfName UFName = PdfName.Create("UF");
    internal static readonly PdfName DescName = PdfName.Create("Desc");
    internal static readonly PdfName AFName = PdfName.Create("AF");
    internal static readonly PdfName AFRelationshipName = PdfName.Create("AFRelationship");
    internal static readonly PdfName ParamsName = PdfName.Create("Params");

    private const string DefaultMimeType = "application/octet-stream";

    internal PdfEmbeddedFile(PdfDocument document, PdfDictionary dictionary, PdfReference? reference, string? key)
    {
        Document = document;
        Dictionary = dictionary;
        Reference = reference;
        Key = key;
    }

    /// <summary>Gets the file specification dictionary.</summary>
    public PdfDictionary Dictionary { get; }

    /// <summary>Gets the reference to the file specification, or <see langword="null"/> for a direct object.</summary>
    public PdfReference? Reference { get; }

    /// <summary>Gets the name of the file: <c>/UF</c>, or <c>/F</c>, or the key of the name tree of the document.</summary>
    public string FileName => Dictionary.GetText(UFName) ?? Dictionary.GetText(PdfNames.F) ?? Key ?? "";

    /// <summary>Gets the description of the file (<c>/Desc</c>), or <see langword="null"/>.</summary>
    public string? Description => Dictionary.GetText(DescName);

    /// <summary>Gets how the file relates to the content of the document (<c>/AFRelationship</c>); <see cref="PdfAssociatedFileRelationship.Unspecified"/> when it is missing or unknown.</summary>
    public PdfAssociatedFileRelationship Relationship => Dictionary.GetName(AFRelationshipName)?.Value switch
    {
        "Source" => PdfAssociatedFileRelationship.Source,
        "Data" => PdfAssociatedFileRelationship.Data,
        "Alternative" => PdfAssociatedFileRelationship.Alternative,
        "Supplement" => PdfAssociatedFileRelationship.Supplement,
        "EncryptedPayload" => PdfAssociatedFileRelationship.EncryptedPayload,
        "FormData" => PdfAssociatedFileRelationship.FormData,
        "Schema" => PdfAssociatedFileRelationship.Schema,
        _ => PdfAssociatedFileRelationship.Unspecified,
    };

    /// <summary>Gets the MIME type of the file (<c>/Subtype</c> of the embedded file stream), or <see langword="null"/>.</summary>
    public string? MimeType => Stream?.Dictionary.GetName(PdfNames.Subtype)?.Value;

    /// <summary>Gets the date the file was created (<c>/Params /CreationDate</c>), or <see langword="null"/>.</summary>
    public DateTimeOffset? CreationDate => GetDate(PdfNames.CreationDate);

    /// <summary>Gets the date the file was last modified (<c>/Params /ModDate</c>), or <see langword="null"/>.</summary>
    public DateTimeOffset? ModificationDate => GetDate(PdfNames.ModDate);

    /// <summary>Gets the size of the file in bytes (<c>/Params /Size</c>), or <see langword="null"/> when the file does not give it.</summary>
    public long? Size => Parameters?.Get<PdfInteger>(PdfNames.Size)?.Value;

    internal PdfDocument Document { get; }

    internal string? Key { get; }

    /// <summary>Gets the embedded file stream: <c>/UF</c>, or <c>/F</c>, of the <c>/EF</c> dictionary.</summary>
    internal PdfStream? Stream => Dictionary.Get<PdfDictionary>(EFName) is { } files ? files.Get<PdfStream>(UFName) ?? files.Get<PdfStream>(PdfNames.F) : null;

    private PdfDictionary? Parameters => Stream?.Dictionary.Get<PdfDictionary>(ParamsName);

    /// <summary>Gets the content of the file.</summary>
    /// <returns>The bytes of the file.</returns>
    /// <exception cref="InvalidPdfContentException">The file specification has no embedded file stream, or the stream cannot be decoded.</exception>
    /// <exception cref="PdfResourceLimitException">The decoded file exceeds <see cref="PdfReadOptions.MaxDecodedStreamLength"/>.</exception>
    public byte[] GetData()
    {
        var stream = Stream ?? throw new InvalidPdfContentException($"The file specification of '{FileName}' has no embedded file stream");
        return stream.GetDecodedData();
    }

    /// <summary>Creates the embedded file stream and the file specification of a file (7.11.3, 7.11.4).</summary>
    internal static (PdfDictionary Dictionary, PdfReference Reference) Create(PdfDocument document, string fileName, ReadOnlySpan<byte> data, PdfEmbeddedFileOptions? options)
    {
        ArgumentException.ThrowIfNullOrEmpty(fileName);
        options ??= new PdfEmbeddedFileOptions();
        var mimeType = options.MimeType ?? DefaultMimeType;
        if (mimeType.Length == 0 || mimeType.Any(c => c is <= ' ' or > '~'))
            throw new ArgumentException("The MIME type must be non-empty printable ASCII, such as 'text/csv'", nameof(options));

        var relationship = options.Relationship switch
        {
            PdfAssociatedFileRelationship.Unspecified => "Unspecified",
            PdfAssociatedFileRelationship.Source => "Source",
            PdfAssociatedFileRelationship.Data => "Data",
            PdfAssociatedFileRelationship.Alternative => "Alternative",
            PdfAssociatedFileRelationship.Supplement => "Supplement",
            PdfAssociatedFileRelationship.EncryptedPayload => "EncryptedPayload",
            PdfAssociatedFileRelationship.FormData => "FormData",
            PdfAssociatedFileRelationship.Schema => "Schema",
            _ => throw new ArgumentOutOfRangeException(nameof(options), options.Relationship, "The relationship is not valid"),
        };

        // The parameters of the embedded file (7.11.4.2, Table 45): the size of the uncompressed file and its dates
        var parameters = new PdfDictionary { [PdfNames.Size] = PdfInteger.Get(data.Length) };
        if (options.CreationDate is { } creationDate)
        {
            parameters[PdfNames.CreationDate] = PdfString.FromText(PdfDate.Format(creationDate));
        }

        if (options.ModificationDate is { } modificationDate)
        {
            parameters[PdfNames.ModDate] = PdfString.FromText(PdfDate.Format(modificationDate));
        }

        var stream = new PdfStream(new PdfDictionary
        {
            [PdfNames.Type] = EmbeddedFileName,
            [PdfNames.Subtype] = PdfName.Create(mimeType),
            [ParamsName] = parameters,
        });
        stream.SetData(data);
        var streamReference = document.Objects.Add(stream);

        // The file specification (7.11.3, Table 43): /F for the readers of PDF 1.6 and earlier, /UF for the others
        var dictionary = new PdfDictionary
        {
            [PdfNames.Type] = FilespecName,
            [PdfNames.F] = PdfString.FromBytes(ToFileSpecificationString(fileName)),
            [UFName] = PdfString.FromText(fileName),
        };
        if (options.Description is { } description)
        {
            dictionary[DescName] = PdfString.FromText(description);
        }

        dictionary[EFName] = new PdfDictionary
        {
            [PdfNames.F] = streamReference,
            [UFName] = streamReference,
        };
        dictionary[AFRelationshipName] = PdfName.Create(relationship);
        return (dictionary, document.Objects.Add(dictionary));
    }

    /// <summary>Gets the bytes of a file specification string (7.11.2): the name in 7-bit ASCII, the other characters replaced by an underscore.</summary>
    private static byte[] ToFileSpecificationString(string fileName)
    {
        var builder = new StringBuilder(fileName.Length);
        foreach (var c in fileName)
        {
            builder.Append(c is >= ' ' and <= '~' ? c : '_');
        }

        return Encoding.ASCII.GetBytes(builder.ToString());
    }

    private DateTimeOffset? GetDate(PdfName key) => PdfDate.TryParse(Parameters?.GetText(key), out var date) ? date : null;
}
