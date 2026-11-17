namespace Meziantou.Framework.Compression.Internals.SevenZip;

/// <summary>The constants of the 7z format (7zFormat.txt of the LZMA SDK).</summary>
internal static class SevenZipConstants
{
    public const int SignatureHeaderSize = 32;

    /// <summary>The maximum number of coders of a folder, and of streams of a coder (as 7-Zip).</summary>
    public const int MaxCoders = 64;

    /// <summary>The maximum number of nested encoded headers.</summary>
    public const int MaxEncodedHeaderDepth = 4;

    /// <summary>The largest NumCyclesPower of the AES key derivation that is accepted when reading (2^24 SHA-256 rounds).</summary>
    public const int MaxAesCyclesPower = 24;

    /// <summary>The NumCyclesPower of the AES key derivation that 7-Zip writes (2^19 SHA-256 rounds).</summary>
    public const int DefaultAesCyclesPower = 19;

    /// <summary>The NumCyclesPower that means that the key is the salt and the password, without hashing.</summary>
    public const int RawAesKeyCyclesPower = 0x3F;

    /// <summary>The Windows attribute bit with which 7-Zip marks the Unix mode in the high 16 bits of the attributes.</summary>
    public const uint UnixExtensionAttribute = 0x8000;

    public const uint DirectoryAttribute = 0x10;

    public const uint UnixFileTypeMask = 0xF000;
    public const uint UnixSymbolicLink = 0xA000;
    public const uint UnixDirectory = 0x4000;
    public const uint UnixRegularFile = 0x8000;

    /// <summary>The longest target of a symbolic link that is read (the content of the entry).</summary>
    public const int MaxLinkTargetLength = 32 * 1024;

    public static ReadOnlySpan<byte> Signature => [(byte)'7', (byte)'z', 0xBC, 0xAF, 0x27, 0x1C];
}
