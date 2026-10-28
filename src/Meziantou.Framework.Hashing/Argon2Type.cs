namespace Meziantou.Framework.Hashing;

/// <summary>The variant of Argon2 (RFC 9106 section 3.1, the type y).</summary>
public enum Argon2Type
{
    /// <summary>Argon2d: data-dependent memory access (y = 0).</summary>
    Argon2d = 0,

    /// <summary>Argon2i: data-independent memory access (y = 1).</summary>
    Argon2i = 1,

    /// <summary>Argon2id: data-independent memory access for the first half of the first pass, data-dependent afterwards (y = 2).</summary>
    Argon2id = 2,
}
