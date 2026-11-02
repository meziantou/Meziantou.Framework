using System.Security.Cryptography.X509Certificates;

namespace Meziantou.Framework.Pdf;

/// <summary>
/// A recipient of a document encrypted by the public-key security handler (ISO 32000-2, 7.6.5): the certificate whose
/// private key opens the document, and the operations it allows.
/// </summary>
public sealed class PdfEncryptionRecipient
{
    /// <summary>Initializes a new instance of the <see cref="PdfEncryptionRecipient"/> class.</summary>
    /// <param name="certificate">The certificate of the recipient, with an RSA public key. Its private key is not needed to encrypt the document.</param>
    /// <exception cref="ArgumentException">The certificate does not have an RSA public key.</exception>
    public PdfEncryptionRecipient(X509Certificate2 certificate)
    {
        ArgumentNullException.ThrowIfNull(certificate);
        using (var key = certificate.GetRSAPublicKey())
        {
            if (key is null)
                throw new ArgumentException("The certificate of a recipient must have an RSA public key", nameof(certificate));
        }

        Certificate = certificate;
    }

    /// <summary>Gets the certificate of the recipient.</summary>
    public X509Certificate2 Certificate { get; }

    /// <summary>
    /// Gets the operations the recipient is allowed, or <see langword="null"/> for the <see cref="PdfEncryption.Permissions"/>
    /// of the encryption. The default is <see langword="null"/>.
    /// </summary>
    public PdfPermissions? Permissions { get; init; }
}
