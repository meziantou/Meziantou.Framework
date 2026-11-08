using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Security.Cryptography;
using System.Security.Cryptography.Pkcs;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using Meziantou.Framework.Pdf.Objects;
using Meziantou.Framework.Pdf.Text;

namespace Meziantou.Framework.Pdf.Signing;

/// <summary>
/// Signs documents (ISO 32000-2, 12.8): a signature field whose signature is a CAdES detached signature of the file
/// (PAdES baseline B-B, ETSI EN 319 142-1), stamped by a time stamp authority for B-T, the validation data of B-LT and
/// the document time stamps of B-LTA.
/// </summary>
/// <remarks>
/// Each operation appends an incremental update to the file, so that the signatures the document already has stay
/// valid. A document that was not read from a file is saved in full first. The changes made to a document read from a
/// file are saved in the update.
/// </remarks>
public static class PdfSigner
{
    // The placeholders of the byte ranges: wide enough for any offset of a file, replaced in place
    private const string ByteRangePlaceholder = "[0 9999999999 9999999999 9999999999]";
    private const int DefaultTimestampReservedSize = 16384;

    private static readonly PdfName VriName = PdfName.Create("VRI");

    /// <summary>Signs a document without time stamp (PAdES B-B).</summary>
    /// <param name="document">The document.</param>
    /// <param name="options">The options.</param>
    /// <returns>The signed file.</returns>
    /// <exception cref="ArgumentException">The certificate has no private key, the field name is used, or the options ask for a time stamp: use <see cref="SignAsync"/>.</exception>
    /// <exception cref="InvalidOperationException">The document has no pages, or its certification does not allow another signature.</exception>
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
    /// <exception cref="ArgumentException">The certificate has no private key, or the field name is used by a field that is not an unsigned signature field.</exception>
    /// <exception cref="InvalidOperationException">
    /// The document has no pages, its certification does not allow another signature (<see cref="PdfCertificationLevel.NoChanges"/>),
    /// or a certification signature is requested and the document is already signed.
    /// </exception>
    /// <exception cref="HttpRequestException">The time stamp authority could not be reached.</exception>
    /// <exception cref="CryptographicException">The signature or the time stamp could not be computed.</exception>
    public static async Task<byte[]> SignAsync(PdfDocument document, PdfSignatureOptions options, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(options);
        if (!options.Certificate.HasPrivateKey)
            throw new ArgumentException("The certificate has no private key", nameof(options));

        if (options.Certification is { } certification && !Enum.IsDefined(certification))
            throw new ArgumentException("The certification level is not valid", nameof(options));

        var (basisLength, signed) = OpenBasis(document);

        // A certification that allows no change forbids other signatures (12.8.2.2.2); it is the first signature (12.8.2.2.1)
        if (PdfSignatureFields.GetDocumentCertificationLevel(signed) == PdfCertificationLevel.NoChanges)
            throw new InvalidOperationException("The document is certified without allowing changes (DocMDP /P 1): another signature would invalidate its certification");

        if (options.Certification is not null && PdfSignatureFields.GetFields(signed).Exists(field => field.Type == "Sig" && field.Field.GetResolved(PdfSignatureFields.ValueName) is PdfDictionary))
            throw new InvalidOperationException("A certification signature is the first signature of a document, and the document is already signed");

        // The size of the signature: a signature of the same certificates, and room for a time stamp
        var reserved = options.ReservedSize;
        if (reserved == 0)
        {
            var sampleOptions = new PdfSignatureOptions(options.Certificate) { HashAlgorithm = options.HashAlgorithm, RsaSignaturePadding = options.RsaSignaturePadding };
            sampleOptions.Certificates.AddRange(options.Certificates);
            var sample = await CmsSignature.CreateAsync(new byte[64], sampleOptions, cancellationToken).ConfigureAwait(false);
            reserved = sample.Length + 2048 + (options.TimestampAuthority is null ? 0 : DefaultTimestampReservedSize);
        }
        else
        {
            CmsSignature.GetOid(options.HashAlgorithm);
        }

        var signature = CreateSignatureDictionary("Sig", "ETSI.CAdES.detached", reserved);
        signature[PdfName.Create("M")] = PdfString.FromText(PdfDate.Format(options.SigningTime ?? DateTimeOffset.UtcNow));
        SetText(signature, "Name", options.SignerName);
        SetText(signature, "Reason", options.Reason);
        SetText(signature, "Location", options.Location);
        SetText(signature, "ContactInfo", options.ContactInfo);

        var field = AddSignatureField(signed, options.FieldName, options.Appearance, signature, nameof(options));
        var references = new PdfArray();
        if (options.Certification is { } level)
        {
            references.Add(PdfSignatureFields.CreateDocMdpReference(level));
        }

        // The lock of the field applies when the options do not give one (12.7.5.5)
        var fieldLock = options.FieldLock ?? (field.GetResolved(PdfSignatureFields.LockName) is PdfDictionary lockDictionary ? PdfSignatureFields.ReadLock(lockDictionary) : null);
        if (fieldLock is not null)
        {
            references.Add(PdfSignatureFields.CreateFieldMdpReference(fieldLock, signed.Trailer[PdfNames.Root] as PdfReference));
            if (options.FieldLock is not null)
            {
                field[PdfSignatureFields.LockName] = PdfSignatureFields.CreateLockDictionary(fieldLock, "SigFieldLock");
            }
        }

        if (references.Count > 0)
        {
            signature[PdfSignatureFields.ReferenceName] = references;
        }

        if (options.Certification is not null)
        {
            // The certification signature is the DocMDP entry of the permissions of the catalog (12.8.4.2)
            if (signed.Catalog.GetResolved(PdfSignatureFields.PermsName) is not PdfDictionary permissions)
            {
                permissions = [];
                signed.Catalog[PdfSignatureFields.PermsName] = permissions;
            }

            permissions[PdfSignatureFields.DocMdpName] = (PdfReference)field[PdfSignatureFields.ValueName]!;
        }

        var file = SaveUpdate(signed, basisLength, reserved);
        var (contentsStart, contentsEnd) = FillByteRange(file, basisLength, reserved);
        var digest = CmsSignature.Hash(options.HashAlgorithm, file.AsSpan(0, contentsStart), file.AsSpan(contentsEnd));
        var contents = await CmsSignature.CreateAsync(digest, options, cancellationToken).ConfigureAwait(false);
        WriteContents(file, contentsStart, contents, reserved, nameof(PdfSignatureOptions.ReservedSize));
        return file;
    }

    /// <summary>
    /// Adds a document time stamp (ISO 32000-2, 12.8.5): an RFC 3161 time stamp of the file, in a signature field. After
    /// <see cref="AddValidationData(PdfDocument, PdfValidationData)"/>, it protects the validation data of the signatures (PAdES B-LTA).
    /// </summary>
    /// <param name="document">The document.</param>
    /// <param name="options">The options.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>The time-stamped file.</returns>
    /// <exception cref="ArgumentException">The field name is used by a field that is not an unsigned signature field, or the digest algorithm is not supported.</exception>
    /// <exception cref="InvalidOperationException">The document has no pages, or the reserved size is too small for the time stamp token.</exception>
    /// <exception cref="HttpRequestException">The time stamp authority could not be reached.</exception>
    /// <exception cref="CryptographicException">The response of the time stamp authority is not valid.</exception>
    public static async Task<byte[]> TimestampAsync(PdfDocument document, PdfTimestampOptions options, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(options);
        CmsSignature.GetOid(options.HashAlgorithm);

        var (basisLength, signed) = OpenBasis(document);
        var reserved = options.ReservedSize == 0 ? DefaultTimestampReservedSize : options.ReservedSize;
        var signature = CreateSignatureDictionary("DocTimeStamp", "ETSI.RFC3161", reserved);
        AddSignatureField(signed, options.FieldName, appearance: null, signature, nameof(options));

        var file = SaveUpdate(signed, basisLength, reserved);
        var (contentsStart, contentsEnd) = FillByteRange(file, basisLength, reserved);
        var digest = CmsSignature.Hash(options.HashAlgorithm, file.AsSpan(0, contentsStart), file.AsSpan(contentsEnd));
        var token = await CmsSignature.RequestTimestampAsync(options.HttpClient, options.TimestampAuthority, digest, options.HashAlgorithm, cancellationToken).ConfigureAwait(false);
        WriteContents(file, contentsStart, token, reserved, nameof(PdfTimestampOptions.ReservedSize));
        return file;
    }

    /// <summary>
    /// Adds validation data to the document security store (ISO 32000-2, 12.8.4.3; PAdES B-LT): the certificates and the
    /// revocation evidence of the data, and the certificates the signatures and their time stamps embed.
    /// </summary>
    /// <param name="document">The document.</param>
    /// <param name="validationData">The certificates and the revocation evidence; the data the store already has is not added again.</param>
    /// <returns>The file with the validation data.</returns>
    public static byte[] AddValidationData(PdfDocument document, PdfValidationData validationData)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(validationData);

        var (basisLength, signed) = OpenBasis(document);
        var store = GetOrCreateStore(signed);
        var certificates = new List<byte[]>();
        foreach (var certificate in validationData.Certificates)
        {
            certificates.Add(certificate.RawData);
        }

        foreach (var field in PdfSignatureFields.GetFields(signed))
        {
            if (field.Type == "Sig" && field.Field.GetResolved(PdfSignatureFields.ValueName) is PdfDictionary signature && signature.TryGetResolved<PdfString>(PdfNames.Contents, out var contents))
            {
                AddEmbeddedCertificates(contents.Span, certificates);
            }
        }

        AddStreams(signed, store, "Certs", certificates);
        AddStreams(signed, store, "OCSPs", validationData.OcspResponses);
        AddStreams(signed, store, "CRLs", validationData.CertificateRevocationLists);
        return SaveValidationData(signed, basisLength);
    }

    /// <summary>
    /// Adds the validation data of the signatures and of the document time stamps to the document security store
    /// (ISO 32000-2, 12.8.4.3; PAdES B-LT), from the data of the options and of the store, without downloading anything.
    /// </summary>
    /// <param name="document">The document.</param>
    /// <param name="options">The options.</param>
    /// <returns>The file with the validation data.</returns>
    /// <exception cref="ArgumentException">The options ask to download the missing data: use <see cref="AddValidationDataAsync"/>.</exception>
    public static byte[] AddValidationData(PdfDocument document, PdfValidationDataOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (options.DownloadMissingData)
            throw new ArgumentException("The missing data is downloaded: use AddValidationDataAsync", nameof(options));

        return AddValidationDataAsync(document, options, CancellationToken.None).GetAwaiter().GetResult();
    }

    /// <summary>
    /// Adds the validation data of the signatures and of the document time stamps to the document security store
    /// (ISO 32000-2, 12.8.4.3; PAdES B-LT): the chains of the signers and of the time stamp authorities, completed with
    /// the certificates of the options and of the store, and the revocation evidence of each certificate of the chains
    /// that is not self-issued. With <see cref="PdfValidationDataOptions.DownloadMissingData"/>, the issuers and the
    /// revocation evidence that are missing are downloaded.
    /// </summary>
    /// <param name="document">The document.</param>
    /// <param name="options">The options.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>The file with the validation data.</returns>
    /// <exception cref="HttpRequestException">The revocation evidence of a certificate could not be downloaded from any of its addresses.</exception>
    public static async Task<byte[]> AddValidationDataAsync(PdfDocument document, PdfValidationDataOptions options, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(options);

        var (basisLength, signed) = OpenBasis(document);
        var store = GetOrCreateStore(signed);
        using var collector = new ValidationDataCollector(options.DownloadMissingData, options.HttpClient);
        foreach (var certificate in options.Certificates)
        {
            collector.AddCertificate(certificate);
        }

        foreach (var data in ReadStreams(store, "Certs"))
        {
            collector.AddCertificate(data);
        }

        foreach (var data in options.CertificateRevocationLists.Concat(ReadStreams(store, "CRLs")))
        {
            collector.AddCertificateRevocationList(data);
        }

        foreach (var data in options.OcspResponses.Concat(ReadStreams(store, "OCSPs")))
        {
            collector.AddOcspResponse(data);
        }

        // The signatures and the document time stamps (12.8.5): the certificates they embed complete the chains
        var signatures = new List<(PdfString Contents, List<X509Certificate2> Signers)>();
        foreach (var field in PdfSignatureFields.GetFields(signed))
        {
            if (field.Type == "Sig" && field.Field.GetResolved(PdfSignatureFields.ValueName) is PdfDictionary signature && signature.TryGetResolved<PdfString>(PdfNames.Contents, out var contents))
            {
                signatures.Add((contents, collector.AddSignature(contents.Span)));
            }
        }

        var sets = new List<(PdfString Contents, ValidationDataSet Data)>();
        foreach (var (contents, signers) in signatures)
        {
            sets.Add((contents, await collector.CollectAsync(signers, cancellationToken).ConfigureAwait(false)));
        }

        var certificates = AddStreams(signed, store, "Certs", collector.Certificates);
        var ocsps = AddStreams(signed, store, "OCSPs", collector.OcspResponses);
        var crls = AddStreams(signed, store, "CRLs", collector.CertificateRevocationLists);
        if (options.IncludeVri)
        {
            // The key of an entry is the SHA-1 digest of the /Contents of the signature, in uppercase hexadecimal (12.8.4.4)
            if (store.GetResolved(VriName) is not PdfDictionary vri)
            {
                vri = [];
                store[VriName] = vri;
            }

            foreach (var (contents, data) in sets)
            {
                var entry = new PdfDictionary();
                AddReferences(entry, "Cert", data.Certificates, certificates);
                AddReferences(entry, "OCSP", data.OcspResponses, ocsps);
                AddReferences(entry, "CRL", data.CertificateRevocationLists, crls);
                vri[PdfName.Create(GetVriKey(contents.Span))] = entry;
            }
        }

        return SaveValidationData(signed, basisLength);
    }

    [SuppressMessage("Security", "CA5350:Do Not Use Weak Cryptographic Algorithms", Justification = "ISO 32000-2 (12.8.4.4) names the entries of the VRI dictionary with the SHA-1 digest of the signature, it is not used for security")]
    private static string GetVriKey(ReadOnlySpan<byte> contents) => Convert.ToHexString(SHA1.HashData(contents));

    private static void AddReferences(PdfDictionary entry, string key, List<byte[]> values, Dictionary<string, PdfReference> references)
    {
        var array = new PdfArray();
        foreach (var value in values)
        {
            if (references.TryGetValue(GetStreamKey(value), out var reference))
            {
                array.Add(reference);
            }
        }

        if (array.Count > 0)
        {
            entry[PdfName.Create(key)] = array;
        }
    }

    private static PdfDictionary GetOrCreateStore(PdfDocument document)
    {
        var catalog = document.Catalog;
        if (catalog.GetResolved(PdfSignatureFields.DssName) is not PdfDictionary store)
        {
            store = new PdfDictionary { [PdfNames.Type] = PdfName.Create("DSS") };
            catalog[PdfSignatureFields.DssName] = document.Objects.Add(store);
        }

        return store;
    }

    private static byte[] SaveValidationData(PdfDocument document, int basisLength)
    {
        using var stream = new MemoryStream(basisLength + 65536);
        document.SaveIncremental(stream);
        return stream.ToArray();
    }

    private static IEnumerable<byte[]> ReadStreams(PdfDictionary store, string key)
    {
        if (store.GetResolved(PdfName.Create(key)) is not PdfArray array)
            yield break;

        foreach (var item in array)
        {
            if (PdfReference.ResolveIfReference(item) is PdfStream stream)
            {
                yield return stream.GetDecodedData();
            }
        }
    }

    /// <summary>The certificates of the signed data of a signature and of its time stamps, which some validators need in the store.</summary>
    private static void AddEmbeddedCertificates(ReadOnlySpan<byte> contents, List<byte[]> certificates)
    {
        try
        {
            System.Formats.Asn1.AsnDecoder.ReadEncodedValue(contents, System.Formats.Asn1.AsnEncodingRules.BER, out _, out _, out var length);
            var cms = new SignedCms();
            cms.Decode(contents[..length]);
            foreach (var certificate in cms.Certificates)
            {
                certificates.Add(certificate.RawData);
            }

            foreach (var signer in cms.SignerInfos)
            {
                foreach (var attribute in signer.UnsignedAttributes)
                {
                    if (attribute.Oid.Value != CmsSignature.SignatureTimeStampTokenOid)
                        continue;

                    foreach (var value in attribute.Values)
                    {
                        AddEmbeddedCertificates(value.RawData, certificates);
                    }
                }
            }
        }
        catch (Exception ex) when (ex is CryptographicException or System.Formats.Asn1.AsnContentException)
        {
            // A signature that cannot be decoded has no certificates to add
        }
    }

    /// <summary>Adds the values the array of the store does not have yet, each in a stream.</summary>
    /// <returns>The streams of the array, by the digest of their data.</returns>
    private static Dictionary<string, PdfReference> AddStreams(PdfDocument document, PdfDictionary store, string key, IEnumerable<byte[]> values)
    {
        var name = PdfName.Create(key);
        var array = store.GetResolved(name) as PdfArray;
        var existing = new HashSet<string>(StringComparer.Ordinal);
        var references = new Dictionary<string, PdfReference>(StringComparer.Ordinal);
        if (array is not null)
        {
            foreach (var item in array)
            {
                if (PdfReference.ResolveIfReference(item) is PdfStream stream)
                {
                    var streamKey = GetStreamKey(stream.GetDecodedData());
                    existing.Add(streamKey);
                    if (item is PdfReference reference)
                    {
                        references.TryAdd(streamKey, reference);
                    }
                }
            }
        }

        foreach (var value in values)
        {
            var streamKey = GetStreamKey(value);
            if (!existing.Add(streamKey))
                continue;

            if (array is null)
            {
                array = [];
                store[name] = array;
            }

            var stream = new PdfStream();
            stream.SetData(value);
            var reference = document.Objects.Add(stream);
            array.Add(reference);
            references[streamKey] = reference;
        }

        return references;
    }

    private static string GetStreamKey(byte[] data) => Convert.ToHexString(SHA256.HashData(data));

    /// <summary>The file the update is appended to, and the document read from it with the same password.</summary>
    private static (int BasisLength, PdfDocument Document) OpenBasis(PdfDocument document)
    {
        using var stream = new MemoryStream();
        if (document.Reader is { IsRepaired: false } reader)
        {
            stream.Capacity = (int)Math.Min(reader.Input.Length + 65536, Array.MaxLength);
            document.SaveIncremental(stream);
        }
        else
        {
            document.Save(stream);
        }

        // The buffer of the stream is read in place: the file is not copied
        var password = document.Reader?.Options.Password ?? document.Encryption?.OwnerPassword ?? document.Encryption?.UserPassword;
        // A new encryption for certificates is opened with a recipient that has its private key
        var certificate = document.Encryption?.Recipients.FirstOrDefault(recipient => recipient.Certificate.HasPrivateKey)?.Certificate ?? document.Reader?.Options.Certificate;
        var basis = stream.GetBuffer().AsMemory(0, (int)stream.Length);
        return (basis.Length, PdfDocument.Open(basis, new PdfReadOptions { Password = password, Certificate = certificate }));
    }

    private static byte[] SaveUpdate(PdfDocument document, int basisLength, int reserved)
    {
        using var stream = new MemoryStream(basisLength + (reserved * 2) + 65536);
        document.SaveIncremental(stream);
        return stream.ToArray();
    }

    private static PdfDictionary CreateSignatureDictionary(string type, string subFilter, int reserved) => new()
    {
        [PdfNames.Type] = PdfName.Create(type),
        [PdfNames.Filter] = PdfName.Create("Adobe.PPKLite"),
        [PdfSignatureFields.SubFilterName] = PdfName.Create(subFilter),
        [PdfSignatureFields.ByteRangeName] = new PdfArray(PdfInteger.Get(0), PdfInteger.Get(9999999999), PdfInteger.Get(9999999999), PdfInteger.Get(9999999999)),
        [PdfNames.Contents] = PdfString.FromBytes(new byte[reserved], hexadecimal: true),
    };

    /// <summary>Writes the byte ranges in place of their placeholder: the whole file but the hexadecimal string of <c>/Contents</c> (12.8.1).</summary>
    private static (int ContentsStart, int ContentsEnd) FillByteRange(byte[] file, int basisLength, int reserved)
    {
        var placeholder = Encoding.ASCII.GetBytes("<" + new string('0', reserved * 2) + ">");
        var contentsStart = file.AsSpan(basisLength).IndexOf(placeholder);
        var byteRangeStart = file.AsSpan(basisLength).IndexOf(Encoding.ASCII.GetBytes(ByteRangePlaceholder));
        if (contentsStart < 0 || byteRangeStart < 0)
            throw new InvalidOperationException("The placeholders of the signature were not found in the file");

        contentsStart += basisLength;
        byteRangeStart += basisLength;
        var contentsEnd = contentsStart + placeholder.Length;
        var byteRange = string.Create(CultureInfo.InvariantCulture, $"[0 {contentsStart} {contentsEnd} {file.Length - contentsEnd}");
        byteRange = byteRange.PadRight(ByteRangePlaceholder.Length - 1) + "]";
        Encoding.ASCII.GetBytes(byteRange).CopyTo(file, byteRangeStart);
        return (contentsStart, contentsEnd);
    }

    private static void WriteContents(byte[] file, int contentsStart, byte[] contents, int reserved, string reservedSizeName)
    {
        if (contents.Length > reserved)
            throw new InvalidOperationException($"The signature takes {contents.Length} bytes, more than the {reserved} bytes reserved: set {reservedSizeName}");

        var hex = Convert.ToHexString(contents);
        Encoding.ASCII.GetBytes(hex).CopyTo(file, contentsStart + 1);
    }

    /// <summary>
    /// Adds the signature dictionary to a signature field: an unsigned signature field of that name, or a new field with
    /// its widget, invisible on the first page or visible where the appearance says (12.7.5.5).
    /// </summary>
    /// <returns>The field.</returns>
    private static PdfDictionary AddSignatureField(PdfDocument document, string? fieldName, PdfSignatureAppearance? appearance, PdfDictionary signature, string parameterName)
    {
        if (document.Pages.Count == 0)
            throw new InvalidOperationException("A document without pages cannot be signed");

        var catalog = document.Catalog;
        if (catalog.GetResolved(PdfNames.AcroForm) is not PdfDictionary form)
        {
            form = [];
            catalog[PdfNames.AcroForm] = document.Objects.Add(form);
        }

        if (form.GetResolved(PdfSignatureFields.FieldsName) is not PdfArray fields)
        {
            fields = [];
            form[PdfSignatureFields.FieldsName] = fields;
        }

        // SignaturesExist and AppendOnly (12.7.3)
        form[PdfSignatureFields.SigFlagsName] = PdfInteger.Get(3);

        // The fully qualified names of the fields are unique (12.7.4.2): a signature field that is not signed yet is signed
        var existingFields = PdfSignatureFields.GetFields(document);
        if (fieldName is not null && existingFields.Find(entry => entry.Name == fieldName) is { } existing)
        {
            if (existing.Type != "Sig" || existing.Field.GetResolved(PdfSignatureFields.ValueName) is not null)
                throw new ArgumentException($"The document already has a field named '{fieldName}' that is not an unsigned signature field", parameterName);

            if (appearance is not null)
                throw new ArgumentException($"The field '{fieldName}' exists: it keeps its widget, and an appearance cannot be given", parameterName);

            existing.Field[PdfSignatureFields.ValueName] = document.Objects.Add(signature);
            return existing.Field;
        }

        // A partial name has no period (12.7.4.2)
        if (fieldName is not null && (fieldName.Length == 0 || fieldName.Contains('.', StringComparison.Ordinal)))
            throw new ArgumentException($"The field name '{fieldName}' is empty or has a period: the name of a new field is a partial name", parameterName);

        var pageIndex = appearance?.PageIndex ?? 0;
        if (pageIndex >= document.Pages.Count)
            throw new ArgumentException($"The appearance of the signature is on page {pageIndex}, and the document has {document.Pages.Count} pages", parameterName);

        var page = document.Pages[pageIndex];
        var widget = new PdfDictionary
        {
            [PdfNames.Type] = PdfNames.Annot,
            [PdfNames.Subtype] = PdfSignatureFields.WidgetName,
            [PdfSignatureFields.FieldTypeName] = PdfName.Create("Sig"),
            [PdfSignatureFields.FieldNameName] = PdfString.FromText(fieldName ?? GetFieldName(existingFields)),
            [PdfSignatureFields.ValueName] = document.Objects.Add(signature),
            [PdfNames.Rect] = appearance?.Rectangle.ToArray() ?? PdfArray.FromNumbers(0, 0, 0, 0),
            [PdfNames.F] = PdfInteger.Get(132), // Print and Locked (12.5.3)
            [PdfNames.P] = page.Reference,
        };
        if (appearance is not null)
        {
            // The normal appearance (12.5.5): a form of the size of the rectangle
            var appearanceForm = document.CreateForm(new PdfRectangle(0, 0, appearance.Rectangle.Width, appearance.Rectangle.Height));
            appearance.Draw?.Invoke(appearanceForm.CreateContentBuilder());
            widget[PdfSignatureFields.AppearanceName] = new PdfDictionary { [PdfNames.N] = appearanceForm.Reference };
        }

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

        return widget;
    }

    /// <summary>The first name <c>SignatureN</c> that no field uses as its fully qualified name.</summary>
    private static string GetFieldName(List<PdfFieldEntry> fields)
    {
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var field in fields)
        {
            names.Add(field.Name);
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
