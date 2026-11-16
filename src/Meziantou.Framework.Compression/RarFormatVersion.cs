namespace Meziantou.Framework.Compression;

/// <summary>The version of the format of a RAR archive.</summary>
public enum RarFormatVersion
{
    /// <summary>The format of RAR 1.5 to 4.x (signature <c>Rar!\x1A\x07\x00</c>).</summary>
    Rar4,

    /// <summary>The format of RAR 5.0 and newer (signature <c>Rar!\x1A\x07\x01\x00</c>).</summary>
    Rar5,
}
