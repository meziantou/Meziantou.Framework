namespace Meziantou.Framework.Compression;

/// <summary>The operating system that created an entry of a RAR archive, which defines the meaning of its attributes.</summary>
public enum RarHostOperatingSystem
{
    /// <summary>An operating system that the format does not define.</summary>
    Unknown,

    /// <summary>MS-DOS (RAR 1.5-4.x): the attributes are Windows attributes.</summary>
    MSDos,

    /// <summary>OS/2 (RAR 1.5-4.x): the attributes are Windows attributes.</summary>
    OS2,

    /// <summary>Windows: the attributes are Windows attributes.</summary>
    Windows,

    /// <summary>Unix: the attributes are a Unix mode (file type and permissions).</summary>
    Unix,

    /// <summary>Mac OS (RAR 1.5-4.x).</summary>
    MacOS,

    /// <summary>BeOS (RAR 1.5-4.x).</summary>
    BeOS,
}
