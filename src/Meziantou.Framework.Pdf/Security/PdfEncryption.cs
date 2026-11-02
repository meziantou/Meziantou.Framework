using Meziantou.Framework.Pdf.Security;

namespace Meziantou.Framework.Pdf;

/// <summary>
/// The encryption of a document: by the standard security handler (ISO 32000-2, 7.6.4), with passwords, or by the
/// public-key security handler (7.6.5), for the certificates of recipients. Set <see cref="PdfDocument.Encryption"/> to
/// encrypt a document when it is saved.
/// </summary>
/// <remarks>
/// The user password opens the document with the permissions; the owner password opens it with every permission. An
/// empty user password opens the document without a prompt: the permissions are then the only protection, which the
/// readers enforce. A recipient opens the document with the private key of its certificate, with its permissions. A
/// document that is read from an encrypted file keeps its encryption: its <see cref="PdfDocument.Encryption"/> has no
/// passwords and no recipients, and the document is saved with the keys of the file.
/// </remarks>
public sealed class PdfEncryption
{
    /// <summary>Initializes a new instance of the <see cref="PdfEncryption"/> class that encrypts a document with passwords.</summary>
    /// <param name="userPassword">The user password; empty to open the document without a password.</param>
    /// <param name="ownerPassword">The owner password; empty for the user password.</param>
    public PdfEncryption(string userPassword, string ownerPassword)
    {
        ArgumentNullException.ThrowIfNull(userPassword);
        ArgumentNullException.ThrowIfNull(ownerPassword);
        UserPassword = userPassword;
        OwnerPassword = ownerPassword;
        Recipients = [];
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="PdfEncryption"/> class that encrypts a document for the certificates of
    /// recipients (the public-key security handler, <c>adbe.pkcs7.s5</c>): each opens the document with its private key.
    /// </summary>
    /// <param name="recipients">The recipients; at least one.</param>
    /// <exception cref="ArgumentException">There is no recipient, or a recipient is <see langword="null"/>.</exception>
    public PdfEncryption(params IEnumerable<PdfEncryptionRecipient> recipients)
    {
        ArgumentNullException.ThrowIfNull(recipients);
        PdfEncryptionRecipient[] list = [.. recipients];
        if (list.Length == 0)
            throw new ArgumentException("The encryption needs at least one recipient", nameof(recipients));

        if (Array.IndexOf(list, null) >= 0)
            throw new ArgumentException("A recipient is null", nameof(recipients));

        Recipients = list;
        UsesCertificates = true;
    }

    internal PdfEncryption(SecurityHandler handler)
    {
        Handler = handler;
        Algorithm = handler.Algorithm;
        Permissions = handler.Permissions;
        EncryptMetadata = handler.EncryptMetadata;
        UsesCertificates = handler.IsPublicKey;
        Recipients = [];
    }

    /// <summary>Gets the user password, or <see langword="null"/> for the encryption of a document read from a file or encrypted for certificates.</summary>
    public string? UserPassword { get; }

    /// <summary>Gets the owner password, or <see langword="null"/> for the encryption of a document read from a file or encrypted for certificates.</summary>
    public string? OwnerPassword { get; }

    /// <summary>Gets the recipients the document is encrypted for: empty for passwords, and for the encryption of a document read from a file.</summary>
    public IReadOnlyList<PdfEncryptionRecipient> Recipients { get; }

    /// <summary>Gets a value indicating whether the document is encrypted for certificates (the public-key security handler) instead of passwords.</summary>
    public bool UsesCertificates { get; }

    /// <summary>Gets the algorithm. The default is <see cref="PdfEncryptionAlgorithm.AesWith256BitKey"/>, which needs PDF 2.0: the version of the file is raised to what the algorithm needs.</summary>
    public PdfEncryptionAlgorithm Algorithm { get; init; } = PdfEncryptionAlgorithm.AesWith256BitKey;

    /// <summary>
    /// Gets the operations the user password allows, or the recipients whose <see cref="PdfEncryptionRecipient.Permissions"/>
    /// is <see langword="null"/>. The default is <see cref="PdfPermissions.All"/>.
    /// </summary>
    public PdfPermissions Permissions { get; init; } = PdfPermissions.All;

    /// <summary>
    /// Gets a value indicating whether the XMP metadata stream is encrypted. The default is <see langword="true"/>.
    /// The RC4 algorithms of the standard security handler always encrypt it.
    /// </summary>
    public bool EncryptMetadata { get; init; } = true;

    /// <summary>Gets the security handler of the file the document was read from, whose keys encrypt the document when it is saved.</summary>
    internal SecurityHandler? Handler { get; }
}
