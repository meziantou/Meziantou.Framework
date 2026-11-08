using System.Diagnostics.CodeAnalysis;
using System.Formats.Asn1;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Security.Cryptography.Pkcs;
using System.Security.Cryptography.X509Certificates;

namespace Meziantou.Framework.Pdf.Signing;

/// <summary>
/// Gathers the validation data of the signatures of a document (PAdES B-LT, ETSI EN 319 142-1, 5.4): the chains of the
/// signers and of the time stamp authorities, and the revocation evidence of each certificate of the chains, from the
/// data given and the document security store, or downloaded.
/// </summary>
internal sealed class ValidationDataCollector : IDisposable
{
    private const int MaxChainLength = 16;
    private const string AuthorityInformationAccessOid = "1.3.6.1.5.5.7.1.1";
    private const string CrlDistributionPointsOid = "2.5.29.31";
    private const string AuthorityKeyIdentifierOid = "2.5.29.35";
    private const string SubjectKeyIdentifierOid = "2.5.29.14";
    private const string OcspBasicOid = "1.3.6.1.5.5.7.48.1.1";
    private const string Sha1Oid = "1.3.14.3.2.26";

    private static readonly Asn1Tag ContextTag0 = new(TagClass.ContextSpecific, 0, isConstructed: true);
    private static readonly Asn1Tag UriTag = new(TagClass.ContextSpecific, 6);

    private readonly List<X509Certificate2> _certificates = [];
    private readonly List<X509Certificate2> _ownedCertificates = [];
    private readonly List<CrlEntry> _crls = [];
    private readonly List<OcspEntry> _ocsps = [];
    private readonly bool _download;
    private readonly HttpClient? _client;
    private HttpClient? _ownedClient;

    public ValidationDataCollector(bool download, HttpClient? client)
    {
        _download = download;
        _client = client;
    }

    /// <summary>Gets the certificates to store: those given, those of the store, those the signatures embed and those downloaded.</summary>
    public IEnumerable<byte[]> Certificates => _certificates.Select(certificate => certificate.RawData);

    public IEnumerable<byte[]> CertificateRevocationLists => _crls.Select(crl => crl.Data);

    public IEnumerable<byte[]> OcspResponses => _ocsps.Select(ocsp => ocsp.Data);

    public void Dispose()
    {
        foreach (var certificate in _ownedCertificates)
        {
            certificate.Dispose();
        }

        _ownedClient?.Dispose();
    }

    public void AddCertificate(X509Certificate2 certificate)
    {
        if (!_certificates.Exists(item => item.RawData.AsSpan().SequenceEqual(certificate.RawData)))
        {
            _certificates.Add(certificate);
        }
    }

    public X509Certificate2? AddCertificate(ReadOnlySpan<byte> data)
    {
        foreach (var item in _certificates)
        {
            if (item.RawData.AsSpan().SequenceEqual(data))
                return item;
        }

        try
        {
            var certificate = X509CertificateLoader.LoadCertificate(data);
            _ownedCertificates.Add(certificate);
            _certificates.Add(certificate);
            return certificate;
        }
        catch (CryptographicException)
        {
            // A certificate that cannot be read does not complete any chain
            return null;
        }
    }

    /// <summary>Adds a revocation list; one that cannot be read is stored, and covers no certificate.</summary>
    public CrlEntry AddCertificateRevocationList(byte[] data)
    {
        var existing = _crls.Find(item => item.Data.AsSpan().SequenceEqual(data));
        if (existing is not null)
            return existing;

        var entry = new CrlEntry(data, TryReadCrlIssuer(data));
        _crls.Add(entry);
        return entry;
    }

    /// <summary>Adds an OCSP response and the certificates of its responder; one that cannot be read is stored, and covers no certificate.</summary>
    public OcspEntry AddOcspResponse(byte[] data)
    {
        var existing = _ocsps.Find(item => item.Data.AsSpan().SequenceEqual(data));
        if (existing is not null)
            return existing;

        var entry = TryReadOcspResponse(data) ?? new OcspEntry(data, Successful: false, [], []);
        _ocsps.Add(entry);
        foreach (var certificate in entry.Certificates)
        {
            AddCertificate(certificate);
        }

        return entry;
    }

    /// <summary>The signers of a CMS signed data (a signature, or a time stamp token) and of its time stamps; their certificates go to the pool.</summary>
    public List<X509Certificate2> AddSignature(ReadOnlySpan<byte> contents)
    {
        var signers = new List<X509Certificate2>();
        AddSignature(contents, signers, depth: 0);
        return signers;
    }

    private void AddSignature(ReadOnlySpan<byte> contents, List<X509Certificate2> signers, int depth)
    {
        if (depth > 2)
            return;

        try
        {
            // The /Contents of a signature is padded with zeros after the DER value
            AsnDecoder.ReadEncodedValue(contents, AsnEncodingRules.BER, out _, out _, out var length);
            var cms = new SignedCms();
            cms.Decode(contents[..length]);
            foreach (var certificate in cms.Certificates)
            {
                AddCertificate(certificate.RawData);
            }

            foreach (var signer in cms.SignerInfos)
            {
                if (signer.Certificate is { } certificate && AddCertificate(certificate.RawData) is { } pooled && !signers.Contains(pooled))
                {
                    signers.Add(pooled);
                }

                foreach (var attribute in signer.UnsignedAttributes)
                {
                    if (attribute.Oid.Value != CmsSignature.SignatureTimeStampTokenOid)
                        continue;

                    foreach (var value in attribute.Values)
                    {
                        AddSignature(value.RawData, signers, depth + 1);
                    }
                }
            }
        }
        catch (Exception ex) when (ex is CryptographicException or AsnContentException)
        {
            // A signature that cannot be decoded has no chains to validate
        }
    }

    /// <summary>The chains of the signers, and the revocation evidence of each certificate of the chains that is not self-issued.</summary>
    public async Task<ValidationDataSet> CollectAsync(List<X509Certificate2> signers, CancellationToken cancellationToken)
    {
        var result = new ValidationDataSet();
        foreach (var signer in signers)
        {
            var chain = await BuildChainAsync(signer, cancellationToken).ConfigureAwait(false);
            for (var i = 0; i < chain.Count; i++)
            {
                result.AddCertificate(chain[i].RawData);
                if (!IsSelfIssued(chain[i]))
                {
                    await AddRevocationEvidenceAsync(chain[i], i + 1 < chain.Count ? chain[i + 1] : null, result, cancellationToken).ConfigureAwait(false);
                }
            }
        }

        return result;
    }

    private async Task<List<X509Certificate2>> BuildChainAsync(X509Certificate2 certificate, CancellationToken cancellationToken)
    {
        var chain = new List<X509Certificate2> { certificate };
        var current = certificate;
        while (!IsSelfIssued(current) && chain.Count < MaxChainLength)
        {
            var issuer = FindIssuer(current);
            if (issuer is null && _download)
            {
                await DownloadIssuersAsync(current, cancellationToken).ConfigureAwait(false);
                issuer = FindIssuer(current);
            }

            if (issuer is null || chain.Contains(issuer))
                break;

            chain.Add(issuer);
            current = issuer;
        }

        return chain;
    }

    /// <summary>The certificate whose subject is the issuer of the certificate, and whose key identifier is its authority key identifier when both have one (RFC 5280, 4.2.1.1).</summary>
    private X509Certificate2? FindIssuer(X509Certificate2 certificate)
    {
        var authorityKeyIdentifier = GetAuthorityKeyIdentifier(certificate);
        X509Certificate2? candidate = null;
        foreach (var item in _certificates)
        {
            if (item == certificate || !item.SubjectName.RawData.AsSpan().SequenceEqual(certificate.IssuerName.RawData))
                continue;

            if (authorityKeyIdentifier is null)
                return item;

            var subjectKeyIdentifier = GetSubjectKeyIdentifier(item);
            if (subjectKeyIdentifier is null)
            {
                candidate ??= item;
            }
            else if (subjectKeyIdentifier.AsSpan().SequenceEqual(authorityKeyIdentifier))
            {
                return item;
            }
        }

        return candidate;
    }

    /// <summary>The certificates of the <c>caIssuers</c> addresses of the certificate (RFC 5280, 4.2.2.1): a DER certificate, or a CMS "certs-only" message.</summary>
    private async Task DownloadIssuersAsync(X509Certificate2 certificate, CancellationToken cancellationToken)
    {
        foreach (var uri in GetAuthorityInformationAccess(certificate, ocsp: false))
        {
            byte[] data;
            try
            {
                data = await GetAsync(uri, cancellationToken).ConfigureAwait(false);
            }
            catch (HttpRequestException)
            {
                // Another address may have the issuer; the chain stops where no issuer is found
                continue;
            }

            if (AddCertificate(data) is null)
            {
                try
                {
                    var cms = new SignedCms();
                    cms.Decode(data);
                    foreach (var item in cms.Certificates)
                    {
                        AddCertificate(item.RawData);
                    }
                }
                catch (CryptographicException)
                {
                    // The address has no certificate
                }
            }

            if (FindIssuer(certificate) is not null)
                return;
        }
    }

    /// <summary>
    /// The revocation evidence of a certificate: the OCSP responses and the revocation lists of the pool that cover it,
    /// or else an OCSP response of its responder, or else the revocation list of one of its distribution points.
    /// </summary>
    private async Task AddRevocationEvidenceAsync(X509Certificate2 certificate, X509Certificate2? issuer, ValidationDataSet result, CancellationToken cancellationToken)
    {
        var found = false;
        foreach (var ocsp in _ocsps)
        {
            if (Covers(ocsp, certificate))
            {
                AddOcspToSet(ocsp, result);
                found = true;
            }
        }

        foreach (var crl in _crls)
        {
            if (crl.Issuer is not null && crl.Issuer.AsSpan().SequenceEqual(certificate.IssuerName.RawData))
            {
                result.AddCertificateRevocationList(crl.Data);
                found = true;
            }
        }

        if (found || !_download)
            return;

        Exception? error = null;
        var attempted = false;
        if (issuer is not null)
        {
            foreach (var uri in GetAuthorityInformationAccess(certificate, ocsp: true))
            {
                attempted = true;
                try
                {
                    var data = await PostOcspRequestAsync(uri, CreateOcspRequest(certificate, issuer), cancellationToken).ConfigureAwait(false);
                    var entry = AddOcspResponse(data);
                    if (Covers(entry, certificate))
                    {
                        AddOcspToSet(entry, result);
                        return;
                    }

                    error = new CryptographicException($"The OCSP response of '{uri}' is not a successful response for the certificate");
                }
                catch (HttpRequestException ex)
                {
                    error = ex;
                }
            }
        }

        foreach (var uri in GetCrlDistributionPoints(certificate))
        {
            attempted = true;
            try
            {
                var entry = AddCertificateRevocationList(await GetAsync(uri, cancellationToken).ConfigureAwait(false));
                if (entry.Issuer is not null && entry.Issuer.AsSpan().SequenceEqual(certificate.IssuerName.RawData))
                {
                    result.AddCertificateRevocationList(entry.Data);
                    return;
                }

                error = new CryptographicException($"The data of '{uri}' is not a revocation list of the issuer of the certificate");
            }
            catch (HttpRequestException ex)
            {
                error = ex;
            }
        }

        if (attempted)
            throw new HttpRequestException($"The revocation evidence of the certificate '{certificate.Subject}' could not be downloaded", error);
    }

    private static void AddOcspToSet(OcspEntry ocsp, ValidationDataSet result)
    {
        result.AddOcspResponse(ocsp.Data);
        foreach (var certificate in ocsp.Certificates)
        {
            result.AddCertificate(certificate);
        }
    }

    /// <summary>Whether a successful OCSP response has a single response for the certificate: its serial number, and the digest of the name of its issuer (RFC 6960, 4.1.1).</summary>
    private static bool Covers(OcspEntry ocsp, X509Certificate2 certificate)
    {
        if (!ocsp.Successful)
            return false;

        foreach (var id in ocsp.CertificateIds)
        {
            if (!id.SerialNumber.AsSpan().SequenceEqual(certificate.SerialNumberBytes.Span))
                continue;

            if (Hash(id.HashAlgorithm, certificate.IssuerName.RawData) is { } nameHash && nameHash.AsSpan().SequenceEqual(id.IssuerNameHash))
                return true;
        }

        return false;
    }

    [SuppressMessage("Security", "CA5350:Do Not Use Weak Cryptographic Algorithms", Justification = "SHA-1 identifies the certificate in an OCSP request (RFC 6960, 4.1.1; RFC 5019, 2.1.1), it is not used for security")]
    private static byte[]? Hash(string algorithm, byte[] data) => algorithm switch
    {
        Sha1Oid => SHA1.HashData(data),
        "2.16.840.1.101.3.4.2.1" => SHA256.HashData(data),
        "2.16.840.1.101.3.4.2.2" => SHA384.HashData(data),
        "2.16.840.1.101.3.4.2.3" => SHA512.HashData(data),
        _ => null,
    };

    /// <summary>
    /// An OCSP request of one certificate, without extension nor signature (RFC 6960, 4.1.1): its <c>CertID</c> uses
    /// SHA-1, the algorithm every responder supports (RFC 5019, 2.1.1).
    /// </summary>
    internal static byte[] CreateOcspRequest(X509Certificate2 certificate, X509Certificate2 issuer)
    {
        var writer = new AsnWriter(AsnEncodingRules.DER);
        using (writer.PushSequence())
        using (writer.PushSequence())
        using (writer.PushSequence())
        using (writer.PushSequence())
        using (writer.PushSequence())
        {
            using (writer.PushSequence())
            {
                writer.WriteObjectIdentifier(Sha1Oid);
                writer.WriteNull();
            }

            writer.WriteOctetString(Hash(Sha1Oid, certificate.IssuerName.RawData));
            writer.WriteOctetString(Hash(Sha1Oid, issuer.PublicKey.EncodedKeyValue.RawData));
            writer.WriteInteger(certificate.SerialNumberBytes.Span);
        }

        return writer.Encode();
    }

    /// <summary>The issuer of a revocation list (RFC 5280, 5.1); <see langword="null"/> when the data is not one.</summary>
    private static byte[]? TryReadCrlIssuer(byte[] data)
    {
        try
        {
            var list = new AsnReader(data, AsnEncodingRules.BER).ReadSequence().ReadSequence();
            if (list.PeekTag().HasSameClassAndValue(Asn1Tag.Integer))
            {
                list.ReadEncodedValue();
            }

            list.ReadEncodedValue();
            return list.ReadEncodedValue().ToArray();
        }
        catch (AsnContentException)
        {
            return null;
        }
    }

    /// <summary>The status of an OCSP response, the identifiers of the certificates of its single responses and the certificates of its responder (RFC 6960, 4.2.1); <see langword="null"/> when the data is not one.</summary>
    private static OcspEntry? TryReadOcspResponse(byte[] data)
    {
        try
        {
            var response = new AsnReader(data, AsnEncodingRules.BER).ReadSequence();
            var status = response.ReadEnumeratedBytes().Span;
            if (status is not [0] || !response.HasData)
                return new OcspEntry(data, Successful: false, [], []);

            var responseBytes = response.ReadSequence(ContextTag0).ReadSequence();
            if (responseBytes.ReadObjectIdentifier() != OcspBasicOid)
                return new OcspEntry(data, Successful: false, [], []);

            var basic = new AsnReader(responseBytes.ReadOctetString(), AsnEncodingRules.BER).ReadSequence();
            var responseData = basic.ReadSequence();
            if (responseData.PeekTag().HasSameClassAndValue(ContextTag0))
            {
                responseData.ReadEncodedValue(); // version
            }

            responseData.ReadEncodedValue(); // responderID
            responseData.ReadEncodedValue(); // producedAt
            var ids = new List<OcspCertificateId>();
            var responses = responseData.ReadSequence();
            while (responses.HasData)
            {
                var certificateId = responses.ReadSequence().ReadSequence();
                var algorithm = certificateId.ReadSequence().ReadObjectIdentifier();
                var nameHash = certificateId.ReadOctetString();
                certificateId.ReadOctetString(); // issuerKeyHash
                ids.Add(new OcspCertificateId(algorithm, nameHash, certificateId.ReadIntegerBytes().ToArray()));
            }

            basic.ReadEncodedValue(); // signatureAlgorithm
            basic.ReadEncodedValue(); // signature
            var certificates = new List<byte[]>();
            if (basic.HasData && basic.PeekTag().HasSameClassAndValue(ContextTag0))
            {
                var sequence = basic.ReadSequence(ContextTag0).ReadSequence();
                while (sequence.HasData)
                {
                    certificates.Add(sequence.ReadEncodedValue().ToArray());
                }
            }

            return new OcspEntry(data, Successful: true, ids, certificates);
        }
        catch (AsnContentException)
        {
            return null;
        }
    }

    private static bool IsSelfIssued(X509Certificate2 certificate) => certificate.SubjectName.RawData.AsSpan().SequenceEqual(certificate.IssuerName.RawData);

    private static byte[]? GetAuthorityKeyIdentifier(X509Certificate2 certificate)
    {
        if (certificate.Extensions[AuthorityKeyIdentifierOid] is not { } extension)
            return null;

        try
        {
            return new X509AuthorityKeyIdentifierExtension(extension.RawData).KeyIdentifier?.ToArray();
        }
        catch (CryptographicException)
        {
            return null;
        }
    }

    private static byte[]? GetSubjectKeyIdentifier(X509Certificate2 certificate)
    {
        if (certificate.Extensions[SubjectKeyIdentifierOid] is not { } extension)
            return null;

        try
        {
            return new X509SubjectKeyIdentifierExtension(extension, extension.Critical).SubjectKeyIdentifierBytes.ToArray();
        }
        catch (CryptographicException)
        {
            return null;
        }
    }

    /// <summary>The HTTP addresses of the OCSP responders or of the issuers of a certificate (RFC 5280, 4.2.2.1).</summary>
    private static List<Uri> GetAuthorityInformationAccess(X509Certificate2 certificate, bool ocsp)
    {
        var result = new List<Uri>();
        if (certificate.Extensions[AuthorityInformationAccessOid] is not { } extension)
            return result;

        try
        {
            var access = new X509AuthorityInformationAccessExtension(extension.RawData, extension.Critical);
            AddHttpUris(result, ocsp ? access.EnumerateOcspUris() : access.EnumerateCAIssuersUris());
        }
        catch (CryptographicException)
        {
            // An extension that cannot be read has no address
        }

        return result;
    }

    /// <summary>The HTTP addresses of the full names of the distribution points of a certificate (RFC 5280, 4.2.1.13).</summary>
    internal static List<Uri> GetCrlDistributionPoints(X509Certificate2 certificate)
    {
        var result = new List<Uri>();
        if (certificate.Extensions[CrlDistributionPointsOid] is not { } extension)
            return result;

        try
        {
            var values = new List<string>();
            var points = new AsnReader(extension.RawData, AsnEncodingRules.BER).ReadSequence();
            while (points.HasData)
            {
                var point = points.ReadSequence();
                if (!point.HasData || !point.PeekTag().HasSameClassAndValue(ContextTag0))
                    continue;

                // distributionPoint [0] is a CHOICE, explicitly tagged; its fullName [0] is GeneralNames, implicitly tagged
                var name = point.ReadSequence(ContextTag0);
                if (!name.HasData || !name.PeekTag().HasSameClassAndValue(ContextTag0))
                    continue;

                var fullName = name.ReadSequence(ContextTag0);
                while (fullName.HasData)
                {
                    if (fullName.PeekTag().HasSameClassAndValue(UriTag))
                    {
                        values.Add(fullName.ReadCharacterString(UniversalTagNumber.IA5String, UriTag));
                    }
                    else
                    {
                        fullName.ReadEncodedValue();
                    }
                }
            }

            AddHttpUris(result, values);
        }
        catch (AsnContentException)
        {
            // An extension that cannot be read has no address
        }

        return result;
    }

    private static void AddHttpUris(List<Uri> result, IEnumerable<string> values)
    {
        foreach (var value in values)
        {
            if (Uri.TryCreate(value, UriKind.Absolute, out var uri) && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps))
            {
                result.Add(uri);
            }
        }
    }

    private HttpClient Client => _client ?? (_ownedClient ??= new HttpClient());

    private async Task<byte[]> GetAsync(Uri uri, CancellationToken cancellationToken)
    {
        using var response = await Client.GetAsync(uri, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task<byte[]> PostOcspRequestAsync(Uri uri, byte[] request, CancellationToken cancellationToken)
    {
        using var content = new ByteArrayContent(request);
        content.Headers.ContentType = new MediaTypeHeaderValue("application/ocsp-request");
        using var response = await Client.PostAsync(uri, content, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);
    }

    internal sealed record CrlEntry(byte[] Data, byte[]? Issuer);

    internal sealed record OcspEntry(byte[] Data, bool Successful, List<OcspCertificateId> CertificateIds, List<byte[]> Certificates);

    internal sealed record OcspCertificateId(string HashAlgorithm, byte[] IssuerNameHash, byte[] SerialNumber);
}
