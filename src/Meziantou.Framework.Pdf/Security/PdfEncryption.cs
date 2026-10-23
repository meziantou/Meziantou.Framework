using Meziantou.Framework.Pdf.Security;

namespace Meziantou.Framework.Pdf;

/// <summary>
/// The encryption of a document by the standard security handler (ISO 32000-2, 7.6.4): its passwords, its algorithm and
/// the permissions of the user password. Set <see cref="PdfDocument.Encryption"/> to encrypt a document when it is saved.
/// </summary>
/// <remarks>
/// The user password opens the document with the permissions; the owner password opens it with every permission. An
/// empty user password opens the document without a prompt: the permissions are then the only protection, which the
/// readers enforce. A document that is read from an encrypted file keeps its encryption: its
/// <see cref="PdfDocument.Encryption"/> has no passwords, and the document is saved with the keys of the file.
/// </remarks>
public sealed class PdfEncryption
{
    /// <summary>Initializes a new instance of the <see cref="PdfEncryption"/> class.</summary>
    /// <param name="userPassword">The user password; empty to open the document without a password.</param>
    /// <param name="ownerPassword">The owner password; empty for the user password.</param>
    public PdfEncryption(string userPassword, string ownerPassword)
    {
        ArgumentNullException.ThrowIfNull(userPassword);
        ArgumentNullException.ThrowIfNull(ownerPassword);
        UserPassword = userPassword;
        OwnerPassword = ownerPassword;
    }

    internal PdfEncryption(StandardSecurityHandler handler)
    {
        Handler = handler;
        Algorithm = handler.Algorithm;
        Permissions = handler.Permissions;
        EncryptMetadata = handler.EncryptMetadata;
    }

    /// <summary>Gets the user password, or <see langword="null"/> for the encryption of a document read from a file.</summary>
    public string? UserPassword { get; }

    /// <summary>Gets the owner password, or <see langword="null"/> for the encryption of a document read from a file.</summary>
    public string? OwnerPassword { get; }

    /// <summary>Gets the algorithm. The default is <see cref="PdfEncryptionAlgorithm.AesWith256BitKey"/>, which needs PDF 2.0: the version of the file is raised to what the algorithm needs.</summary>
    public PdfEncryptionAlgorithm Algorithm { get; init; } = PdfEncryptionAlgorithm.AesWith256BitKey;

    /// <summary>Gets the operations the user password allows. The default is <see cref="PdfPermissions.All"/>.</summary>
    public PdfPermissions Permissions { get; init; } = PdfPermissions.All;

    /// <summary>
    /// Gets a value indicating whether the XMP metadata stream is encrypted. The default is <see langword="true"/>.
    /// The RC4 algorithms always encrypt it.
    /// </summary>
    public bool EncryptMetadata { get; init; } = true;

    /// <summary>Gets the security handler of the file the document was read from, whose keys encrypt the document when it is saved.</summary>
    internal StandardSecurityHandler? Handler { get; }
}
