namespace Meziantou.Framework.Compression.Internals.Rar;

/// <summary>The encryption parameters of an entry or of the headers.</summary>
internal sealed class RarEncryption
{
    public bool IsRar5 { get; init; }

    public byte[] Salt { get; init; } = [];

    public int KdfCount { get; init; }

    public byte[] Iv { get; init; } = [];

    /// <summary>Gets the password check value of RAR5 (8 bytes), or <see langword="null"/>.</summary>
    public byte[]? PasswordCheck { get; init; }

    /// <summary>Gets a value indicating whether the CRC and the hash of the entry are converted with the hash key (RAR5).</summary>
    public bool UseMac { get; init; }
}
