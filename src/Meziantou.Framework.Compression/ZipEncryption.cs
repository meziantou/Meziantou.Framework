namespace Meziantou.Framework.Compression;

/// <summary>The encryption of the content of a zip entry.</summary>
public enum ZipEncryption
{
    /// <summary>The content is not encrypted.</summary>
    None,

    /// <summary>The traditional PKWARE encryption (ZipCrypto). It is weak: use it only to read existing archives.</summary>
    ZipCrypto,

    /// <summary>WinZip AES encryption with a 128-bit key.</summary>
    Aes128,

    /// <summary>WinZip AES encryption with a 192-bit key.</summary>
    Aes192,

    /// <summary>WinZip AES encryption with a 256-bit key.</summary>
    Aes256,

    /// <summary>An encryption that is not supported: the PKWARE strong encryption, or an unknown AES key length.</summary>
    Unsupported,
}
