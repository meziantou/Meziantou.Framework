using Meziantou.Framework.FileConversionTool.Layout;
using Meziantou.Framework.Imaging;
using Meziantou.Framework.WebPlatform.Dom;

namespace Meziantou.Framework.FileConversionTool.Mobi;

/// <summary>
/// Serves the documents of a book (the HTML of a MOBI 6 version, or the parts and flows of a KF8 version) and its resources
/// (images, fonts) to the browsing context that renders it, from the URLs of <see cref="MobiHtml.BaseUrl"/>. Every other
/// URL is a network error: a book does not load anything from the network.
/// </summary>
/// <param name="book">The book.</param>
/// <param name="html">The HTML of the MOBI 6 version, or null.</param>
/// <param name="encodingName">The encoding of <paramref name="html"/>.</param>
/// <param name="kf8">The content of the KF8 version, or null.</param>
internal sealed class MobiDocumentLoader(MobiBook book, byte[]? html, string encodingName, Kf8Content? kf8) : DocumentLoader
{
    public override ValueTask<DocumentResource?> LoadAsync(DocumentRequest request, CancellationToken cancellationToken)
    {
        return ValueTask.FromResult(Load(request.Url));
    }

    private static DocumentResource NotFound() => new("Not found") { StatusCode = 404, Headers = [new("Content-Type", "text/plain")] };

    private DocumentResource? Load(string url)
    {
        if (!url.StartsWith(MobiHtml.BaseUrl, StringComparison.Ordinal))
            return null;

        if (html is not null && MobiHtml.RemoveQueryAndFragment(url) is var path && path.SequenceEqual(MobiHtml.DocumentUrl))
            return new DocumentResource(html) { Headers = [new("Content-Type", "text/html; charset=" + encodingName)] };

        if (kf8 is not null)
        {
            if (Kf8Content.GetPartIndex(url) is { } part)
            {
                if (part >= kf8.Parts.Count)
                    return NotFound();

                var mediaType = kf8.PartsAreXml[part] ? "application/xhtml+xml" : "text/html; charset=utf-8";
                return new DocumentResource(kf8.Parts[part]) { Headers = [new("Content-Type", mediaType)] };
            }

            if (Kf8Content.GetFlowIndex(url, out var extension) is { } flow)
            {
                if (flow is 0 || flow >= kf8.Flows.Count)
                    return NotFound();

                return new DocumentResource(kf8.Flows[flow]) { Headers = [new("Content-Type", MediaTypes.FromPath("flow" + extension))] };
            }
        }

        if (MobiHtml.GetImageIndex(url) is not { } index || book.GetImage(index) is not { } data)
            return NotFound();

        if (MobiFont.IsFont(data.Span))
        {
            byte[] font;
            try
            {
                font = MobiFont.Decode(data.Span);
            }
            catch (InvalidDataException)
            {
                return NotFound();
            }

            return new DocumentResource(font) { Headers = [new("Content-Type", MobiFont.GetMediaType(font))] };
        }

        // The resource records also hold other data, which are not images
        var format = Image.DetectFormat(data.Span);
        if (format is ImageFormat.Unknown or ImageFormat.Custom)
            return NotFound();

        return new DocumentResource(data.ToArray()) { Headers = [new("Content-Type", ImageFormatInfo.Get(format).MimeType)] };
    }
}
