using Meziantou.Framework.Compression.Internals.IO;

namespace Meziantou.Framework.Compression.Internals.Zip;

/// <summary>
/// Decrypts the traditional PKWARE encryption (ZipCrypto, APPNOTE 6.1). The 12-byte encryption header is read and checked
/// when the stream is created: its last byte must be the high byte of the CRC (or of the MS-DOS time when the entry has a
/// data descriptor). A wrong password passes this check once in 256 times; the CRC of the content catches the others.
/// </summary>
internal sealed class ZipCryptoDecryptStream : ReadOnlyStreamBase
{
    private readonly Stream _input;
    private ZipCryptoKeys _keys;

    public ZipCryptoDecryptStream(Stream input, string password, byte checkByte)
    {
        _input = input;
        _keys = ZipCryptoKeys.Create(password);

        Span<byte> header = stackalloc byte[ZipCryptoKeys.HeaderLength];
        input.ReadExactlyOrThrow(header);
        _keys.Decrypt(header);
        if (header[^1] != checkByte)
            throw new ArchivePasswordException("The password of the zip entry is wrong.");
    }

    public static int Overhead => ZipCryptoKeys.HeaderLength;

    public override int Read(Span<byte> buffer)
    {
        var read = _input.Read(buffer);
        _keys.Decrypt(buffer[..read]);
        return read;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _input.Dispose();
        }

        base.Dispose(disposing);
    }
}
