using System.Formats.Asn1;
using System.Net;
using System.Security.Cryptography;
using System.Security.Cryptography.Pkcs;
using System.Security.Cryptography.X509Certificates;

namespace Meziantou.Framework.Pdf.Signing.Tests;

/// <summary>An RFC 3161 time stamp authority that answers the requests of an <see cref="HttpClient"/> in memory, with a certificate of the tests.</summary>
internal sealed class TestTimestampAuthority : HttpMessageHandler
{
    public TestTimestampAuthority(DateTimeOffset time)
    {
        Time = time;
        using var key = RSA.Create(2048);
        var request = new CertificateRequest("CN=Test time stamp authority", key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        request.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension([new Oid("1.3.6.1.5.5.7.3.8")], critical: true));
        Certificate = request.CreateSelfSigned(time.AddDays(-1), time.AddYears(1));
    }

    public DateTimeOffset Time { get; }

    public X509Certificate2 Certificate { get; }

    public int RequestCount { get; private set; }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        RequestCount++;
        var body = await request.Content!.ReadAsByteArrayAsync(cancellationToken);
        if (!Rfc3161TimestampRequest.TryDecode(body, out var timestampRequest, out _))
            return new HttpResponseMessage(HttpStatusCode.BadRequest);

        // The token: the TSTInfo of the request, signed with the certificate of the authority and its ESS signing certificate
        var info = new Rfc3161TimestampTokenInfo(new Oid("1.2.3.4"), timestampRequest.HashAlgorithmId, timestampRequest.GetMessageHash(), BitConverter.GetBytes(RequestCount), Time, nonce: timestampRequest.GetNonce());
        var cms = new SignedCms(new ContentInfo(new Oid("1.2.840.113549.1.9.16.1.4"), info.Encode()));
        var signer = new CmsSigner(Certificate) { IncludeOption = X509IncludeOption.EndCertOnly };
        signer.SignedAttributes.Add(new AsnEncodedData("1.2.840.113549.1.9.16.2.47", EncodeSigningCertificateV2(Certificate)));
        cms.ComputeSignature(signer, silent: true);

        // TimeStampResp: the status granted, and the token
        var writer = new AsnWriter(AsnEncodingRules.DER);
        using (writer.PushSequence())
        {
            using (writer.PushSequence())
            {
                writer.WriteInteger(0);
            }

            writer.WriteEncodedValue(cms.Encode());
        }

        return new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(writer.Encode()) };
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            Certificate.Dispose();
        }

        base.Dispose(disposing);
    }

    private static byte[] EncodeSigningCertificateV2(X509Certificate2 certificate)
    {
        var writer = new AsnWriter(AsnEncodingRules.DER);
        using (writer.PushSequence())
        {
            using (writer.PushSequence())
            {
                using (writer.PushSequence())
                {
                    writer.WriteOctetString(SHA256.HashData(certificate.RawData));
                }
            }
        }

        return writer.Encode();
    }
}
