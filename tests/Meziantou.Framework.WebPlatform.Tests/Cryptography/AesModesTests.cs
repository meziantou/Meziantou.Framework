using System.Security.Cryptography;
using Meziantou.Framework.WebPlatform.Cryptography;

namespace Meziantou.Framework.WebPlatform.Tests.Cryptography;

// The AES modes of the Web Cryptography API: CBC, CTR and GCM (any IV length, short tags)
public sealed class AesModesTests
{
    // The GCM test cases 2 to 4 and 6 of "The Galois/Counter Mode of Operation (GCM)" (McGrew and Viega), appendix B
    [Theory]
    [InlineData("00000000000000000000000000000000", "000000000000000000000000", "", "00000000000000000000000000000000", "0388dace60b6a392f328c2b971b2fe78", "ab6e47d42cec13bdf53a67b21257bddf")]
    [InlineData("feffe9928665731c6d6a8f9467308308", "cafebabefacedbaddecaf888", "", "d9313225f88406e5a55909c5aff5269a86a7a9531534f7da2e4c303d8a318a721c3c0c95956809532fcf0e2449a6b525b16aedf5aa0de657ba637b391aafd255",
        "42831ec2217774244b7221b784d0d49ce3aa212f2c02a4e035c17e2329aca12e21d514b25466931c7d8f6a5aac84aa051ba30b396a0aac973d58e091473f5985", "4d5c2af327cd64a62cf35abd2ba6fab4")]
    [InlineData("feffe9928665731c6d6a8f9467308308", "cafebabefacedbaddecaf888", "feedfacedeadbeeffeedfacedeadbeefabaddad2", "d9313225f88406e5a55909c5aff5269a86a7a9531534f7da2e4c303d8a318a721c3c0c95956809532fcf0e2449a6b525b16aedf5aa0de657ba637b39",
        "42831ec2217774244b7221b784d0d49ce3aa212f2c02a4e035c17e2329aca12e21d514b25466931c7d8f6a5aac84aa051ba30b396a0aac973d58e091", "5bc94fbc3221a5db94fae95ae7121a47")]
    [InlineData("feffe9928665731c6d6a8f9467308308", "9313225df88406e555909c5aff5269aa6a7a9538534f7da1e4c303d2a318a728c3c0c95156809539fcf0e2429a6b525416aedbf5a0de6a57a637b39b", "feedfacedeadbeeffeedfacedeadbeefabaddad2", "d9313225f88406e5a55909c5aff5269a86a7a9531534f7da2e4c303d8a318a721c3c0c95956809532fcf0e2449a6b525b16aedf5aa0de657ba637b39",
        "8ce24998625615b603a033aca13fb894be9112a5c3a211a8ba262a3cca7e2ca701e4a9a4fba43c90ccdcb281d48c7c6fd62875d2aca417034c34aee5", "619cc5aefffe0bfa462af43c1699d050")]
    public void Gcm_TestVectors(string key, string iv, string additionalData, string plaintext, string ciphertext, string tag)
    {
        var output = AesModes.GcmEncrypt(Convert.FromHexString(key), Convert.FromHexString(iv), Convert.FromHexString(additionalData), 16, Convert.FromHexString(plaintext));
        Assert.Equal(ciphertext + tag, Convert.ToHexStringLower(output));
        Assert.Equal(Convert.FromHexString(plaintext), AesModes.GcmDecrypt(Convert.FromHexString(key), Convert.FromHexString(iv), Convert.FromHexString(additionalData), 16, output));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(16)]
    [InlineData(100)]
    public void Gcm_MatchesBaseClassLibrary(int length)
    {
        var key = RandomNumberGenerator.GetBytes(32);
        var nonce = RandomNumberGenerator.GetBytes(12);
        var additionalData = RandomNumberGenerator.GetBytes(20);
        var plaintext = RandomNumberGenerator.GetBytes(length);
        var expected = new byte[length];
        var expectedTag = new byte[16];
        using (var gcm = new AesGcm(key, 16))
        {
            gcm.Encrypt(nonce, plaintext, expected, expectedTag, additionalData);
        }

        var output = AesModes.GcmEncrypt(key, nonce, additionalData, 16, plaintext);
        Assert.Equal([.. expected, .. expectedTag], output);
    }

    [Fact]
    public void Gcm_ShortTagAndTampering()
    {
        var key = RandomNumberGenerator.GetBytes(16);
        var iv = RandomNumberGenerator.GetBytes(256);
        var output = AesModes.GcmEncrypt(key, iv, [], 4, [1, 2, 3]);
        Assert.Equal(7, output.Length);
        Assert.Equal([1, 2, 3], AesModes.GcmDecrypt(key, iv, [], 4, output));

        output[0] ^= 1;
        Assert.Null(AesModes.GcmDecrypt(key, iv, [], 4, output));
        Assert.Null(AesModes.GcmDecrypt(key, iv, [], 4, [1, 2]));
    }

    // NIST SP 800-38A F.5.1 (CTR-AES128.Encrypt)
    [Fact]
    public void Ctr_TestVector()
    {
        var output = AesModes.Ctr(Convert.FromHexString("2b7e151628aed2a6abf7158809cf4f3c"), Convert.FromHexString("f0f1f2f3f4f5f6f7f8f9fafbfcfdfeff"), 128, Convert.FromHexString("6bc1bee22e409f96e93d7e117393172aae2d8a571e03ac9c9eb76fac45af8e51"));
        Assert.Equal("874d6191b620e3261bef6864990db6ce9806f66b7970fdff8617187bb9fffdff", Convert.ToHexStringLower(output));
    }

    [Fact]
    public void Ctr_CounterWrapsWithinItsLength()
    {
        // With a 4-bit counter, the block after counter 0xF is counter 0x0 and the other bits are unchanged
        var key = RandomNumberGenerator.GetBytes(16);
        var counter = Convert.FromHexString("000102030405060708090a0b0c0d0e1f");
        var keyStream = AesModes.Ctr(key, counter, 4, new byte[32]);
        var wrapped = AesModes.Ctr(key, Convert.FromHexString("000102030405060708090a0b0c0d0e10"), 4, new byte[16]);
        Assert.Equal(wrapped, keyStream.AsSpan(16).ToArray());
    }

    [Fact]
    public void Cbc_RoundTripAndInvalidPadding()
    {
        var key = RandomNumberGenerator.GetBytes(16);
        var iv = RandomNumberGenerator.GetBytes(16);
        var ciphertext = AesModes.CbcEncrypt(key, iv, [1, 2, 3]);
        Assert.Equal(16, ciphertext.Length);
        Assert.Equal([1, 2, 3], AesModes.CbcDecrypt(key, iv, ciphertext));

        // A block whose last byte is not a valid padding
        using var aes = Aes.Create();
        aes.Key = key;
        var invalid = aes.EncryptCbc(new byte[16], iv, PaddingMode.None);
        Assert.Null(AesModes.CbcDecrypt(key, iv, invalid));
    }
}
