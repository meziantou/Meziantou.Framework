using System.Net;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace Meziantou.Framework.PostgreSql.Tests;

/// <summary>A self-signed loopback certificate written to a temporary directory.</summary>
internal sealed class TestCertificate : IDisposable
{
    private readonly string _directoryPath;

    private TestCertificate(string directoryPath, string pfxPath, string pfxPassword)
    {
        _directoryPath = directoryPath;
        PfxPath = pfxPath;
        PfxPassword = pfxPassword;
    }

    public string PfxPath { get; }

    public string PfxPassword { get; }

    public static TestCertificate Create()
    {
        var directoryPath = Path.Combine(AppContext.BaseDirectory, "certificates", Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture));
        Directory.CreateDirectory(directoryPath);

        using var rsa = RSA.Create(2048);
        var request = new CertificateRequest("CN=localhost", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        request.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension([new Oid("1.3.6.1.5.5.7.3.1")], critical: false));
        var sanBuilder = new SubjectAlternativeNameBuilder();
        sanBuilder.AddIpAddress(IPAddress.Loopback);
        request.CertificateExtensions.Add(sanBuilder.Build());

        using var certificate = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-1), DateTimeOffset.UtcNow.AddHours(1));
        const string Password = "Password123!";

        var pfxPath = Path.Combine(directoryPath, "server.pfx");
        File.WriteAllBytes(pfxPath, certificate.Export(X509ContentType.Pfx, Password));

        return new TestCertificate(directoryPath, pfxPath, Password);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_directoryPath, recursive: true);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
