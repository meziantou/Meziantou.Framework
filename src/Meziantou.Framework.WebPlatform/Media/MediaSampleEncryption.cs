namespace Meziantou.Framework.WebPlatform.Media;

/// <summary>
/// The protection scheme of an encrypted sample (ISO/IEC 23001-7, Common Encryption): the <c>scheme_type</c> of the
/// <c>schm</c> box of an MP4 track, and the scheme of the encrypted blocks of WebM.
/// </summary>
public enum MediaEncryptionScheme
{
    /// <summary><c>cenc</c>: AES-128 in counter mode (CTR), on the whole sample or on the protected ranges of its subsamples (WebM encryption uses it too).</summary>
    Cenc,

    /// <summary><c>cbcs</c>: AES-128 in cipher block chaining mode (CBC) with a pattern of encrypted and clear 16-byte blocks, restarted at each subsample.</summary>
    Cbcs,
}

/// <summary>A subsample of an encrypted sample: a range of clear bytes followed by a range of protected bytes.</summary>
/// <param name="ClearBytes">The number of bytes that are not encrypted (e.g. the header of a NAL unit).</param>
/// <param name="ProtectedBytes">The number of bytes that follow the clear bytes and are encrypted.</param>
[System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Auto)]
public readonly record struct MediaSubsample(int ClearBytes, int ProtectedBytes);

/// <summary>
/// How a coded frame of a media segment is encrypted (<see cref="MediaCodedFrame.Encryption"/>): the key that decrypts it,
/// the initialization vector, and the ranges of the frame that are encrypted. The library decrypts the frame with the keys
/// of the <c>MediaKeys</c> of the media element (Encrypted Media Extensions, https://w3c.github.io/encrypted-media/), and the
/// playback waits for the key when no session has it.
/// </summary>
public sealed class MediaSampleEncryption
{
    /// <summary>The length of the key IDs and of the keys of the Common Encryption schemes, in bytes.</summary>
    public const int KeyIdLength = 16;

    /// <summary>Creates the encryption of a sample.</summary>
    /// <param name="scheme">The protection scheme.</param>
    /// <param name="keyId">The ID of the key (16 bytes).</param>
    /// <param name="initializationVector">The initialization vector: 8 or 16 bytes for <see cref="MediaEncryptionScheme.Cenc"/>, 16 bytes for <see cref="MediaEncryptionScheme.Cbcs"/> (the constant IV of the track, or the IV of the sample).</param>
    /// <param name="subsamples">The subsamples of the sample, or an empty list when the whole sample is encrypted.</param>
    public MediaSampleEncryption(MediaEncryptionScheme scheme, ReadOnlyMemory<byte> keyId, ReadOnlyMemory<byte> initializationVector, IReadOnlyList<MediaSubsample>? subsamples = null)
    {
        if (keyId.Length != KeyIdLength)
            throw new ArgumentException("A key ID has 16 bytes", nameof(keyId));

        if (initializationVector.Length is not (8 or 16) || (scheme == MediaEncryptionScheme.Cbcs && initializationVector.Length != 16))
            throw new ArgumentException("The initialization vector has 8 or 16 bytes (16 bytes for cbcs)", nameof(initializationVector));

        subsamples ??= [];
        foreach (var subsample in subsamples)
        {
            ArgumentOutOfRangeException.ThrowIfNegative(subsample.ClearBytes, nameof(subsamples));
            ArgumentOutOfRangeException.ThrowIfNegative(subsample.ProtectedBytes, nameof(subsamples));
        }

        Scheme = scheme;
        KeyId = keyId.ToArray();
        InitializationVector = initializationVector.ToArray();
        Subsamples = [.. subsamples];
    }

    /// <summary>Gets the protection scheme.</summary>
    public MediaEncryptionScheme Scheme { get; }

    /// <summary>Gets the ID of the key that decrypts the sample.</summary>
    public ReadOnlyMemory<byte> KeyId { get; }

    /// <summary>Gets the initialization vector of the sample.</summary>
    public ReadOnlyMemory<byte> InitializationVector { get; }

    /// <summary>Gets the subsamples of the sample, or an empty list when the whole sample is encrypted.</summary>
    public IReadOnlyList<MediaSubsample> Subsamples { get; }

    /// <summary>
    /// Gets the number of encrypted 16-byte blocks of the pattern of <see cref="MediaEncryptionScheme.Cbcs"/> (the
    /// <c>default_crypt_byte_block</c> of the <c>tenc</c> box). With <see cref="SkipByteBlock"/>, 0 and 0 encrypt every block.
    /// </summary>
    public int CryptByteBlock { get; init; }

    /// <summary>Gets the number of clear 16-byte blocks that follow the encrypted blocks of the pattern of <see cref="MediaEncryptionScheme.Cbcs"/>.</summary>
    public int SkipByteBlock { get; init; }
}

/// <summary>Initialization Data found in the media data (https://w3c.github.io/encrypted-media/#initialization-data), reported by the backend.</summary>
/// <param name="InitDataType">The Initialization Data Type: <c>cenc</c> (the <c>pssh</c> boxes of an MP4 file), <c>webm</c> (the <c>ContentEncKeyID</c> of a WebM track) or <c>keyids</c>.</param>
/// <param name="Data">The Initialization Data.</param>
public sealed record MediaEncryptionInitData(string InitDataType, ReadOnlyMemory<byte> Data);
