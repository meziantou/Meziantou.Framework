using System.Security.Cryptography.X509Certificates;

namespace Meziantou.Framework.Email;

/// <summary>The signature of a signer of an S/MIME entity, and the result of its verification.</summary>
public sealed class SmimeSignature
{
    internal SmimeSignature(X509Certificate2? certificate, DateTimeOffset? signingTime, string? digestAlgorithm, string? error)
    {
        Certificate = certificate;
        SigningTime = signingTime;
        DigestAlgorithm = digestAlgorithm;
        Error = error;
    }

    /// <summary>Gets the certificate of the signer, or <see langword="null"/> when the signature does not include it and it is not in the store given to the verification.</summary>
    public X509Certificate2? Certificate { get; }

    /// <summary>Gets the date and time the signer says the content was signed (the signing time attribute), or <see langword="null"/>.</summary>
    public DateTimeOffset? SigningTime { get; }

    /// <summary>Gets the identifier (OID) of the digest algorithm of the signature, such as <c>2.16.840.1.101.3.4.2.1</c> for SHA-256.</summary>
    public string? DigestAlgorithm { get; }

    /// <summary>Gets whether the signature is valid: the content was not modified, and the certificate is valid when the verification checks it.</summary>
    public bool IsValid => Error is null;

    /// <summary>Gets the reason the signature is not valid, or <see langword="null"/> when it is valid.</summary>
    public string? Error { get; }
}
