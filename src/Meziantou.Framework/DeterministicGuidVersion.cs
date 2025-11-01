namespace Meziantou.Framework;

/// <summary>
/// Specifies the version of the deterministic GUID generation algorithm.
/// </summary>
[SuppressMessage("Design", "CA1008:Enums should have zero value", Justification = "<Pending>")]
public enum DeterministicGuidVersion
{
    /// <summary>
    /// Version 3 uses MD5 hashing.
    /// </summary>
    Version3 = 3,

    /// <summary>
    /// Version 5 uses SHA-1 hashing (preferred).
    /// </summary>
    Version5 = 5,
}
