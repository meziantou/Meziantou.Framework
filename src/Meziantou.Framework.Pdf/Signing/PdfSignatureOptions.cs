using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace Meziantou.Framework.Pdf.Signing;

/// <summary>The options of a signature: the certificate, the information of the signature dictionary and the time stamp of PAdES B-T.</summary>
public sealed class PdfSignatureOptions
{
    /// <summary>Initializes a new instance of the <see cref="PdfSignatureOptions"/> class.</summary>
    /// <param name="certificate">The certificate of the signer, with its private key (RSA or ECDSA).</param>
    public PdfSignatureOptions(X509Certificate2 certificate)
    {
        ArgumentNullException.ThrowIfNull(certificate);
        Certificate = certificate;
    }

    /// <summary>Gets the certificate of the signer.</summary>
    public X509Certificate2 Certificate { get; }

    /// <summary>Gets the other certificates of the chain of the signer, which the signature embeds so that it can be validated.</summary>
    public X509Certificate2Collection Certificates { get; } = [];

    /// <summary>Gets the digest algorithm. The default is SHA-256.</summary>
    public HashAlgorithmName HashAlgorithm { get; init; } = HashAlgorithmName.SHA256;

    /// <summary>Gets the name of the signature field. The default is <c>Signature</c> followed by the first number not used.</summary>
    public string? FieldName { get; init; }

    /// <summary>Gets the name of the signer (<c>/Name</c>), when it is not the name of the certificate.</summary>
    public string? SignerName { get; init; }

    /// <summary>Gets the reason of the signature (<c>/Reason</c>), such as "I approve this document".</summary>
    public string? Reason { get; init; }

    /// <summary>Gets the place of the signature (<c>/Location</c>).</summary>
    public string? Location { get; init; }

    /// <summary>Gets the contact information of the signer (<c>/ContactInfo</c>).</summary>
    public string? ContactInfo { get; init; }

    /// <summary>Gets the time of the signature the signer claims (<c>/M</c>). The default is the current time.</summary>
    public DateTimeOffset? SigningTime { get; init; }

    /// <summary>
    /// Gets the URL of the RFC 3161 time stamp authority that stamps the signature, for PAdES B-T; <see langword="null"/>
    /// (the default) for PAdES B-B, a signature without time stamp.
    /// </summary>
    public Uri? TimestampAuthority { get; init; }

    /// <summary>Gets the client the time stamp requests are sent with; <see langword="null"/> for a client of the library.</summary>
    public HttpClient? HttpClient { get; init; }

    /// <summary>Gets the bytes reserved for the signature in the file; 0 (the default) for a size computed from the certificates and the time stamp.</summary>
    public int ReservedSize
    {
        get;
        init
        {
            ArgumentOutOfRangeException.ThrowIfNegative(value);
            field = value;
        }
    }
}
