namespace Meziantou.Framework.Pdf;

/// <summary>The algorithm of the standard security handler that encrypts the strings and the streams of a document (ISO 32000-2, 7.6).</summary>
public enum PdfEncryptionAlgorithm
{
    /// <summary>RC4 with a 40-bit key: revision 2 of the standard security handler (PDF 1.1). It is not secure: use it only for old readers.</summary>
    Rc4With40BitKey,

    /// <summary>RC4 with a 128-bit key: revision 3 (PDF 1.4). It is not secure, and PDF 2.0 deprecates it.</summary>
    Rc4With128BitKey,

    /// <summary>AES with a 128-bit key: revision 4 (PDF 1.6). PDF 2.0 deprecates it.</summary>
    AesWith128BitKey,

    /// <summary>AES with a 256-bit key: revision 6 (PDF 2.0). The default.</summary>
    AesWith256BitKey,
}
