using System.Security.Cryptography;

namespace Meziantou.Framework.WebPlatform.Media;

/// <summary>
/// Decrypts the samples of the Common Encryption schemes (ISO/IEC 23001-7): <c>cenc</c> (AES-128-CTR on the whole sample
/// or on the protected ranges of its subsamples, as one key stream) and <c>cbcs</c> (AES-128-CBC with a pattern of
/// encrypted and clear blocks, restarted with the IV at each subsample; the last partial block of a range is clear).
/// </summary>
internal static class MediaSampleDecryptor
{
    private const int BlockSize = 16;

    /// <summary>
    /// Decrypts a sample. Returns false when the encryption does not match the sample (the subsamples do not cover it
    /// exactly, an invalid pattern): the media data is corrupted.
    /// </summary>
    /// <param name="encryption">The encryption of the sample.</param>
    /// <param name="key">The AES-128 key.</param>
    /// <param name="sample">The encrypted sample.</param>
    /// <param name="destination">The decrypted sample (as long as <paramref name="sample"/>; it can be the same buffer).</param>
    public static bool TryDecrypt(MediaSampleEncryption encryption, ReadOnlySpan<byte> key, ReadOnlySpan<byte> sample, Span<byte> destination)
    {
        if (key.Length != BlockSize || destination.Length < sample.Length)
            return false;

        if (encryption.CryptByteBlock is < 0 or > 15 || encryption.SkipByteBlock is < 0 or > 15)
            return false;

        // The subsamples cover the sample exactly
        long total = 0;
        foreach (var subsample in encryption.Subsamples)
        {
            total += (long)subsample.ClearBytes + subsample.ProtectedBytes;
        }

        if (encryption.Subsamples.Count > 0 && total != sample.Length)
            return false;

        if (!sample.Overlaps(destination, out var offset) || offset != 0)
        {
            sample.CopyTo(destination);
        }

        using var aes = Aes.Create();
        aes.Key = key.ToArray();
        return encryption.Scheme switch
        {
            MediaEncryptionScheme.Cenc => DecryptCtr(aes, encryption, destination[..sample.Length]),
            MediaEncryptionScheme.Cbcs => DecryptCbcs(aes, encryption, destination[..sample.Length]),
            _ => false,
        };
    }

    private static bool DecryptCtr(Aes aes, MediaSampleEncryption encryption, Span<byte> data)
    {
        // The counter block: the IV (an 8-byte IV is followed by a zero block counter), incremented as a 128-bit number
        Span<byte> counter = stackalloc byte[BlockSize];
        counter.Clear();
        encryption.InitializationVector.Span.CopyTo(counter);
        Span<byte> keyStream = stackalloc byte[BlockSize];
        var keyStreamOffset = BlockSize;

        if (encryption.Subsamples.Count == 0)
        {
            ApplyKeyStream(aes, data, counter, keyStream, ref keyStreamOffset);
            return true;
        }

        var position = 0;
        foreach (var subsample in encryption.Subsamples)
        {
            position += subsample.ClearBytes;
            ApplyKeyStream(aes, data.Slice(position, subsample.ProtectedBytes), counter, keyStream, ref keyStreamOffset);
            position += subsample.ProtectedBytes;
        }

        return true;
    }

    private static void ApplyKeyStream(Aes aes, Span<byte> range, Span<byte> counter, Span<byte> keyStream, ref int keyStreamOffset)
    {
        for (var i = 0; i < range.Length; i++)
        {
            if (keyStreamOffset == BlockSize)
            {
                aes.EncryptEcb(counter, keyStream, PaddingMode.None);
                Increment(counter);
                keyStreamOffset = 0;
            }

            range[i] ^= keyStream[keyStreamOffset++];
        }
    }

    private static void Increment(Span<byte> counter)
    {
        for (var i = counter.Length - 1; i >= 0; i--)
        {
            if (++counter[i] != 0)
                return;
        }
    }

    private static bool DecryptCbcs(Aes aes, MediaSampleEncryption encryption, Span<byte> data)
    {
        var iv = encryption.InitializationVector.Span;
        if (iv.Length != BlockSize)
            return false;

        if (encryption.Subsamples.Count == 0)
        {
            DecryptPatternRange(aes, encryption, iv, data);
            return true;
        }

        var position = 0;
        foreach (var subsample in encryption.Subsamples)
        {
            position += subsample.ClearBytes;
            DecryptPatternRange(aes, encryption, iv, data.Slice(position, subsample.ProtectedBytes));
            position += subsample.ProtectedBytes;
        }

        return true;
    }

    /// <summary>
    /// Decrypts a protected range of <c>cbcs</c>: the encrypted blocks of the pattern form one CBC chain that starts with the
    /// IV (the clear blocks are skipped), and the bytes after the last complete block are clear. A pattern of 0:0 encrypts
    /// every complete block.
    /// </summary>
    private static void DecryptPatternRange(Aes aes, MediaSampleEncryption encryption, ReadOnlySpan<byte> iv, Span<byte> range)
    {
        var crypt = encryption.CryptByteBlock;
        var skip = encryption.SkipByteBlock;
        if (crypt == 0 && skip == 0)
        {
            crypt = 1;
        }

        Span<byte> chain = stackalloc byte[BlockSize];
        iv.CopyTo(chain);
        Span<byte> nextChain = stackalloc byte[BlockSize];
        var blocks = range.Length / BlockSize;
        var block = 0;
        while (block < blocks)
        {
            var count = Math.Min(crypt, blocks - block);
            var encrypted = range.Slice(block * BlockSize, count * BlockSize);

            // The last ciphertext block of the run is the IV of the next run of encrypted blocks
            encrypted[^BlockSize..].CopyTo(nextChain);
            aes.DecryptCbc(encrypted, chain, encrypted, PaddingMode.None);
            nextChain.CopyTo(chain);
            block += count + skip;
        }
    }
}
