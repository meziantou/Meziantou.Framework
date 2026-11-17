namespace Meziantou.Framework.FileConversionTool;

/// <summary>The encryption of the entries of a zip output.</summary>
internal enum ZipEncryptionMethod
{
    Aes256,
    Aes192,
    Aes128,

    /// <summary>The traditional encryption of zip (ZipCrypto): every tool reads it, but it is weak.</summary>
    ZipCrypto,
}
