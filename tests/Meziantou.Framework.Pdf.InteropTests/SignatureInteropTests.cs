using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text.RegularExpressions;
using Meziantou.Framework.Pdf.Signing;
using Meziantou.Framework.Pdf.TestHarness.ExternalTools;

namespace Meziantou.Framework.Pdf.InteropTests;

/// <summary>Poppler's pdfsig checks the signatures the library writes. The pinned release has no pdfsig for Windows: the tests are skipped there.</summary>
public sealed partial class SignatureInteropTests
{
    [Theory]
    [InlineData("rsa")]
    [InlineData("ecdsa")]
    [InlineData("encrypted")]
    [InlineData("two-signatures")]
    public async Task SignaturesAreValidForPdfsig(string name)
    {
        global::Xunit.Assert.SkipUnless(PdfSigTool.IsSupported, "The release of meziantou/prebuilt has no pdfsig for this platform");
        var pdfsig = await PdfSigTool.LocateAsync(XunitCancellationToken);
        using var certificate = CreateCertificate(ecdsa: name == "ecdsa");

        var document = new PdfDocument();
        document.Pages.Add(PdfSize.A4).CreateContentBuilder().DrawText("Signed", PdfFont.GetStandardFont(PdfStandardFont.Helvetica), 24, 60, 700);
        if (name == "encrypted")
        {
            document.Encryption = new PdfEncryption("", "owner");
        }

        var signed = PdfSigner.Sign(document, new PdfSignatureOptions(certificate) { Reason = "Interop" });
        var expectedSignatures = 1;
        if (name == "two-signatures")
        {
            signed = PdfSigner.Sign(PdfDocument.Open(signed), new PdfSignatureOptions(certificate) { Reason = "Second" });
            expectedSignatures = 2;
        }

        var path = Path.Combine(AppContext.BaseDirectory, "InteropOutput", "signed-" + name + ".pdf");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllBytesAsync(path, signed, XunitCancellationToken);

        var report = await pdfsig.VerifyAsync(path, XunitCancellationToken);

        Assert.HasCount(expectedSignatures, ValidSignatureRegex().Matches(report));
        Assert.Contains("Total document signed", report, StringComparison.Ordinal);
        Assert.Contains("ETSI.CAdES.detached", report, StringComparison.Ordinal);
    }

    private static X509Certificate2 CreateCertificate(bool ecdsa)
    {
        var now = DateTimeOffset.UtcNow;
        if (ecdsa)
        {
            using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
            return new CertificateRequest("CN=ECDSA signer", key, HashAlgorithmName.SHA256).CreateSelfSigned(now.AddDays(-1), now.AddYears(1));
        }

        using var rsa = RSA.Create(2048);
        return new CertificateRequest("CN=RSA signer", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1).CreateSelfSigned(now.AddDays(-1), now.AddYears(1));
    }

    [GeneratedRegex("Signature Validation: Signature is Valid", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 1000)]
    private static partial Regex ValidSignatureRegex();
}
