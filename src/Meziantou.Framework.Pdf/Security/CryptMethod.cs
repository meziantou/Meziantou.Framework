namespace Meziantou.Framework.Pdf.Security;

/// <summary>The method of a crypt filter (ISO 32000-2, 7.6.6, table 25: <c>/CFM</c>).</summary>
internal enum CryptMethod
{
    /// <summary>The data is not encrypted (<c>/None</c>, or the <c>/Identity</c> crypt filter).</summary>
    None,

    /// <summary>RC4 with a key per object (<c>/V2</c>).</summary>
    Rc4,

    /// <summary>AES-128 in CBC mode with a key per object (<c>/AESV2</c>).</summary>
    AesV2,

    /// <summary>AES-256 in CBC mode with the file key (<c>/AESV3</c>).</summary>
    AesV3,
}
