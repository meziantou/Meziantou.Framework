namespace Meziantou.Framework.Email.Storage;

/// <summary>How the server uses a property of an offline address book online (<c>OAB_PROP_REC</c>, [MS-OXOAB] 2.9.4).</summary>
[Flags]
#pragma warning disable CA1711 // The name of the type is the name of the concept
public enum OabPropertyFlags
#pragma warning restore CA1711
{
    /// <summary>No flag.</summary>
    None = 0,

    /// <summary>The property is part of the ambiguous name resolution (ANR) index.</summary>
    AnrIndex = 0x1,

    /// <summary>The property is a primary key: every record has a value.</summary>
    PrimaryKey = 0x2,
}
