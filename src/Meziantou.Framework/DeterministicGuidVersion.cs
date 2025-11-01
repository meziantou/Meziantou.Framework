namespace Meziantou.Framework;

/// <summary>
/// Specifies the version of the deterministic GUID algorithm to use as defined in RFC 4122.
/// </summary>
[SuppressMessage("Design", "CA1008:Enums should have zero value", Justification = "<Pending>")]
public enum DeterministicGuidVersion
{
    /// <summary>
    /// Version 3 UUID using MD5 hashing algorithm.
    /// </summary>
    Version3 = 3,

    /// <summary>
    /// Version 5 UUID using SHA-1 hashing algorithm (recommended over Version3).
    /// </summary>
    Version5 = 5,
}
