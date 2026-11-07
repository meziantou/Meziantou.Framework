namespace Meziantou.Framework.AtlassianDataFormat;

/// <summary>How a media item is converted.</summary>
public enum AdfMediaRendering
{
    /// <summary>An image, falling back to a link when the item is not an image and to the alternative text when no URL is known.</summary>
    Image = 0,

    /// <summary>A link, falling back to the alternative text when no URL is known.</summary>
    Link,

    /// <summary>The alternative text of the item.</summary>
    AltText,

    /// <summary>Nothing.</summary>
    Skip,
}
