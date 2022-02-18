namespace Meziantou.Framework.Win32;

/// <summary>
/// Indicates the source of the perceived type information.
/// </summary>
[Flags]
public enum PerceivedTypeSource
{
    /// <summary>
    /// No perceived type was found.
    /// </summary>
    Undefined = 0x0000,

    /// <summary>
    /// The perceived type was determined through an association in the registry.
    /// </summary>
    SoftCoded = 0x0001,

    /// <summary>
    /// The perceived type is inherently known to the operating system.
    /// </summary>
    HardCoded = 0x0002,

    /// <summary>
    /// The perceived type was determined through a codec provided with the operating system.
    /// </summary>
    NativeSupport = 0x0004,

    /// <summary>
    /// The perceived type is supported by the Windows GDI+ library.
    /// </summary>
    GdiPlus = 0x0010,

    /// <summary>
    /// The perceived type is supported by the Microsoft Windows Media software development kit (SDK).
    /// </summary>
    WmSdk = 0x0020,

    /// <summary>
    /// The perceived type is supported by Windows compressed folders.
    /// </summary>
    ZipFolder = 0x0040,

    /// <summary>
    /// The perceived type was determined through MIME content types in the registry.
    /// </summary>
    Mime = 0x0080,
}
