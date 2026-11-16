using System.Text;
using Meziantou.Framework.Compression.Internals.IO;

namespace Meziantou.Framework.Compression.Internals.Zip;

/// <summary>
/// Decrypts the traditional PKWARE encryption (ZipCrypto, APPNOTE 6.1). The 12-byte encryption header is read and checked
/// when the stream is created: its last byte must be the high byte of the CRC (or of the MS-DOS time when the entry has a
/// data descriptor). A wrong password passes this check once in 256 times; the CRC of the content catches the others.
/// </summary>
internal sealed class ZipCryptoDecryptStream : ReadOnlyStreamBase
{
    private const int HeaderLength = 12;

    private readonly Stream _input;
    private uint _key0 = 0x12345678;
    private uint _key1 = 0x23456789;
    private uint _key2 = 0x34567890;

    public ZipCryptoDecryptStream(Stream input, string password, byte checkByte)
    {
        _input = input;
        foreach (var value in Encoding.UTF8.GetBytes(password))
        {
            UpdateKeys(value);
        }

        Span<byte> header = stackalloc byte[HeaderLength];
        input.ReadExactlyOrThrow(header);
        Decrypt(header);
        if (header[^1] != checkByte)
            throw new ArchivePasswordException("The password of the zip entry is wrong.");
    }

    public static int Overhead => HeaderLength;

    public override int Read(Span<byte> buffer)
    {
        var read = _input.Read(buffer);
        Decrypt(buffer[..read]);
        return read;
    }

    private void Decrypt(Span<byte> data)
    {
        for (var i = 0; i < data.Length; i++)
        {
            var temp = (_key2 | 2) & 0xFFFF;
            var value = (byte)(data[i] ^ (byte)(unchecked(temp * (temp ^ 1)) >> 8));
            data[i] = value;
            UpdateKeys(value);
        }
    }

    private void UpdateKeys(byte value)
    {
        _key0 = ZipCrc32.Update(_key0, value);
        _key1 = unchecked(((_key1 + (_key0 & 0xFF)) * 134775813) + 1);
        _key2 = ZipCrc32.Update(_key2, (byte)(_key1 >> 24));
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
