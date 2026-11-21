namespace Meziantou.Framework.Email;

/// <summary>The S/MIME structure of a MIME entity ([RFC 8551]).</summary>
public enum SmimeFormat
{
    /// <summary>The entity is not signed or encrypted with S/MIME.</summary>
    None,

    /// <summary>A clear-signed entity: a <c>multipart/signed</c> whose first part is the content, readable without S/MIME, and whose second part is the signature (<c>application/pkcs7-signature</c>).</summary>
    ClearSigned,

    /// <summary>An opaque-signed entity: an <c>application/pkcs7-mime</c> with <c>smime-type=signed-data</c>, whose content is inside the signature.</summary>
    OpaqueSigned,

    /// <summary>An encrypted entity: an <c>application/pkcs7-mime</c> with <c>smime-type=enveloped-data</c>.</summary>
    Encrypted,
}
