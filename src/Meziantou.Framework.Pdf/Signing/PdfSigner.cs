using System.Globalization;
using System.Text;
using Meziantou.Framework.Pdf.Objects;
using Meziantou.Framework.Pdf.Text;

namespace Meziantou.Framework.Pdf.Signing;

/// <summary>
/// Signs documents (ISO 32000-2, 12.8): an invisible signature field whose signature is a CAdES detached signature of the
/// file (PAdES baseline B-B, ETSI EN 319 142-1), stamped by a time stamp authority for B-T.
/// </summary>
/// <remarks>
/// The signature is appended to the file as an incremental update, so that the signatures the document already has stay
/// valid. A document that was not read from a file is saved in full first. The changes made to a document read from a
/// file are saved in the update of the signature.
/// </remarks>
public static class PdfSigner
{
    // The placeholders of the byte ranges: wide enough for any offset of a file, replaced in place
    private const string ByteRangePlaceholder = "[0 9999999999 9999999999 9999999999]";

    private static readonly PdfName FieldsName = PdfName.Create("Fields");

    /// <summary>Signs a document without time stamp (PAdES B-B).</summary>
    /// <param name="document">The document.</param>
    /// <param name="options">The options.</param>
    /// <returns>The signed file.</returns>
    /// <exception cref="ArgumentException">The certificate has no private key, or the options ask for a time stamp: use <see cref="SignAsync"/>.</exception>
    public static byte[] Sign(PdfDocument document, PdfSignatureOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (options.TimestampAuthority is not null)
            throw new ArgumentException("A time stamp is requested from a server: use SignAsync", nameof(options));

        return SignAsync(document, options, CancellationToken.None).GetAwaiter().GetResult();
    }

    /// <summary>Signs a document, with the time stamp of <see cref="PdfSignatureOptions.TimestampAuthority"/> when it is set (PAdES B-T).</summary>
    /// <param name="document">The document.</param>
    /// <param name="options">The options.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>The signed file.</returns>
    /// <exception cref="ArgumentException">The certificate has no private key.</exception>
    /// <exception cref="HttpRequestException">The time stamp authority could not be reached.</exception>
    /// <exception cref="System.Security.Cryptography.CryptographicException">The signature or the time stamp could not be computed.</exception>
    public static async Task<byte[]> SignAsync(PdfDocument document, PdfSignatureOptions options, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(options);
        if (!options.Certificate.HasPrivateKey)
            throw new ArgumentException("The certificate has no private key", nameof(options));

        // The file the signature is appended to, and the document read from it with the same password
        byte[] basis;
        if (document.Reader is { IsRepaired: false })
        {
            using var stream = new MemoryStream();
            document.SaveIncremental(stream);
            basis = stream.ToArray();
        }
        else
        {
            basis = document.ToByteArray();
        }

        var password = document.Reader?.Options.Password ?? document.Encryption?.OwnerPassword ?? document.Encryption?.UserPassword;
        var signed = PdfDocument.Open(basis, new PdfReadOptions { Password = password });

        // The size of the signature: a signature of the same certificates, and room for a time stamp
        var reserved = options.ReservedSize;
        if (reserved == 0)
        {
            var sampleOptions = new PdfSignatureOptions(options.Certificate) { HashAlgorithm = options.HashAlgorithm };
            sampleOptions.Certificates.AddRange(options.Certificates);
            var sample = await CmsSignature.CreateAsync(new byte[32], sampleOptions, cancellationToken).ConfigureAwait(false);
            reserved = sample.Length + 2048 + (options.TimestampAuthority is null ? 0 : 16384);
        }

        AddSignatureField(signed, options, reserved);
        byte[] file;
        using (var stream = new MemoryStream())
        {
            signed.SaveIncremental(stream);
            file = stream.ToArray();
        }

        // The byte ranges: the whole file but the hexadecimal string of /Contents (12.8.1)
        var placeholder = Encoding.ASCII.GetBytes("<" + new string('0', reserved * 2) + ">");
        var contentsStart = file.AsSpan(basis.Length).IndexOf(placeholder);
        var byteRangeStart = file.AsSpan(basis.Length).IndexOf(Encoding.ASCII.GetBytes(ByteRangePlaceholder));
        if (contentsStart < 0 || byteRangeStart < 0)
            throw new InvalidOperationException("The placeholders of the signature were not found in the file");

        contentsStart += basis.Length;
        byteRangeStart += basis.Length;
        var contentsEnd = contentsStart + placeholder.Length;
        var byteRange = string.Create(CultureInfo.InvariantCulture, $"[0 {contentsStart} {contentsEnd} {file.Length - contentsEnd}");
        byteRange = byteRange.PadRight(ByteRangePlaceholder.Length - 1) + "]";
        Encoding.ASCII.GetBytes(byteRange).CopyTo(file, byteRangeStart);

        var content = new byte[contentsStart + (file.Length - contentsEnd)];
        file.AsSpan(0, contentsStart).CopyTo(content);
        file.AsSpan(contentsEnd).CopyTo(content.AsSpan(contentsStart));
        var signature = await CmsSignature.CreateAsync(content, options, cancellationToken).ConfigureAwait(false);
        if (signature.Length > reserved)
            throw new InvalidOperationException($"The signature takes {signature.Length} bytes, more than the {reserved} bytes reserved: set PdfSignatureOptions.ReservedSize");

        var hex = Convert.ToHexString(signature);
        Encoding.ASCII.GetBytes(hex).CopyTo(file, contentsStart + 1);
        return file;
    }

    /// <summary>Adds the signature dictionary, its invisible widget on the first page and the field of the interactive form (12.7.5.5).</summary>
    private static void AddSignatureField(PdfDocument document, PdfSignatureOptions options, int reserved)
    {
        if (document.Pages.Count == 0)
            throw new InvalidOperationException("A document without pages cannot be signed");

        var signature = new PdfDictionary
        {
            [PdfNames.Type] = PdfName.Create("Sig"),
            [PdfNames.Filter] = PdfName.Create("Adobe.PPKLite"),
            [PdfName.Create("SubFilter")] = PdfName.Create("ETSI.CAdES.detached"),
            [PdfName.Create("ByteRange")] = new PdfArray(PdfInteger.Get(0), PdfInteger.Get(9999999999), PdfInteger.Get(9999999999), PdfInteger.Get(9999999999)),
            [PdfNames.Contents] = PdfString.FromBytes(new byte[reserved], hexadecimal: true),
            [PdfName.Create("M")] = PdfString.FromText(PdfDate.Format(options.SigningTime ?? DateTimeOffset.UtcNow)),
        };
        SetText(signature, "Name", options.SignerName);
        SetText(signature, "Reason", options.Reason);
        SetText(signature, "Location", options.Location);
        SetText(signature, "ContactInfo", options.ContactInfo);

        var catalog = document.Catalog;
        if (catalog.GetResolved(PdfNames.AcroForm) is not PdfDictionary form)
        {
            form = [];
            catalog[PdfNames.AcroForm] = document.Objects.Add(form);
        }

        if (form.GetResolved(FieldsName) is not PdfArray fields)
        {
            fields = [];
            form[FieldsName] = fields;
        }

        // SignaturesExist and AppendOnly (12.7.3)
        form[PdfName.Create("SigFlags")] = PdfInteger.Get(3);

        var page = document.Pages[0];
        var widget = new PdfDictionary
        {
            [PdfNames.Type] = PdfNames.Annot,
            [PdfNames.Subtype] = PdfName.Create("Widget"),
            [PdfName.Create("FT")] = PdfName.Create("Sig"),
            [PdfName.Create("T")] = PdfString.FromText(options.FieldName ?? GetFieldName(fields)),
            [PdfName.Create("V")] = document.Objects.Add(signature),
            [PdfNames.Rect] = PdfArray.FromNumbers(0, 0, 0, 0),
            [PdfNames.F] = PdfInteger.Get(132),
            [PdfNames.P] = page.Reference,
        };
        var widgetReference = document.Objects.Add(widget);
        fields.Add(widgetReference);
        if (page.Dictionary.GetResolved(PdfNames.Annots) is PdfArray annotations)
        {
            annotations.Add(widgetReference);
        }
        else
        {
            page.Dictionary[PdfNames.Annots] = new PdfArray(widgetReference);
        }
    }

    private static string GetFieldName(PdfArray fields)
    {
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var field in fields)
        {
            if (PdfReference.ResolveIfReference(field) is PdfDictionary dictionary && dictionary.GetText(PdfName.Create("T")) is { } name)
            {
                names.Add(name);
            }
        }

        for (var i = 1; ; i++)
        {
            var name = "Signature" + i.ToString(CultureInfo.InvariantCulture);
            if (!names.Contains(name))
                return name;
        }
    }

    private static void SetText(PdfDictionary dictionary, string key, string? value)
    {
        if (value is not null)
        {
            dictionary[PdfName.Create(key)] = PdfString.FromText(value);
        }
    }
}
