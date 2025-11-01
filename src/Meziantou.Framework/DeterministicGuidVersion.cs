namespace Meziantou.Framework;

/// <summary>Specifies the version of the deterministic GUID generation algorithm.</summary>
[SuppressMessage("Design", "CA1008:Enums should have zero value", Justification = "<Pending>")]
public enum DeterministicGuidVersion
{
    /// <summary>UUID version 3 using MD5 hashing.</summary>
    Version3 = 3,

    /// <summary>UUID version 5 using SHA-1 hashing.</summary>
    Version5 = 5,
}
