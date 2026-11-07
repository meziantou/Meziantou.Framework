namespace Meziantou.Framework.WebPlatform.Dom.FileApi;

/// <summary>https://w3c.github.io/FileAPI/#dfn-BlobPropertyBag</summary>
/// <seealso href="https://w3c.github.io/FileAPI/#dfn-BlobPropertyBag"/>
[IdlInterface("BlobPropertyBag", "https://w3c.github.io/FileAPI/#dfn-BlobPropertyBag")]
public class BlobPropertyBag
{
    /// <summary>Gets the media type of the blob.</summary>
    /// <seealso href="https://w3c.github.io/FileAPI/#dom-blobpropertybag-type"/>
    [IdlMember("type", "https://w3c.github.io/FileAPI/#dom-blobpropertybag-type")]
    public string Type { get; init; } = "";

    /// <summary>
    /// Gets how the line endings of the string parts are converted (https://w3c.github.io/FileAPI/#dom-blobpropertybag-endings):
    /// <c>transparent</c> (unchanged) or <c>native</c> (converted to the native line ending, LF on every host: the documents do not know the operating system).
    /// </summary>
    /// <seealso href="https://w3c.github.io/FileAPI/#dom-blobpropertybag-endings"/>
    [IdlMember("endings", "https://w3c.github.io/FileAPI/#dom-blobpropertybag-endings")]
    public string Endings { get; init; } = "transparent";
}
