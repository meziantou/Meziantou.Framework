using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace Meziantou.Framework.Tests;

public sealed class X509CertificateExtensionsTests
{
    [Fact]
    public void NotBeforeUtc_And_NotAfterUtc()
    {
        using var rsa = RSA.Create(2048);
        var request = new CertificateRequest("CN=localhost", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        using var certificate = request.CreateSelfSigned(new DateTimeOffset(2025, 03, 01, 08, 00, 00, TimeSpan.Zero), new DateTimeOffset(2027, 03, 01, 08, 00, 00, TimeSpan.Zero));

        Assert.Equal(new DateTimeOffset(certificate.NotBefore.ToUniversalTime()), certificate.NotBeforeUtc);
        Assert.Equal(new DateTimeOffset(certificate.NotAfter.ToUniversalTime()), certificate.NotAfterUtc);
        Assert.NotEqual(certificate.NotBeforeUtc, certificate.NotAfterUtc);
    }
}
