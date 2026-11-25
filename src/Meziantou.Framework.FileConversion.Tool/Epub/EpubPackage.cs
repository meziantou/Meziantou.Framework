using System.IO.Compression;
using System.Xml;

namespace Meziantou.Framework.FileConversionTool.Epub;

/// <summary>
/// The package of an EPUB book: its metadata, its manifest and its reading order (spine), read from
/// <c>META-INF/container.xml</c> and the package document it names.
/// </summary>
internal sealed class EpubPackage
{
    private const string ContainerPath = "META-INF/container.xml";

    // The size of the XML documents read to analyze the book
    private const int MaxXmlLength = 16 * 1024 * 1024;

    private readonly Dictionary<string, EpubManifestItem> _manifestByPath;

    private EpubPackage(string packagePath, string? title, string? author, bool isPrePaginated, IReadOnlyList<EpubManifestItem> manifest, IReadOnlyList<EpubManifestItem> spine)
    {
        PackagePath = packagePath;
        Title = title;
        Author = author;
        IsPrePaginated = isPrePaginated;
        Manifest = manifest;
        Spine = spine;
        _manifestByPath = new Dictionary<string, EpubManifestItem>(StringComparer.Ordinal);
        foreach (var item in manifest)
        {
            _manifestByPath.TryAdd(item.Path, item);
        }
    }

    /// <summary>Gets the path of the package document in the archive.</summary>
    public string PackagePath { get; }

    public string? Title { get; }

    public string? Author { get; }

    /// <summary>Gets a value indicating whether the book has a fixed layout (<c>rendition:layout</c> is <c>pre-paginated</c>).</summary>
    public bool IsPrePaginated { get; }

    /// <summary>Gets the resources of the book whose paths stay inside the archive.</summary>
    public IReadOnlyList<EpubManifestItem> Manifest { get; }

    /// <summary>Gets the documents of the reading order, in order.</summary>
    public IReadOnlyList<EpubManifestItem> Spine { get; }

    /// <summary>Reads the package of a book.</summary>
    /// <exception cref="InvalidDataException">The archive is not an EPUB book: no container, no package document, or invalid XML.</exception>
    public static EpubPackage Read(ZipArchive archive)
    {
        if (archive.Entries.Count > ZipFiles.MaxEntryCount)
            throw new InvalidDataException($"The archive has more than {ZipFiles.MaxEntryCount.ToString(CultureInfo.InvariantCulture)} entries");

        var containerEntry = archive.GetEntry(ContainerPath) ?? throw new InvalidDataException($"The book has no '{ContainerPath}' file");
        var packagePath = ReadPackagePath(ReadXml(containerEntry)) ?? throw new InvalidDataException("The container of the book does not name a package document");
        var packageEntry = archive.GetEntry(packagePath) ?? throw new InvalidDataException($"The package document '{packagePath}' of the book does not exist");
        return ReadPackage(packagePath, ReadXml(packageEntry));
    }

    /// <summary>Gets the manifest item of a path of the archive, or null when the manifest does not list it.</summary>
    public EpubManifestItem? GetItemByPath(string path) => _manifestByPath.GetValueOrDefault(path);

    /// <summary>
    /// Resolves a reference (a relative URL, possibly percent-encoded and with a fragment) against the path of a file of the
    /// archive. Returns null when the reference is absolute (another scheme, a host, a root path) or goes above the root of the archive.
    /// </summary>
    public static string? ResolvePath(string basePath, string reference)
    {
        var href = reference.Trim();
        var end = href.IndexOfAny(['#', '?']);
        if (end >= 0)
        {
            href = href[..end];
        }

        if (href.Length is 0 || href.StartsWith('/', StringComparison.Ordinal) || href.StartsWith('\\', StringComparison.Ordinal) || href.Contains(':', StringComparison.Ordinal))
            return null;

        try
        {
            href = Uri.UnescapeDataString(href);
        }
        catch (UriFormatException)
        {
            return null;
        }

        var segments = new List<string>();
        var directoryEnd = basePath.LastIndexOf('/', StringComparison.Ordinal);
        if (directoryEnd >= 0)
        {
            segments.AddRange(basePath[..directoryEnd].Split('/'));
        }

        foreach (var segment in href.Split('/'))
        {
            switch (segment)
            {
                case "" or ".":
                    break;
                case "..":
                    if (segments.Count is 0)
                        return null;

                    segments.RemoveAt(segments.Count - 1);
                    break;
                default:
                    // A backslash or a NUL is not a valid character of a path of the archive
                    if (segment.Contains('\\', StringComparison.Ordinal) || segment.Contains('\0', StringComparison.Ordinal))
                        return null;

                    segments.Add(segment);
                    break;
            }
        }

        return segments.Count is 0 ? null : string.Join('/', segments);
    }

    /// <summary>Reads an XML entry of the archive. DTDs are ignored and no external resource is loaded.</summary>
    /// <exception cref="InvalidDataException">The entry is too large, or is not well-formed XML.</exception>
    public static XmlDocument ReadXml(ZipArchiveEntry entry)
    {
        var data = ZipFiles.ReadEntry(entry, MaxXmlLength);
        return ParseXml(data, entry.FullName);
    }

    /// <summary>Parses an XML document. DTDs are ignored and no external resource is loaded.</summary>
    /// <exception cref="InvalidDataException">The data is not well-formed XML.</exception>
    public static XmlDocument ParseXml(byte[] data, string name)
    {
        var settings = new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Ignore,
            XmlResolver = null,
            MaxCharactersInDocument = MaxXmlLength,
            MaxCharactersFromEntities = 0,
        };

        try
        {
            using var stream = new MemoryStream(data, writable: false);
            using var reader = XmlReader.Create(stream, settings);
            var document = new XmlDocument { XmlResolver = null };
            document.Load(reader);
            return document;
        }
        catch (Exception ex) when (ex is XmlException or ArgumentException)
        {
            // (ArgumentException: an XML declaration that the reader reads but XmlDocument rejects, such as a version that is not "1.n")
            throw new InvalidDataException($"The file '{name}' of the book is not valid XML: {ex.Message}", ex);
        }
    }

    private static string? ReadPackagePath(XmlDocument container)
    {
        string? first = null;
        foreach (var element in Descendants(container.DocumentElement, "rootfile"))
        {
            var fullPath = element.GetAttribute("full-path");
            if (fullPath.Length is 0)
                continue;

            if (element.GetAttribute("media-type") is "application/oebps-package+xml")
                return Normalize(fullPath);

            first ??= Normalize(fullPath);
        }

        return first;

        static string? Normalize(string path) => ResolvePath(basePath: "", path);
    }

    private static EpubPackage ReadPackage(string packagePath, XmlDocument package)
    {
        var root = package.DocumentElement ?? throw new InvalidDataException("The package document of the book is empty");

        string? title = null;
        string? author = null;
        var isPrePaginated = false;
        foreach (var metadata in Children(root, "metadata"))
        {
            title ??= NullIfEmpty(Children(metadata, "title").FirstOrDefault()?.InnerText);
            author ??= NullIfEmpty(Children(metadata, "creator").FirstOrDefault()?.InnerText);
            foreach (var meta in Children(metadata, "meta"))
            {
                if (meta.GetAttribute("property") is "rendition:layout" && meta.InnerText.Trim() is "pre-paginated")
                {
                    isPrePaginated = true;
                }
            }
        }

        var manifest = new List<EpubManifestItem>();
        var manifestById = new Dictionary<string, EpubManifestItem>(StringComparer.Ordinal);
        foreach (var manifestElement in Children(root, "manifest"))
        {
            foreach (var itemElement in Children(manifestElement, "item"))
            {
                var id = itemElement.GetAttribute("id");
                var path = ResolvePath(packagePath, itemElement.GetAttribute("href"));
                if (path is null)
                    continue;

                var item = new EpubManifestItem(id, path, itemElement.GetAttribute("media-type").Trim(), itemElement.GetAttribute("properties"));
                manifest.Add(item);
                if (id.Length > 0)
                {
                    manifestById.TryAdd(id, item);
                }
            }
        }

        var spine = new List<EpubManifestItem>();
        foreach (var spineElement in Children(root, "spine"))
        {
            foreach (var itemRef in Children(spineElement, "itemref"))
            {
                if (manifestById.TryGetValue(itemRef.GetAttribute("idref"), out var item))
                {
                    spine.Add(item);
                }
            }
        }

        if (spine.Count is 0)
            throw new InvalidDataException("The reading order (spine) of the book is empty");

        return new EpubPackage(packagePath, title, author, isPrePaginated, manifest, spine);

        static string? NullIfEmpty(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    // The elements are matched by local name: EPUB 2 and EPUB 3 use different namespaces, and some books use none
    private static IEnumerable<XmlElement> Children(XmlElement parent, string localName)
    {
        foreach (var node in parent.ChildNodes)
        {
            if (node is XmlElement element && element.LocalName == localName)
                yield return element;
        }
    }

    internal static IEnumerable<XmlElement> Descendants(XmlElement? root, string localName)
    {
        if (root is null)
            yield break;

        foreach (var node in root.GetElementsByTagName("*"))
        {
            if (node is XmlElement element && element.LocalName == localName)
                yield return element;
        }
    }
}
