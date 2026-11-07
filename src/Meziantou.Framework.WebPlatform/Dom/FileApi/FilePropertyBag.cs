namespace Meziantou.Framework.WebPlatform.Dom.FileApi;

/// <summary>https://w3c.github.io/FileAPI/#dfn-FilePropertyBag</summary>
/// <seealso href="https://w3c.github.io/FileAPI/#dfn-FilePropertyBag"/>
[IdlInterface("FilePropertyBag", "https://w3c.github.io/FileAPI/#dfn-FilePropertyBag")]
public sealed class FilePropertyBag : BlobPropertyBag
{
    /// <summary>
    /// Gets the last modification date of the file, in milliseconds since the Unix epoch, or null to use the current
    /// time.
    /// </summary>
    /// <seealso href="https://w3c.github.io/FileAPI/#dom-filepropertybag-lastmodified"/>
    [IdlMember("lastModified", "https://w3c.github.io/FileAPI/#dom-filepropertybag-lastmodified")]
    public long? LastModified { get; init; }
}
