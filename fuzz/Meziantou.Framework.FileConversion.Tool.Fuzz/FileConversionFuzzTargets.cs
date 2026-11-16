using Meziantou.Framework.Compression;
using Meziantou.Framework.FileConversionTool.Archives;
using Meziantou.Framework.FileConversionTool.Epub;
using Meziantou.Framework.FileConversionTool.Inputs;
using Meziantou.Framework.FileConversionTool.Outputs;
using Meziantou.Framework.FileConversionTool.Tests;
using Meziantou.Framework.Fuzzing;
using Meziantou.Framework.Imaging;
using ZipArchive = System.IO.Compression.ZipArchive;
using ZipArchiveMode = System.IO.Compression.ZipArchiveMode;

namespace Meziantou.Framework.FileConversionTool.Fuzz;

/// <summary>
/// The inputs the tool reads besides the image, PDF and archive files (fuzzed in their own projects): the output name
/// templates, the page ranges, the PDF permissions, the conversions between Markdown and HTML, the URLs of the local files of HTML documents, the EPUB packages, the pages of the comic book archives and the copy of the entries of an
/// archive to another format.
/// </summary>
public static class FileConversionFuzzTargets
{
    private static readonly string[] TemplateTokens = ["{name}", "{ext}", "{dir}", "{page}", "{index}", "{count}", "{page:000}", "{index:0}", "{{", "}}", "{", "}", ":", "/", "."];

    private static readonly string[] PageRangeTokens = ["1", "10", "-", ",", " ", "0", "2147483647"];

    private static readonly string[] PdfPermissionTokens = ["None", "Print", "PrintHighQuality", "Modify", "CopyContent", "Annotate", "FillForms", "ExtractForAccessibility", "Assemble", "All", ",", " ", "-", "4"];

    private static readonly string[] MarkdownTokens = ["---\n", "title:", "\"", "'", "# ", "=====\n", "-----\n", "| a | b |\n|---|---|\n", "- [x] ", "*", "_", "`", "```\n", "<", ">", "&", "<script>", "[link](", ")", "\n", "\r\n", "> "];

    private static readonly string[] HtmlTokens = ["<!DOCTYPE html>", "<html>", "<head>", "<title>", "</title>", "<body>", "</body>", "<h1>", "<p>", "<b>", "<ul>", "<li>", "<table>", "<tr>", "<td>", "<pre>", "<code>", "&amp;", "&lt;", "\uFEFF"];

    private static readonly string[] EpubTokens =
    [
        "PK\u0003\u0004", "PK\u0001\u0002", "PK\u0005\u0006", "mimetype", "application/epub+zip", "META-INF/container.xml",
        "<rootfile", "full-path=", "media-type=", "application/oebps-package+xml", "<package", "<metadata", "<manifest", "<spine",
        "<item", "<itemref", " idref=", " href=", " id=", "rendition:layout", "pre-paginated", "<dc:title>", "<dc:creator>",
        "<img", " src=", "<svg", "<image", "xlink:href=", "<body>", "</body>", "<!DOCTYPE", "../", "%2e%2e/", "#", "image/png",
    ];

    private static readonly string[] ArchiveTokens = ["PK\u0003\u0004", "PK\u0001\u0002", "PK\u0005\u0006", "7z\u00bc\u00af\u0027\u001c", "Rar!\u001a\u0007\u0001\u0000", "Rar!\u001a\u0007\u0000", "ustar", "ustar\u000000", "\u001f\u008b", "BZh9", "\u00fd7zXZ\u0000", "LZIP", ".png", ".jpg", "/", "__MACOSX/", "ComicInfo.xml"];

    public static IReadOnlyList<FuzzTarget> All { get; } =
    [
        new("file-conversion-output-template", OutputTemplate) { Seeds = GetTemplateSeeds, Dictionary = TemplateTokens },
        new("file-conversion-page-range", PageRanges) { Seeds = GetPageRangeSeeds, Dictionary = PageRangeTokens },
        new("file-conversion-pdf-permissions", PdfPermissionsTarget) { Seeds = GetPdfPermissionsSeeds, Dictionary = PdfPermissionTokens },
        new("file-conversion-markdown-to-html", MarkdownToHtml) { Seeds = GetMarkdownSeeds, Dictionary = MarkdownTokens },
        new("file-conversion-html-to-markdown", HtmlToMarkdown) { Seeds = GetHtmlSeeds, Dictionary = HtmlTokens },
        new("file-conversion-local-file-url", LocalFileUrl) { Seeds = GetLocalFileUrlSeeds, Dictionary = ["https://local.invalid/", "/", "..", ".", "%2e", "%2F", "%5C", "\\", ":", "C:", "?", "#", "%00", "%"] },
        new("file-conversion-natural-order", NaturalOrder) { Seeds = GetNaturalOrderSeeds },
        new("file-conversion-epub-path", EpubPath) { Seeds = GetEpubPathSeeds, Dictionary = ["/", "..", ".", "%2F", "%2e", "#", "?", ":", "\\"] },
        new("file-conversion-epub-package", EpubPackageTarget) { Seeds = GetArchiveSeeds, Dictionary = EpubTokens },
        new("file-conversion-comic", Comic) { Seeds = GetArchiveSeeds, Dictionary = ArchiveTokens },
        new("file-conversion-archive-copy", ArchiveCopy) { Seeds = GetArchiveSeeds, Dictionary = ArchiveTokens },
        new("file-conversion-detect-format", DetectFormat) { Seeds = GetArchiveSeeds, Dictionary = [.. ArchiveTokens, "%PDF-", "mimetypeapplication/epub+zip", "\u0089PNG", "GIF89a", "RIFF", "WEBP"] },
    ];

    private static void OutputTemplate(ReadOnlySpan<byte> data)
    {
        var text = FuzzInput.ReadText(data);
        OutputNameTemplate template;
        try
        {
            template = OutputNameTemplate.Parse(text);
        }
        catch (FormatException)
        {
            return;
        }

        var first = template.Format(new OutputNameValues("name", "png", "/dir", Page: 1, PageCount: 12, Index: 1, IndexCount: null));
        var second = template.Format(new OutputNameValues("name", "png", "/dir", Page: 2, PageCount: 12, Index: 2, IndexCount: null));
        FuzzAssert.Equal(first, template.Format(new OutputNameValues("name", "png", "/dir", Page: 1, PageCount: 12, Index: 1, IndexCount: null)), "The format of a template is not deterministic");
        if (template.HasPageNumber)
        {
            FuzzAssert.False(first == second, "A template with {page} or {index} gives the same name to two pages");
        }

        if (!text.Contains("{{", StringComparison.Ordinal) && !text.Contains("}}", StringComparison.Ordinal))
        {
            FuzzAssert.False(first.Contains('{', StringComparison.Ordinal) || first.Contains('}', StringComparison.Ordinal), "A placeholder is left in the formatted name");
        }
    }

    private static void PageRanges(ReadOnlySpan<byte> data)
    {
        var text = FuzzInput.ReadText(data);
        PageRange range;
        try
        {
            range = PageRange.Parse(text);
        }
        catch (FormatException)
        {
            return;
        }

        FuzzAssert.False(range.Contains(0), "A page range contains the page 0");
        var previous = 0;
        for (var pageCount = 1; pageCount <= 64; pageCount++)
        {
            var count = range.Count(pageCount);
            FuzzAssert.True(count >= previous && count <= pageCount && count - previous == (range.Contains(pageCount) ? 1 : 0), "The number of pages of a range does not match the pages it contains");
            previous = count;
        }
    }

    private static void PdfPermissionsTarget(ReadOnlySpan<byte> data)
    {
        var text = FuzzInput.ReadText(data);
        Pdf.PdfPermissions permissions;
        try
        {
            permissions = Program.ParsePermissions(text);
        }
        catch (FormatException)
        {
            return;
        }

        FuzzAssert.Equal(Pdf.PdfPermissions.None, permissions & ~Pdf.PdfPermissions.All, "The permissions have bits that are not permissions");
        FuzzAssert.Equal(permissions, Program.ParsePermissions(permissions.ToString()), "The names of the parsed permissions do not parse to the same permissions");
    }

    private static void MarkdownToHtml(ReadOnlySpan<byte> data)
    {
        var markdown = FuzzInput.ReadText(data);
        string html;
        try
        {
            html = TextDocumentConverter.MarkdownToHtml(markdown, "default <title>", new ConversionSettings(), fragment: false).Html;
        }
        catch (ArgumentException)
        {
            // A document nested too deeply
            return;
        }

        FuzzAssert.True(html.StartsWith("<!DOCTYPE html>\n<html>\n<head>\n", StringComparison.Ordinal), "The HTML document does not start with its head");
        FuzzAssert.True(html.EndsWith("</body>\n</html>\n", StringComparison.Ordinal), "The HTML document does not end with its body");

        // The title is escaped: the head has no element the Markdown document added
        var titleStart = html.IndexOf("<title>", StringComparison.Ordinal) + "<title>".Length;
        var titleEnd = html.IndexOf("</title>", titleStart, StringComparison.Ordinal);
        FuzzAssert.True(titleStart > "<title>".Length && titleEnd >= titleStart, "The HTML document has no title");
        var title = html[titleStart..titleEnd];
        FuzzAssert.False(title.Contains('<', StringComparison.Ordinal) || title.Contains('>', StringComparison.Ordinal) || title.Contains('\n', StringComparison.Ordinal), "The title of the HTML document is not escaped");
        FuzzAssert.True(html.IndexOf("</head>\n<body>\n", StringComparison.Ordinal) > titleEnd, "The head of the HTML document has more than its metadata");

        var fragment = TextDocumentConverter.MarkdownToHtml(markdown, "title", new ConversionSettings(), fragment: true).Html;
        FuzzAssert.True(html.Contains("<body>\n" + fragment, StringComparison.Ordinal), "The body of the HTML document is not the HTML fragment");
    }

    private static void LocalFileUrl(ReadOnlySpan<byte> data)
    {
        var url = FuzzInput.ReadText(data);
        var root = FullPath.FromPath(OperatingSystem.IsWindows() ? @"C:\root\site" : "/root/site");
        if (!Layout.LocalFileDocumentLoader.TryResolvePath(root, url, out var path))
            return;

        FuzzAssert.True(path.IsChildOf(root), "The URL resolves to a file outside of the root folder");
        FuzzAssert.True(Layout.LocalFileDocumentLoader.TryResolvePath(root, url, out var again) && again == path, "The resolution of a URL is not deterministic");
    }

    private static void HtmlToMarkdown(ReadOnlySpan<byte> data)
    {
        var html = FuzzInput.ReadText(data);
        string markdown;
        try
        {
            markdown = TextDocumentConverter.HtmlToMarkdown(html, new ConversionSettings());
        }
        catch (ArgumentException)
        {
            // A document nested too deeply
            return;
        }

        FuzzAssert.True(markdown.Length is 0 || markdown[^1] is '\n', "The Markdown document does not end with a new line");
    }

    private static void NaturalOrder(ReadOnlySpan<byte> data)
    {
        var parts = FuzzInput.ReadText(data).Split('\n');
        if (parts.Length < 3)
            return;

        var comparer = NaturalStringComparer.Instance;
        var (a, b, c) = (parts[0], parts[1], parts[2]);
        FuzzAssert.Equal(0, comparer.Compare(a, a), "A string is not equal to itself");
        FuzzAssert.Equal(Math.Sign(comparer.Compare(a, b)), -Math.Sign(comparer.Compare(b, a)), "The comparison is not antisymmetric");
        FuzzAssert.Equal(a == b, comparer.Compare(a, b) == 0, "Different strings are equal");
        if (comparer.Compare(a, b) <= 0 && comparer.Compare(b, c) <= 0)
        {
            FuzzAssert.True(comparer.Compare(a, c) <= 0, "The comparison is not transitive");
        }
    }

    // The paths of the documents of a book: the paths of its archive, which are normalized
    private static readonly string[] EpubBasePaths = ["", "content.opf", "OEBPS/content.opf", "OEBPS/Text/chapter.xhtml", "a/b/c/d.xhtml"];

    private static void EpubPath(ReadOnlySpan<byte> data)
    {
        // The first byte selects the path of the document; the rest is the reference, which is arbitrary
        if (data.IsEmpty)
            return;

        var basePath = EpubBasePaths[data[0] % EpubBasePaths.Length];
        var resolved = EpubPackage.ResolvePath(basePath, FuzzInput.ReadText(data[1..]));
        if (resolved is null)
            return;

        foreach (var segment in resolved.Split('/'))
        {
            FuzzAssert.False(segment is "" or "." or "..", "A resolved path has an empty, '.' or '..' segment");
            FuzzAssert.False(segment.Contains('\\', StringComparison.Ordinal) || segment.Contains('\0', StringComparison.Ordinal), "A resolved path has a backslash or a NUL");
        }

        // A resolved path that contains no character to escape resolves to itself
        if (resolved.AsSpan().IndexOfAny("%#?:") < 0)
        {
            FuzzAssert.Equal(resolved, EpubPackage.ResolvePath(basePath: "", resolved), "Resolving a resolved path changes it");
        }
    }

    private static void EpubPackageTarget(ReadOnlySpan<byte> data)
    {
        using var archive = OpenArchive(data);
        if (archive is null)
            return;

        EpubPackage package;
        try
        {
            package = EpubPackage.Read(archive);
        }
        catch (InvalidDataException)
        {
            return;
        }

        foreach (var item in package.Manifest)
        {
            FuzzAssert.Equal(item.Path, EpubPackage.ResolvePath(basePath: "", string.Join('/', item.Path.Split('/').Select(Uri.EscapeDataString))), "A path of the manifest is not normalized");
        }

        foreach (var item in package.Spine)
        {
            FuzzAssert.True(package.Manifest.Contains(item), "An item of the spine is not in the manifest");
        }

        IReadOnlyList<string>? images;
        try
        {
            images = EpubImagePages.Find(archive, package);
        }
        catch (InvalidDataException)
        {
            return;
        }

        if (images is not null)
        {
            FuzzAssert.Equal(package.Spine.Count, images.Count, "The images of a book are not one per document of the reading order");
            foreach (var image in images)
            {
                FuzzAssert.True(package.Spine.Any(item => item.Path == image) || archive.GetEntry(image) is not null, "An image of a book is not in the archive");
            }
        }
    }

    private static void Comic(ReadOnlySpan<byte> data)
    {
        using var archive = OpenAnyArchive(data);
        if (archive is null)
            return;

        List<ArchiveEntry> entries;
        try
        {
            entries = [.. ArchiveInputDocument.GetPageEntries(archive)];
        }
        catch (InvalidDataException)
        {
            return;
        }

        for (var i = 1; i < entries.Count; i++)
        {
            FuzzAssert.True(NaturalStringComparer.Instance.Compare(entries[i - 1].FullName, entries[i].FullName) <= 0, "The pages of an archive are not sorted");
        }

        foreach (var entry in entries)
        {
            FuzzAssert.False(entry.IsDirectory || entry.LinkTarget is not null, "A page of an archive is a directory or a link");
            FuzzAssert.True(FileFormat.FromExtension(Path.GetExtension(entry.Name)) is { Kind: FileFormatKind.Image }, "A page of an archive is not an image file");
        }
    }

    private static void ArchiveCopy(ReadOnlySpan<byte> data)
    {
        using var archive = OpenAnyArchive(data);
        if (archive is null || archive.Entries.Count > 1000)
            return;

        // The content of the files, read once: the copy must give the same content
        var files = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        try
        {
            foreach (var entry in archive.Entries)
            {
                if (!entry.IsDirectory && entry.LinkTarget is null && entry.HardLinkTarget is null)
                {
                    files[entry.FullName] = ArchiveFiles.ReadEntry(entry, maxLength: 1024 * 1024);
                }
            }
        }
        catch (Exception ex) when (ex is InvalidDataException or IOException or NotSupportedException)
        {
            return;
        }

        foreach (var archiveType in new[] { ArchiveType.Zip, ArchiveType.Tar })
        {
            using var output = new MemoryStream();
            try
            {
                using var writer = ArchiveWriter.Create(output, archiveType, new ConversionSettings(), storeByDefault: true);
                ArchiveOutputWriter.Copy(archive, writer, _ => { }, CancellationToken.None);
            }
            catch (Exception ex) when (ex is InvalidDataException or IOException or NotSupportedException or ArgumentException or InvalidOperationException)
            {
                // Names the output format cannot store (empty, too long, duplicated...), or content that is not valid
                return;
            }

            output.Position = 0;
            using var copy = Compression.ArchiveFile.Open(output);
            foreach (var (name, content) in files)
            {
                var copied = copy.GetEntry(name.TrimEnd('/'));
                FuzzAssert.True(copied is not null, "A file of the archive is not in the copy");
                FuzzAssert.True(content.AsSpan().SequenceEqual(ArchiveFiles.ReadEntry(copied)), "The content of a file of the archive differs in the copy");
            }
        }
    }

    private static void DetectFormat(ReadOnlySpan<byte> data)
    {
        using var stream = new MemoryStream(data.ToArray(), writable: false);
        var format = Converter.DetectFormat(stream, "input.bin");
        if (format is { Kind: FileFormatKind.Image })
        {
            FuzzAssert.Equal(format.ImageFormat, Image.DetectFormat(data), "The detected format is not the image format of the content");
        }

        if (format is { Kind: FileFormatKind.Archive })
        {
            FuzzAssert.Equal(format, FileFormat.FromArchiveType(format.ArchiveType, comic: false), "The detected archive format is not the format of its container");
        }

        // The extension of a comic book archive makes an archive a comic book archive, whatever its container
        stream.Position = 0;
        var comicFormat = Converter.DetectFormat(stream, "input.cbz");
        if (format is { Kind: FileFormatKind.Archive } && comicFormat is { Kind: FileFormatKind.Comic })
        {
            FuzzAssert.Equal(format.ArchiveType, comicFormat.ArchiveType, "The container of a comic book archive is not the one of the archive");
        }
    }

    private static Archive? OpenAnyArchive(ReadOnlySpan<byte> data)
    {
        try
        {
            return Compression.ArchiveFile.Open(new MemoryStream(data.ToArray(), writable: false));
        }
        catch (Exception ex) when (ex is InvalidDataException or IOException or NotSupportedException)
        {
            // The documented errors of an archive that is not valid, encrypted or uses a method that is not supported
            return null;
        }
    }

    private static ZipArchive? OpenArchive(ReadOnlySpan<byte> data)
    {
        try
        {
            return new ZipArchive(new MemoryStream(data.ToArray(), writable: false), ZipArchiveMode.Read);
        }
        catch (InvalidDataException)
        {
            return null;
        }
    }

    private static IEnumerable<FuzzSeed> GetTemplateSeeds()
    {
        string[] templates = ["{name}-{page}.{ext}", "{name}-{page:0000}.{ext}", "{index:0}_{count}", "{{literal}}-{name}", "{dir}/x", "out/page-{page:000}-of-{count}.png", "{dir}/converted/{name}.{ext}"];
        for (var i = 0; i < templates.Length; i++)
        {
            yield return FuzzSeed.FromText($"template-{i}", templates[i]);
        }
    }

    private static IEnumerable<FuzzSeed> GetPdfPermissionsSeeds()
    {
        string[] values = ["All", "none", "print,PrintHighQuality", "Print, CopyContent", "Print,4", ""];
        for (var i = 0; i < values.Length; i++)
        {
            yield return FuzzSeed.FromText($"permissions-{i}", values[i]);
        }
    }

    private static IEnumerable<FuzzSeed> GetMarkdownSeeds()
    {
        string[] documents =
        [
            "---\ntitle: \"Front & title\"\n---\n# Heading\n\n| a | b |\n|---|---|\n| 1 | 2 |\n\n- [x] done\n",
            "# Tom & *Jerry*\n\nText",
            "Title\n=====\n\nText",
            "Text only",
            "<script>alert(1)</script>\n\n| a | b |\n|---|---|\n| 1 | 2 |\n",
            "# <b>bold</b> heading\n",
            "",
        ];
        for (var i = 0; i < documents.Length; i++)
        {
            yield return FuzzSeed.FromText($"markdown-{i}", documents[i]);
        }
    }

    private static IEnumerable<FuzzSeed> GetLocalFileUrlSeeds()
    {
        string[] urls =
        [
            "https://local.invalid/a/b.png", "https://local.invalid/a/./b.css?v=1#top", "https://local.invalid/a/../b%20c.png",
            "https://local.invalid/../secret.txt", "https://local.invalid/a/../../secret.txt", "https://local.invalid/%2e%2e/secret.txt",
            "https://local.invalid/a%2F..%2F..%2Fsecret.txt", "https://local.invalid/C:%5Csecret.txt", "https://local.invalid/",
            "https://example.com/a.png", "file:///etc/passwd",
        ];
        for (var i = 0; i < urls.Length; i++)
        {
            yield return FuzzSeed.FromText($"url-{i}", urls[i]);
        }
    }

    private static IEnumerable<FuzzSeed> GetHtmlSeeds()
    {
        string[] documents =
        [
            "<!DOCTYPE html><html><head><title>Head title</title></head><body><h1>Hello</h1><p>Some <b>bold</b> text</p><ul><li>item</li></ul></body></html>",
            "<p>Fragment</p>",
            "<table><tr><td>a</td><td>b</td></tr></table>",
            "",
        ];
        for (var i = 0; i < documents.Length; i++)
        {
            yield return FuzzSeed.FromText($"html-{i}", documents[i]);
        }
    }

    private static IEnumerable<FuzzSeed> GetPageRangeSeeds()
    {
        string[] ranges = ["1-3,7,10-", "5", "2-3,5", "1-2", "3-1", "1,,2"];
        for (var i = 0; i < ranges.Length; i++)
        {
            yield return FuzzSeed.FromText($"range-{i}", ranges[i]);
        }
    }

    private static IEnumerable<FuzzSeed> GetNaturalOrderSeeds()
    {
        yield return FuzzSeed.FromText("pages", "page10.jpg\nPage2.jpg\npage01.jpg");
        yield return FuzzSeed.FromText("numbers", "001\n1\n01");
    }

    private static IEnumerable<FuzzSeed> GetEpubPathSeeds()
    {
        yield return FuzzSeed.FromText("relative", "\u0002text/ch1.xhtml");
        yield return FuzzSeed.FromText("parent", "\u0003../images/a%20b.png#frag");
        yield return FuzzSeed.FromText("escape", "\u0002../../escape.xhtml");
        yield return FuzzSeed.FromText("absolute", "\u0002https://example.com/a.png");
    }

    private static IEnumerable<FuzzSeed> GetArchiveSeeds()
    {
        yield return new FuzzSeed("image-epub", TestFiles.CreateImageEpub());
        yield return new FuzzSeed("text-epub", TestFiles.CreateTextEpub());
        yield return new FuzzSeed("cbz", TestFiles.CreateZip(("2.png", TestFiles.CreateImage(2, 2, ImageFormat.Png)), ("10.jpg", TestFiles.CreateImage(2, 2, ImageFormat.Jpeg)), ("__MACOSX/1.png", [1]), ("notes.txt", [1])));
        yield return new FuzzSeed("cb7", TestFiles.CreateSevenZip(("dir/", []), ("dir/1.png", TestFiles.CreateImage(2, 2, ImageFormat.Png)), ("a.txt", "A"u8.ToArray())));
        yield return new FuzzSeed("cbr", TestFiles.CreateRar(("10.png", TestFiles.CreateImage(2, 2, ImageFormat.Png)), ("2.jpg", TestFiles.CreateImage(2, 2, ImageFormat.Jpeg)), ("ComicInfo.xml", "<ComicInfo/>"u8.ToArray())));
        yield return new FuzzSeed("tar", TestFiles.CreateTar(gzip: false, ("dir/", []), ("dir/1.png", TestFiles.CreateImage(2, 2, ImageFormat.Png)), ("link -> dir/1.png", [])));
        yield return new FuzzSeed("tar-gz", TestFiles.CreateTar(gzip: true, ("a.txt", "A"u8.ToArray()), ("b/c.txt", "C"u8.ToArray())));
        yield return new FuzzSeed("png", TestFiles.CreateImage(1, 1, ImageFormat.Png));
        yield return new FuzzSeed("pdf", TestFiles.CreatePdf(1));
    }
}
