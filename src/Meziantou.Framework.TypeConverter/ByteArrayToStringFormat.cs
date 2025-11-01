namespace Meziantou.Framework;

/// <summary>Specifies the format to use when converting byte arrays to strings.</summary>
public enum ByteArrayToStringFormat
{
    /// <summary>No conversion format specified.</summary>
    None,

    /// <summary>Hexadecimal format without prefix (e.g., "01020304").</summary>
    Base16,

    /// <summary>Hexadecimal format with "0x" prefix (e.g., "0x01020304").</summary>
    Base16Prefixed,

    /// <summary>Base64 encoding format.</summary>
    Base64,
}
