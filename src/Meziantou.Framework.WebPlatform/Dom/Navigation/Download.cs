namespace Meziantou.Framework.WebPlatform.Dom;

/// <summary>
/// A response handled as a download (https://html.spec.whatwg.org/multipage/links.html#downloading-resources), see
/// <see cref="BrowsingContextOptions.DownloadStarted"/>: the response of a navigation whose <c>Content-Disposition</c> is
/// <c>attachment</c>, or the resource of a hyperlink with a <c>download</c> attribute.
/// </summary>
public sealed class Download
{
    internal Download(string url, string? filename, DocumentResource resource)
    {
        Url = url;
        Filename = filename;
        Resource = resource;
    }

    /// <summary>Gets the URL of the response (after the redirects).</summary>
    public string Url { get; }

    /// <summary>
    /// Gets the value of the <c>download</c> attribute of the hyperlink (possibly empty), or null for the response of a
    /// navigation. The <c>Content-Disposition</c> header of <see cref="Resource"/> may suggest another filename.
    /// </summary>
    public string? Filename { get; }

    /// <summary>
    /// Gets the response. The rest of a body that arrives over time (<see cref="DocumentResource.RemainingContentBytes"/>) is
    /// not read by the library: the callback that saves the download reads it, or disposes its enumerator to cancel it.
    /// </summary>
    public DocumentResource Resource { get; }
}
