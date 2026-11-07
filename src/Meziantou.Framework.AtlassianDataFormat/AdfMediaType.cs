namespace Meziantou.Framework.AtlassianDataFormat;

/// <summary>The kind of an <see cref="AdfMedia"/> item.</summary>
public enum AdfMediaType
{
    /// <summary>A media type that is not part of the supported schema.</summary>
    Unknown = 0,

    /// <summary>A file stored in the Atlassian media store. The document contains no URL for it.</summary>
    File,

    /// <summary>A link stored in the Atlassian media store. The document contains no URL for it.</summary>
    Link,

    /// <summary>A file hosted outside of Atlassian. <see cref="AdfMedia.Url"/> is set.</summary>
    External,

    /// <summary>An image, only used by <see cref="AdfMediaInline"/>.</summary>
    Image,
}
