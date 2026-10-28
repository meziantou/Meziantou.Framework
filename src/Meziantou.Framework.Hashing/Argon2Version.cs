namespace Meziantou.Framework.Hashing;

/// <summary>The version of Argon2 (RFC 9106 section 3.1, the version number v).</summary>
public enum Argon2Version
{
    /// <summary>The version 0x10 of the reference implementation, before the XOR of the blocks of the next passes.</summary>
    Version10 = 0x10,

    /// <summary>The version 0x13 defined by RFC 9106, the one to use.</summary>
    Version13 = 0x13,
}
