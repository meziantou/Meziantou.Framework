namespace Meziantou.Framework.Compression;

/// <summary>The integrity check of the blocks of an XZ stream.</summary>
public enum XzCheck
{
    /// <summary>No check.</summary>
    None,

    /// <summary>CRC32 (4 bytes).</summary>
    Crc32,

    /// <summary>CRC64 (8 bytes), the default of xz.</summary>
    Crc64,

    /// <summary>SHA-256 (32 bytes).</summary>
    Sha256,
}
