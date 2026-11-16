using System.IO.Compression;
using System.Xml;

namespace Meziantou.Framework.FileConversionTool.Epub;

/// <summary>Finds the images of a book whose pages are images (comics, manga, fixed-layout books of scanned pages).</summary>
internal static class EpubImagePages
{
    private const string XLinkNamespace = "http://www.w3.org/1999/xlink";

    /// <summary>
    /// Gets the path of the image of each document of the reading order, or null when a document is not a page made of one
    /// image: a document with text, several images or no image.
    /// </summary>
    public static IReadOnlyList<string>? Find(ZipArchive archive, EpubPackage package)
    {
        var result = new List<string>(package.Spine.Count);
        foreach (var item in package.Spine)
        {
            if (item.IsImage)
            {
                result.Add(item.Path);
                continue;
            }

            // An SVG page is a document: the book is laid out
            if (EpubManifestItem.IsSvg(item.MediaType, item.Path))
                return null;

            var entry = archive.GetEntry(item.Path);
            if (entry is null)
                return null;

            XmlDocument document;
            try
            {
                document = EpubPackage.ReadXml(entry);
            }
            catch (InvalidDataException)
            {
                return null;
            }

            var image = FindSingleImage(document);
            if (image is null)
                return null;

            // An SVG image cannot be copied as an encoded image: the book is laid out
            var path = EpubPackage.ResolvePath(item.Path, image);
            if (path is null || archive.GetEntry(path) is null || EpubManifestItem.IsSvg(package.GetItemByPath(path)?.MediaType, path))
                return null;

            result.Add(path);
        }

        return result;
    }

    /// <summary>Gets the reference of the only image of a document that has no text, or null.</summary>
    internal static string? FindSingleImage(XmlDocument document)
    {
        var root = document.DocumentElement;
        if (root is null)
            return null;

        // An SVG document is the page itself; an XHTML document has its content in its body
        var content = root.LocalName is "svg" ? root : EpubPackage.Descendants(root, "body").FirstOrDefault();
        if (content is null)
            return null;

        string? image = null;
        var imageCount = 0;
        foreach (var node in Descendants(content))
        {
            switch (node)
            {
                case XmlElement { LocalName: "img" } element:
                    imageCount++;
                    image = element.GetAttribute("src");
                    break;
                case XmlElement { LocalName: "image" } element:
                    imageCount++;
                    image = element.GetAttribute("href", XLinkNamespace) is { Length: > 0 } xlinkHref ? xlinkHref : element.GetAttribute("href");
                    break;
                case XmlText or XmlCDataSection when !string.IsNullOrWhiteSpace(node.Value) && !IsInsideNonRenderedElement(node, content):
                    return null;
            }

            if (imageCount > 1)
                return null;
        }

        return string.IsNullOrWhiteSpace(image) ? null : image;
    }

    private static bool IsInsideNonRenderedElement(XmlNode node, XmlElement content)
    {
        for (var parent = node.ParentNode; parent is not null && parent != content; parent = parent.ParentNode)
        {
            if (parent.LocalName is "title" or "desc" or "style" or "script")
                return true;
        }

        return false;
    }

    private static IEnumerable<XmlNode> Descendants(XmlNode root)
    {
        var stack = new Stack<XmlNode>();
        stack.Push(root);
        while (stack.Count > 0)
        {
            var node = stack.Pop();
            if (node != root)
                yield return node;

            for (var child = node.LastChild; child is not null; child = child.PreviousSibling)
            {
                stack.Push(child);
            }
        }
    }
}
