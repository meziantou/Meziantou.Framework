namespace Meziantou.Framework.Win32;

/// <summary>Specifies a file's perceived type.</summary>
public enum PerceivedType
{
    /// <summary>The file's perceived type as defined in the registry is not a known type.</summary>
    Custom = -3,

    /// <summary>The file does not have a perceived type.</summary>
    Unspecified = -2,

    /// <summary>Not used.</summary>
    Folder = -1,

    /// <summary>Not intended to be used directly from your code.</summary>
    Unknown = 0,

    /// <summary>The file's perceived type is "text".</summary>
    Text = 1,

    /// <summary>The file's perceived type is "image".</summary>
    Image = 2,

    /// <summary>The file's perceived type is "audio".</summary>
    Audio = 3,

    /// <summary>The file's perceived type is "video".</summary>
    Video = 4,

    /// <summary>The file's perceived type is "compressed".</summary>
    Compressed = 5,

    /// <summary>The file's perceived type is "document".</summary>
    Document = 6,

    /// <summary>The file's perceived type is "system".</summary>
    System = 7,

    /// <summary>The file's perceived type is "application".</summary>
    Application = 8,

    /// <summary>The file's perceived type is "gamemedia".</summary>
    GameMedia = 9,

    /// <summary>The file's perceived type is "Contacts".</summary>
    Contacts = 10,
}
