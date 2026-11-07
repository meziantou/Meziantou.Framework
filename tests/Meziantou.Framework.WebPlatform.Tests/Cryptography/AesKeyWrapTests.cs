using Meziantou.Framework.WebPlatform.Cryptography;

namespace Meziantou.Framework.WebPlatform.Tests.Cryptography;

// AES Key Wrap (RFC 3394), used by the AES-KW algorithm (https://w3c.github.io/webcrypto/#aes-kw)
public sealed class AesKeyWrapTests
{
    // https://www.rfc-editor.org/rfc/rfc3394#section-4
    [Theory]
    [InlineData("000102030405060708090A0B0C0D0E0F", "00112233445566778899AABBCCDDEEFF", "1FA68B0A8112B447AEF34BD8FB5A7B829D3E862371D2CFE5")]
    [InlineData("000102030405060708090A0B0C0D0E0F1011121314151617", "00112233445566778899AABBCCDDEEFF", "96778B25AE6CA435F92B5B97C050AED2468AB8A17AD84E5D")]
    [InlineData("000102030405060708090A0B0C0D0E0F101112131415161718191A1B1C1D1E1F", "00112233445566778899AABBCCDDEEFF", "64E8C3F9CE0F5BA263E9777905818A2A93C8191E7D6E8AE7")]
    [InlineData("000102030405060708090A0B0C0D0E0F1011121314151617", "00112233445566778899AABBCCDDEEFF0001020304050607", "031D33264E15D33268F24EC260743EDCE1C6C7DDEE725A936BA814915C6762D2")]
    [InlineData("000102030405060708090A0B0C0D0E0F101112131415161718191A1B1C1D1E1F", "00112233445566778899AABBCCDDEEFF0001020304050607", "A8F9BC1612C68B3FF6E6F4FBE30E71E4769C8B80A32CB8958CD5D17D6B254DA1")]
    [InlineData("000102030405060708090A0B0C0D0E0F101112131415161718191A1B1C1D1E1F", "00112233445566778899AABBCCDDEEFF000102030405060708090A0B0C0D0E0F", "28C9F404C4B810F4CBCCB35CFB87F8263F5786E2D80ED326CBC7F0E71A99F43BFB988B9B7A02DD21")]
    public void Rfc3394(string kek, string keyData, string expected)
    {
        var kekBytes = Convert.FromHexString(kek);
        var wrapped = AesKeyWrap.Wrap(kekBytes, Convert.FromHexString(keyData));
        Assert.Equal(expected, Convert.ToHexString(wrapped));

        var unwrapped = AesKeyWrap.Unwrap(kekBytes, wrapped);
        Assert.NotNull(unwrapped);
        Assert.Equal(keyData, Convert.ToHexString(unwrapped));
    }

    [Fact]
    public void Unwrap_ModifiedInput_ReturnsNull()
    {
        var kek = Convert.FromHexString("000102030405060708090A0B0C0D0E0F");
        var wrapped = Convert.FromHexString("1FA68B0A8112B447AEF34BD8FB5A7B829D3E862371D2CFE5");
        for (var i = 0; i < wrapped.Length; i++)
        {
            var modified = wrapped.ToArray();
            modified[i] ^= 0x80;
            Assert.Null(AesKeyWrap.Unwrap(kek, modified));
        }

        // Another key-encryption key
        Assert.Null(AesKeyWrap.Unwrap(new byte[16], wrapped));
    }

    [Fact]
    public void RoundTrip_LongKeyData()
    {
        var kek = Enumerable.Range(0, 32).Select(i => (byte)(i * 3)).ToArray();
        var keyData = Enumerable.Range(0, 8 * 50).Select(i => (byte)(i * 7)).ToArray();
        var wrapped = AesKeyWrap.Wrap(kek, keyData);
        Assert.Equal(keyData.Length + 8, wrapped.Length);
        Assert.Equal(keyData, AesKeyWrap.Unwrap(kek, wrapped));
    }

    [Theory]
    [InlineData(16, 0)]
    [InlineData(16, 8)]
    [InlineData(16, 17)]
    [InlineData(16, 23)]
    [InlineData(15, 16)]
    [InlineData(20, 16)]
    public void Wrap_InvalidParameters_Throws(int kekLength, int keyDataLength)
    {
        Assert.ThrowsAny<ArgumentException>(() => AesKeyWrap.Wrap(new byte[kekLength], new byte[keyDataLength]));
    }

    [Theory]
    [InlineData(16, 0)]
    [InlineData(16, 16)]
    [InlineData(16, 25)]
    [InlineData(33, 24)]
    public void Unwrap_InvalidParameters_Throws(int kekLength, int wrappedLength)
    {
        Assert.ThrowsAny<ArgumentException>(() => AesKeyWrap.Unwrap(new byte[kekLength], new byte[wrappedLength]));
    }
}
