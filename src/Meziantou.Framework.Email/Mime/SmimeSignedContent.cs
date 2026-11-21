namespace Meziantou.Framework.Email;

/// <summary>The content of a signed S/MIME entity and the verification of its signatures.</summary>
public sealed class SmimeSignedContent
{
    internal SmimeSignedContent(MimeEntity content, IReadOnlyList<SmimeSignature> signatures)
    {
        Content = content;
        Signatures = signatures;
    }

    /// <summary>Gets the signed content: the first part of a <c>multipart/signed</c>, or the entity inside an opaque signature.</summary>
    public MimeEntity Content { get; }

    /// <summary>Gets the signatures, one per signer.</summary>
    public IReadOnlyList<SmimeSignature> Signatures { get; }

    /// <summary>Gets whether the entity has at least one signature and all its signatures are valid.</summary>
    public bool IsValid => Signatures.Count > 0 && Signatures.All(signature => signature.IsValid);
}
