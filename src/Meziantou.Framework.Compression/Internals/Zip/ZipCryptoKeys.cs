using System.Runtime.InteropServices;
using System.Text;

namespace Meziantou.Framework.Compression.Internals.Zip;

/// <summary>
/// The 3 keys of the traditional PKWARE encryption (ZipCrypto, APPNOTE 6.1), initialized from the password (UTF-8), which
/// both directions update with the plain bytes.
/// </summary>
[StructLayout(LayoutKind.Auto)]
internal struct ZipCryptoKeys
{
    /// <summary>The length of the encryption header that precedes the content.</summary>
    public const int HeaderLength = 12;

    private uint _key0;
    private uint _key1;
    private uint _key2;

    public static ZipCryptoKeys Create(string password)
    {
        var keys = new ZipCryptoKeys
        {
            _key0 = 0x12345678,
            _key1 = 0x23456789,
            _key2 = 0x34567890,
        };

        foreach (var value in Encoding.UTF8.GetBytes(password))
        {
            keys.UpdateKeys(value);
        }

        return keys;
    }

    public void Decrypt(Span<byte> data)
    {
        for (var i = 0; i < data.Length; i++)
        {
            var value = (byte)(data[i] ^ KeyStreamByte());
            data[i] = value;
            UpdateKeys(value);
        }
    }

    public void Encrypt(ReadOnlySpan<byte> source, Span<byte> destination)
    {
        for (var i = 0; i < source.Length; i++)
        {
            var value = source[i];
            destination[i] = (byte)(value ^ KeyStreamByte());
            UpdateKeys(value);
        }
    }

    private readonly byte KeyStreamByte()
    {
        var temp = (_key2 | 2) & 0xFFFF;
        return (byte)(unchecked(temp * (temp ^ 1)) >> 8);
    }

    private void UpdateKeys(byte value)
    {
        _key0 = ZipCrc32.Update(_key0, value);
        _key1 = unchecked(((_key1 + (_key0 & 0xFF)) * 134775813) + 1);
        _key2 = ZipCrc32.Update(_key2, (byte)(_key1 >> 24));
    }
}
