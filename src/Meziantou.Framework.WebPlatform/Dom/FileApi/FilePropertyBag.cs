namespace Meziantou.Framework.WebPlatform.Dom.FileApi;

/// <summary>https://w3c.github.io/FileAPI/#dfn-FilePropertyBag</summary>
[IdlInterface("FilePropertyBag", "https://w3c.github.io/FileAPI/#dfn-FilePropertyBag")]
public sealed class FilePropertyBag : BlobPropertyBag
{
    /// <summary>
    /// Gets the last modification date of the file, in milliseconds since the Unix epoch, or null to use the current
    /// time.
    /// </summary>
    [IdlMember("lastModified", "https://w3c.github.io/FileAPI/#dom-filepropertybag-lastmodified")]
    public long? LastModified { get; init; }
}
