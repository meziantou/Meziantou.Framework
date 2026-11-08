using System.Formats.Asn1;
using System.Net;
using System.Numerics;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace Meziantou.Framework.Pdf.Tests;

/// <summary>
/// A certificate authority of the tests that answers the requests of an <see cref="HttpClient"/> in memory: a root, an
/// intermediate authority, a signer, a delegated OCSP responder (RFC 6960) and the revocation lists of both authorities.
/// </summary>
internal sealed class TestCertificateAuthority : HttpMessageHandler
{
    public static readonly Uri IntermediateUri = new("http://pki.example.com/intermediate.cer");
    public static readonly Uri OcspUri = new("http://pki.example.com/ocsp");
    public static readonly Uri RootCrlUri = new("http://pki.example.com/root.crl");
    public static readonly Uri IntermediateCrlUri = new("http://pki.example.com/intermediate.crl");

    private readonly RSA _rootKey = RSA.Create(2048);
    private readonly RSA _intermediateKey = RSA.Create(2048);
    private readonly RSA _responderKey = RSA.Create(2048);
    private readonly X509Certificate2 _rootWithKey;
    private readonly X509Certificate2 _intermediateWithKey;

    public TestCertificateAuthority()
    {
        var now = DateTimeOffset.UtcNow;
        var notBefore = now.AddDays(-1);
        var notAfter = now.AddYears(1);

        var rootRequest = new CertificateRequest("CN=Test root, O=Meziantou", _rootKey, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        AddAuthorityExtensions(rootRequest);
        _rootWithKey = rootRequest.CreateSelfSigned(notBefore, notAfter);
        Root = X509CertificateLoader.LoadCertificate(_rootWithKey.RawData);

        var intermediateRequest = new CertificateRequest("CN=Test intermediate, O=Meziantou", _intermediateKey, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        AddAuthorityExtensions(intermediateRequest);
        intermediateRequest.CertificateExtensions.Add(X509AuthorityKeyIdentifierExtension.CreateFromCertificate(_rootWithKey, includeKeyIdentifier: true, includeIssuerAndSerial: false));
        intermediateRequest.CertificateExtensions.Add(CertificateRevocationListBuilder.BuildCrlDistributionPointExtension([RootCrlUri.AbsoluteUri]));
        Intermediate = intermediateRequest.Create(_rootWithKey, notBefore, notAfter, [2]);
        _intermediateWithKey = Intermediate.CopyWithPrivateKey(_intermediateKey);

        var responderRequest = new CertificateRequest("CN=Test OCSP responder, O=Meziantou", _responderKey, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        responderRequest.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension([new Oid("1.3.6.1.5.5.7.3.9")], critical: true));
        Responder = responderRequest.Create(_intermediateWithKey, notBefore, notAfter, [3]);

        using var signerKey = RSA.Create(2048);
        var signerRequest = new CertificateRequest("CN=Test signer, O=Meziantou", signerKey, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        signerRequest.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.NonRepudiation, critical: true));
        signerRequest.CertificateExtensions.Add(X509AuthorityKeyIdentifierExtension.CreateFromCertificate(Intermediate, includeKeyIdentifier: true, includeIssuerAndSerial: false));
        signerRequest.CertificateExtensions.Add(new X509AuthorityInformationAccessExtension([OcspUri.AbsoluteUri], [IntermediateUri.AbsoluteUri]));
        signerRequest.CertificateExtensions.Add(CertificateRevocationListBuilder.BuildCrlDistributionPointExtension([IntermediateCrlUri.AbsoluteUri]));
        using var signer = signerRequest.Create(_intermediateWithKey, notBefore, notAfter, [4]);
        Signer = signer.CopyWithPrivateKey(signerKey);

        RootCrl = new CertificateRevocationListBuilder().Build(_rootWithKey, BigInteger.One, now.AddDays(7), HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1, now);
        IntermediateCrl = new CertificateRevocationListBuilder().Build(_intermediateWithKey, BigInteger.One, now.AddDays(7), HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1, now);
    }

    public X509Certificate2 Root { get; }

    public X509Certificate2 Intermediate { get; }

    public X509Certificate2 Responder { get; }

    /// <summary>Gets the signer, with its private key.</summary>
    public X509Certificate2 Signer { get; }

    public byte[] RootCrl { get; }

    public byte[] IntermediateCrl { get; }

    /// <summary>Gets or sets a value indicating whether the responder answers with an HTTP error.</summary>
    public bool FailOcsp { get; set; }

    /// <summary>Gets or sets a value indicating whether the revocation lists are answered with an HTTP error.</summary>
    public bool FailCrls { get; set; }

    /// <summary>Gets the requests, as the method and the address.</summary>
    public List<string> Requests { get; } = [];

    /// <summary>Gets the <c>CertID</c> of the last OCSP request.</summary>
    public (string HashAlgorithm, byte[] IssuerNameHash, byte[] IssuerKeyHash, byte[] SerialNumber)? LastOcspCertificateId { get; private set; }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var uri = request.RequestUri!;
        Requests.Add(request.Method.Method + " " + uri.AbsoluteUri);
        if (request.Method == HttpMethod.Post && uri == OcspUri)
        {
            if (FailOcsp)
                return new HttpResponseMessage(HttpStatusCode.InternalServerError);

            return Ok(CreateOcspResponse(await request.Content!.ReadAsByteArrayAsync(cancellationToken)));
        }

        if (request.Method == HttpMethod.Get)
        {
            if (uri == IntermediateUri)
                return Ok(Intermediate.RawData);

            if (uri == RootCrlUri || uri == IntermediateCrlUri)
                return FailCrls ? new HttpResponseMessage(HttpStatusCode.NotFound) : Ok(uri == RootCrlUri ? RootCrl : IntermediateCrl);
        }

        return new HttpResponseMessage(HttpStatusCode.NotFound);

        static HttpResponseMessage Ok(byte[] data) => new(HttpStatusCode.OK) { Content = new ByteArrayContent(data) };
    }

    /// <summary>A successful basic OCSP response (RFC 6960, 4.2.1) that the certificate of the request is good, signed by the delegated responder.</summary>
    private byte[] CreateOcspResponse(byte[] request)
    {
        // OCSPRequest > TBSRequest > requestList > Request > reqCert
        var tbsRequest = new AsnReader(request, AsnEncodingRules.DER).ReadSequence().ReadSequence();
        var certificateId = tbsRequest.ReadSequence().ReadSequence().ReadEncodedValue().ToArray();
        var reader = new AsnReader(certificateId, AsnEncodingRules.DER).ReadSequence();
        var algorithm = reader.ReadSequence().ReadObjectIdentifier();
        LastOcspCertificateId = (algorithm, reader.ReadOctetString(), reader.ReadOctetString(), reader.ReadIntegerBytes().ToArray());

        var now = DateTimeOffset.UtcNow;
        var responseData = new AsnWriter(AsnEncodingRules.DER);
        using (responseData.PushSequence())
        {
            using (responseData.PushSequence(new Asn1Tag(TagClass.ContextSpecific, 1, isConstructed: true)))
            {
                responseData.WriteEncodedValue(Responder.SubjectName.RawData);
            }

            responseData.WriteGeneralizedTime(now, omitFractionalSeconds: true);
            using (responseData.PushSequence())
            using (responseData.PushSequence())
            {
                responseData.WriteEncodedValue(certificateId);
                responseData.WriteNull(new Asn1Tag(TagClass.ContextSpecific, 0));
                responseData.WriteGeneralizedTime(now, omitFractionalSeconds: true);
            }
        }

        var tbs = responseData.Encode();
        var basic = new AsnWriter(AsnEncodingRules.DER);
        using (basic.PushSequence())
        {
            basic.WriteEncodedValue(tbs);
            using (basic.PushSequence())
            {
                basic.WriteObjectIdentifier("1.2.840.113549.1.1.11");
                basic.WriteNull();
            }

            basic.WriteBitString(_responderKey.SignData(tbs, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1));
            using (basic.PushSequence(new Asn1Tag(TagClass.ContextSpecific, 0, isConstructed: true)))
            using (basic.PushSequence())
            {
                basic.WriteEncodedValue(Responder.RawData);
            }
        }

        var response = new AsnWriter(AsnEncodingRules.DER);
        using (response.PushSequence())
        {
            response.WriteEnumeratedValue(OcspResponseStatus.Successful);
            using (response.PushSequence(new Asn1Tag(TagClass.ContextSpecific, 0, isConstructed: true)))
            using (response.PushSequence())
            {
                response.WriteObjectIdentifier("1.3.6.1.5.5.7.48.1.1");
                response.WriteOctetString(basic.Encode());
            }
        }

        return response.Encode();
    }

    private static void AddAuthorityExtensions(CertificateRequest request)
    {
        request.CertificateExtensions.Add(new X509BasicConstraintsExtension(certificateAuthority: true, hasPathLengthConstraint: false, 0, critical: true));
        request.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.KeyCertSign | X509KeyUsageFlags.CrlSign, critical: true));
        request.CertificateExtensions.Add(new X509SubjectKeyIdentifierExtension(request.PublicKey, critical: false));
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            Root.Dispose();
            Intermediate.Dispose();
            Responder.Dispose();
            Signer.Dispose();
            _rootWithKey.Dispose();
            _intermediateWithKey.Dispose();
            _rootKey.Dispose();
            _intermediateKey.Dispose();
            _responderKey.Dispose();
        }

        base.Dispose(disposing);
    }

    private enum OcspResponseStatus
    {
        Successful = 0,
    }
}
