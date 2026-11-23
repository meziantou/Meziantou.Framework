using Meziantou.Framework.Compression;
using Meziantou.Framework.Email;
using Meziantou.Framework.Email.Mapi;
using Meziantou.Framework.Email.Storage;
using Meziantou.Framework.FileConversionTool.Archives;
using Meziantou.Framework.FileConversionTool.Epub;
using Meziantou.Framework.FileConversionTool.Inputs;
using Meziantou.Framework.FileConversionTool.Mobi;
using Meziantou.Framework.FileConversionTool.Outputs;
using Meziantou.Framework.FileConversionTool.Tests;
using Meziantou.Framework.Fuzzing;
using Meziantou.Framework.Imaging;
using ZipArchive = System.IO.Compression.ZipArchive;
using ZipArchiveMode = System.IO.Compression.ZipArchiveMode;

namespace Meziantou.Framework.FileConversionTool.Fuzz;

/// <summary>
/// The inputs the tool reads besides the image, PDF and archive files (fuzzed in their own projects): the output name
/// templates, the page ranges, the PDF permissions, the conversions between Markdown and HTML, the URLs of the local files of HTML documents, the EPUB packages, the MOBI and KF8 books and their fonts, the pages of the comic book archives and the copy of the entries of an
/// archive to another format, the detection of the email formats and the conversions between the email message formats.
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

    private static readonly string[] MobiHtmlTokens =
    [
        "<mbp:pagebreak/>", "</mbp:pagebreak>", "<MBP:PAGEBREAK />", "<img", " recindex=", " hirecindex=", " lorecindex=", "\"00001\"", "'2'",
        " filepos=", "0000000000", "\"0000000123\"", "<a", "</a>", "<head>", "<guide>", "<reference", "<body>", "<body ", "</body>", ">", "<",
        "é", "Ã©", "â\u0082¬", "\u0080", "¿", "\u0000",
    ];

    private static readonly string[] MobiTokens =
    [
        "BOOKMOBI", "MOBI", "EXTH", "HUFF\u0000\u0000\u0000\u0018", "CDIC\u0000\u0000\u0000\u0010", "\u0000\u0001", "\u0000\u0002", "DH",
        "\u0000\u0000\u0000è", "\u0000\u0000ýé", "\u0000\u0000\u0004ä", "\u0000\u0000\u0000\u0006", "\u0000\u0000\u0000\u0008",
        "\u0000\u0000\u0000d", "\u0000\u0000\u0000É", "\u0000\u0000\u0001÷", "\u0000\u0000\u0000P", "ÿÿÿÿ",
        "\u0000\u0003", "\u0080", "\u0083", .. MobiHtmlTokens,
    ];

    private static readonly string[] Kf8Tokens =
    [
        "kindle:embed:", "kindle:flow:", "kindle:pos:fid:", ":off:", "?mime=text/css", "?mime=image/png", "?mime=image/svg+xml", "0001", "0002", "0000000000",
        "FDST", "INDX", "TAGX", "IDXT", "SKEL0000000000", "BOUNDARY", "FONT", "\u0000\u0000\u0000\u0008", "\u0000\u0000\u0000y", "\u0000\u0000\u0000À",
        "\u0001\u0001\u0003\u0000", "\u0006\u0002\u000c\u0000", "\u0000\u0000\u0000\u0001", "\u0081", "\u0085", "\u008f", " id=\"", "<body", "<img src=\"", "<svg", "<?xml",
        .. MobiTokens,
    ];

    private static readonly string[] EmailTokens =
    [
        "From: ", "To: ", "Subject: ", "Date: ", "MIME-Version: 1.0", "Content-Type: multipart/mixed; boundary=b", "Content-Type: text/plain",
        "Content-Transfer-Encoding: base64", "--b", "--b--", "\r\n", "\n", "\r\n\r\n", "From alice@example.com Thu Jan 15 09:00:00 2026\n", "!BDN", "SM", "SO",
        "\x78\x9F\x3E\x22", "\xD0\xCF\x11\xE0\xA1\xB1\x1A\xE1", "__substg1.0_0037001F", "__properties_version1.0", "123       \n", "<?xml", "<plist",
        "<key>flags</key>",
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
        new("file-conversion-mobi", MobiBookTarget) { Seeds = GetMobiSeeds, Dictionary = MobiTokens },
        new("file-conversion-kf8", MobiBookTarget) { Seeds = GetKf8Seeds, Dictionary = Kf8Tokens },
        new("file-conversion-mobi-font", MobiFontTarget) { Seeds = GetMobiFontSeeds, Dictionary = ["FONT", "OTTO", "wOFF", "wOF2", "\u0000\u0001\u0000\u0000", "x\u009c"] },
        new("file-conversion-mobi-html", MobiHtmlTarget) { Seeds = GetMobiHtmlSeeds, Dictionary = MobiHtmlTokens },
        new("file-conversion-palmdoc", PalmDoc) { Seeds = GetMobiHtmlSeeds, Dictionary = MobiHtmlTokens },
        new("file-conversion-detect-format", DetectFormat) { Seeds = GetDetectFormatSeeds, Dictionary = [.. ArchiveTokens, .. EmailTokens, "%PDF-", "mimetypeapplication/epub+zip", "\u0089PNG", "GIF89a", "RIFF", "WEBP", "BOOKMOBI"] },
        new("file-conversion-email-message", EmailMessage) { Seeds = GetEmailSeeds, Dictionary = EmailTokens },
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

        // The constructs nested too deeply are text: the conversion does not fail
        var html = TextDocumentConverter.MarkdownToHtml(markdown, "default <title>", new ConversionSettings(), fragment: false).Html;

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

        // A resolved path that contains no character to escape resolves to itself: the whitespace around a reference is not part of it
        if (resolved.AsSpan().IndexOfAny("%#?:") < 0 && !char.IsWhiteSpace(resolved[0]) && !char.IsWhiteSpace(resolved[^1]))
        {
            FuzzAssert.Equal(resolved, EpubPackage.ResolvePath(basePath: "", resolved), "Resolving a resolved path changes it");
        }

        // The escaped segments of a resolved path resolve to it (the escaping replaces the unpaired surrogates)
        if (!resolved.AsSpan().ContainsAnyInRange('\uD800', '\uDFFF'))
        {
            FuzzAssert.Equal(resolved, EpubPackage.ResolvePath(basePath: "", string.Join('/', resolved.Split('/').Select(Uri.EscapeDataString))), "Resolving an escaped resolved path changes it");
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
        catch (Exception ex) when (ex is InvalidDataException or NotSupportedException)
        {
            // An archive with too many entries, or a symbolic link whose target cannot be read
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

        // The content of the files, read once: the copy must give the same content. The copy keeps the entries that have the
        // same name, so the content of such a name depends on the entry a reader picks: these names are not checked
        var files = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        var duplicatedNames = archive.Entries.GroupBy(entry => entry.FullName.TrimEnd('/'), StringComparer.Ordinal).Where(group => group.Count() > 1).Select(group => group.Key).ToHashSet(StringComparer.Ordinal);
        try
        {
            foreach (var entry in archive.Entries)
            {
                if (!entry.IsDirectory && entry.LinkTarget is null && entry.HardLinkTarget is null && !duplicatedNames.Contains(entry.FullName.TrimEnd('/')))
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
            int count;
            try
            {
                using var writer = ArchiveWriter.Create(output, archiveType, new ConversionSettings(), storeByDefault: true);
                count = ArchiveOutputWriter.Copy(archive, writer, _ => { }, CancellationToken.None);
            }
            catch (Exception ex) when (ex is InvalidDataException or IOException or NotSupportedException or ArgumentException or InvalidOperationException)
            {
                // Names the output format cannot store (empty, too long, duplicated...), or content that is not valid
                return;
            }

            // A tar archive without entries has no header: no reader can detect its format
            if (count is 0)
                continue;

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

        if (format is { Kind: FileFormatKind.Mailbox or FileFormatKind.EmailMessage })
        {
            FuzzAssert.True(EmailFormatDetector.Detect(data) is not EmailFileFormat.Unknown, "An email format is detected from content the email library does not recognize");
        }

        FuzzAssert.Equal(MobiBook.HasSignature(data), format == FileFormat.Mobi, "A file is detected as a MOBI book without its signature, or the reverse");

        // The extension of a comic book archive makes an archive a comic book archive, whatever its container
        stream.Position = 0;
        var comicFormat = Converter.DetectFormat(stream, "input.cbz");
        if (format is { Kind: FileFormatKind.Archive } && comicFormat is { Kind: FileFormatKind.Comic })
        {
            FuzzAssert.Equal(format.ArchiveType, comicFormat.ArchiveType, "The container of a comic book archive is not the one of the archive");
        }
    }

    /// <summary>
    /// Converts a message to EML, MSG, EMLX and twice to one mbox file: an EML output has only CRLF line breaks, and the lines of an EML input are kept;
    /// the MSG, EMLX and mbox outputs are read back.
    /// </summary>
    private static void EmailMessage(ReadOnlySpan<byte> data)
    {
        FileFormat? format;
        using (var stream = new MemoryStream(data.ToArray(), writable: false))
        {
            format = Converter.DetectFormat(stream, "input.bin");
        }

        if (format is not { Kind: FileFormatKind.EmailMessage })
            return;

        var directory = FullPath.FromPath(Path.Combine(Path.GetTempPath(), "file-conversion-email-" + Guid.NewGuid().ToString("N")));
        Directory.CreateDirectory(directory);
        try
        {
            var input = directory / "input.bin";
            File.WriteAllBytes(input, data.ToArray());
            var eml = directory / "output.eml";
            var msg = directory / "output.msg";
            var emlx = directory / "output.emlx";
            var mbox = directory / "output.mbox";
            try
            {
                EmailOutputWriter.WriteMessage(input, format, eml, FileFormat.Eml);
                EmailOutputWriter.WriteMessage(input, format, msg, FileFormat.Msg);
                EmailOutputWriter.WriteMessage(input, format, emlx, FileFormat.Emlx);
                EmailOutputWriter.WriteMessages([(input, format), (input, format)], mbox, FileFormat.Mbox, new ConversionSettings(), TextWriter.Null);
            }
            catch (EmailException)
            {
                // The documented error of a message that is not valid or uses a feature the library does not support
                return;
            }

            var output = File.ReadAllBytes(eml);
            FuzzAssert.Equal(output.AsSpan().Count((byte)'\n'), output.AsSpan().Count("\r\n"u8), "The EML output has a line break that is not CRLF");
            if (format == FileFormat.Eml)
            {
                FuzzAssert.Equal(ToLines(data), ToLines(output), "The EML output does not have the lines of the EML input");
            }

            MsgFile.Load(msg);
            EmlxFile.Load(emlx);

            // The mbox file has the two messages, and the lines of an EML input (the mbox format escapes the lines that start with "From ")
            var messages = MboxReader.ReadMessages(mbox).ToList();
            FuzzAssert.Equal(2, messages.Count, "The mbox file does not have a message per input");
            if (format == FileFormat.Eml && !Encoding.Latin1.GetString(data).ReplaceLineEndings("\n").Contains("\nFrom ", StringComparison.Ordinal) && !data.StartsWith("From "u8))
            {
                FuzzAssert.Equal(ToLines(data), ToLines(messages[0].Content.Span), "The mbox file does not have the lines of the EML input");
            }
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }

        static string ToLines(ReadOnlySpan<byte> value) => Encoding.Latin1.GetString(value).ReplaceLineEndings("\n").TrimEnd('\n');
    }

    private static void MobiBookTarget(ReadOnlySpan<byte> data)
    {
        var bytes = data.ToArray();
        MobiBook book;
        try
        {
            book = MobiBook.Read(bytes);
        }
        catch (InvalidDataException)
        {
            // The documented error of a file that is not a MOBI book
            return;
        }

        FuzzAssert.True(MobiBook.HasSignature(bytes), "A file without the signature of a MOBI book is read as a book");
        FuzzAssert.True(book.Mobi6 is not null || book.Kf8 is not null, "A book has no version");
        FuzzAssert.True(book.Kf8 is null || book.Kf8.IsKf8, "The KF8 version of a book is not a KF8 header");
        if (book.Mobi6 is { } mobi6)
        {
            CheckMobi6(book, mobi6);
        }

        if (book.Kf8 is { } kf8)
        {
            CheckKf8(book, kf8);
        }
    }

    private static void CheckMobi6(MobiBook book, MobiHeader header)
    {
        byte[] text;
        try
        {
            text = book.ReadText(header);
        }
        catch (InvalidDataException)
        {
            // The documented error of a text that cannot be decompressed
            return;
        }

        FuzzAssert.True(text.Length <= MobiBook.MaxTextLength, "The text of a book is longer than the maximum length");
        FuzzAssert.True(text.AsSpan().SequenceEqual(book.ReadText(header)), "The text of a book is not the same when it is read again");

        var html = CheckMobiHtml(text, header.IsUtf8, out var images);
        var loader = new MobiDocumentLoader(book, html, header.EncodingName, kf8: null);
        FuzzAssert.True(Load(loader, MobiHtml.DocumentUrl) is not null, "The HTML of the book is a network error");
        foreach (var index in images)
        {
            // An image is an image of a supported format, a font, or is not found
            FuzzAssert.True(Load(loader, MobiHtml.GetImageUrl(index)) is not null, "The URL of an image of the book is a network error");
        }

        CheckCover(book, images);
    }

    private static void CheckKf8(MobiBook book, MobiHeader header)
    {
        Kf8Content content;
        try
        {
            content = Kf8Content.Read(book, header);
        }
        catch (InvalidDataException)
        {
            // The documented error of a KF8 version whose text, flows or indexes are not valid
            return;
        }

        FuzzAssert.True(content.Parts.Count > 0, "A KF8 book has no document");
        FuzzAssert.Equal(content.Parts.Count, content.PartsAreXml.Count, "A document of a KF8 book has no media type");
        FuzzAssert.True(content.Flows.Count > 0 && content.Flows[0].Length is 0, "The first flow of a KF8 book is not the documents");
        FuzzAssert.False(content.ResourceIndexes.Any(index => index < 1), "A KF8 book uses a resource whose number is not positive");
        if (content.ImagePages is { } imagePages)
        {
            FuzzAssert.Equal(content.Parts.Count, imagePages.Count, "The images of a KF8 comic are not one per document");
            foreach (var index in imagePages)
            {
                FuzzAssert.True(book.GetImage(index) is { } image && Image.DetectFormat(image.Span) is not (ImageFormat.Unknown or ImageFormat.Custom), "A page of a KF8 comic is not an image");
            }
        }

        var loader = new MobiDocumentLoader(book, html: null, header.EncodingName, content);
        for (var i = 0; i < content.Parts.Count; i++)
        {
            FuzzAssert.Equal(i, Kf8Content.GetPartIndex(Kf8Content.GetPartUrl(i)), "The URL of a document does not give its index back");
            var resource = Load(loader, Kf8Content.GetPartUrl(i) + "#anchor");
            FuzzAssert.True(resource?.ContentBytes is { } bytes && bytes.Span.SequenceEqual(content.Parts[i]), "The URL of a document does not serve the document");
        }

        for (var i = 1; i < content.Flows.Count; i++)
        {
            FuzzAssert.Equal(i, Kf8Content.GetFlowIndex(Kf8Content.GetFlowUrl(i, "text/css"), out _), "The URL of a flow does not give its index back");
            FuzzAssert.True(Load(loader, Kf8Content.GetFlowUrl(i, "text/css")) is not null, "The URL of a flow is a network error");
        }

        foreach (var index in content.ResourceIndexes)
        {
            FuzzAssert.True(Load(loader, MobiHtml.GetImageUrl(index)) is not null, "The URL of a resource of the book is a network error");
        }

        CheckCover(book, content.ResourceIndexes);
    }

    private static void CheckCover(MobiBook book, IReadOnlySet<int> images)
    {
        if (MobiInputDocument.GetCover(book, images) is { } cover)
        {
            FuzzAssert.True(book.CoverImageIndex is { } coverIndex && !images.Contains(coverIndex), "The cover is a page of its own although the text shows it");
            FuzzAssert.Equal(cover.Format, Image.DetectFormat(cover.Data.Span), "The format of the cover is not the format of its data");
        }
    }

    private static WebPlatform.Dom.DocumentResource? Load(MobiDocumentLoader loader, string url)
        => loader.LoadAsync(new WebPlatform.Dom.DocumentRequest(url), CancellationToken.None).AsTask().GetAwaiter().GetResult();

    private static void MobiFontTarget(ReadOnlySpan<byte> data)
    {
        // The first byte gives the flags of the record: the rest is the font file
        var flags = FuzzInput.ReadByte(ref data);
        var font = data.ToArray();
        var record = TestFiles.CreateFontRecord(font, compress: (flags & 1) is not 0, obfuscate: (flags & 2) is not 0);
        FuzzAssert.True(MobiFont.Decode(record).AsSpan().SequenceEqual(font), "A font does not round-trip through a FONT record");

        // Any record decodes, or is reported as not valid
        try
        {
            MobiFont.Decode(data);
        }
        catch (InvalidDataException)
        {
        }
    }

    private static void MobiHtmlTarget(ReadOnlySpan<byte> data)
    {
        // The first byte tells whether the text is UTF-8
        var isUtf8 = (FuzzInput.ReadByte(ref data) & 1) is 0;
        CheckMobiHtml(data, isUtf8, out _);
    }

    private static byte[] CheckMobiHtml(ReadOnlySpan<byte> text, bool isUtf8, out IReadOnlySet<int> images)
    {
        var html = MobiHtml.Convert(text, isUtf8, out images);
        FuzzAssert.True(html.AsSpan().SequenceEqual(MobiHtml.Convert(text, isUtf8, out _)), "The conversion of the text of a book is not deterministic");

        var htmlText = Encoding.Latin1.GetString(html);
        foreach (var index in images)
        {
            FuzzAssert.True(htmlText.Contains($"src=\"{MobiHtml.GetImageUrl(index)}\"", StringComparison.Ordinal), "An image of the text has no URL");
            FuzzAssert.Equal(index, MobiHtml.GetImageIndex(MobiHtml.GetImageUrl(index)), "The URL of an image does not give its index back");
        }

        // The anchors and the attributes go between the characters of a UTF-8 text
        if (isUtf8 && System.Text.Unicode.Utf8.IsValid(text))
        {
            FuzzAssert.True(System.Text.Unicode.Utf8.IsValid(html), "The conversion splits a character of a UTF-8 text");
        }

        return html;
    }

    private static void PalmDoc(ReadOnlySpan<byte> data)
    {
        // A text record holds 4096 bytes of text
        var text = data[..Math.Min(data.Length, 4096)];
        var output = new MobiBuffer(MobiBook.MaxTextLength);
        PalmDocCompression.Decompress(TestFiles.CompressPalmDoc(text), output);
        FuzzAssert.True(output.WrittenSpan.SequenceEqual(text), "The PalmDOC compression does not round-trip");

        // Any data decompresses, or is reported as not valid
        try
        {
            PalmDocCompression.Decompress(text, new MobiBuffer(MobiBook.MaxTextLength));
        }
        catch (InvalidDataException)
        {
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

    private static IEnumerable<FuzzSeed> GetEmailSeeds()
    {
        yield return new FuzzSeed("eml", TestFiles.CreateEml("Hello"));
        yield return new FuzzSeed("eml-lf", "From: alice@example.com\nSubject: Hello\n\nHello\n"u8.ToArray());
        yield return new FuzzSeed("eml-multipart", "From: alice@example.com\r\nSubject: Parts\r\nMIME-Version: 1.0\r\nContent-Type: multipart/mixed; boundary=b\r\n\r\npreamble\r\n--b\r\nContent-Type: text/plain\r\n\r\nHello\r\n--b\r\nContent-Type: application/octet-stream\r\nContent-Transfer-Encoding: base64\r\n\r\nAAEC\r\n--b--\r\n"u8.ToArray());
        yield return new FuzzSeed("msg", TestFiles.CreateMsg("Hello"));
        yield return new FuzzSeed("tnef", TestFiles.CreateTnef("Hello"));
        using var emlx = new MemoryStream();
        EmlxFile.Save(MimeMessage.Load(TestFiles.CreateEml("Hello")), emlx, EmlxFlags.Read);
        yield return new FuzzSeed("emlx", emlx.ToArray());
    }

    private static IEnumerable<FuzzSeed> GetMobiSeeds()
    {
        yield return new FuzzSeed("mobi-palmdoc", TestFiles.CreateTextMobi());
        yield return new FuzzSeed("mobi-huffcdic", TestFiles.CreateTextMobi(TestFiles.MobiCompression.HuffCdic, utf8: false, trailingEntries: true));
        yield return new FuzzSeed("mobi-uncompressed", TestFiles.CreateTextMobi(TestFiles.MobiCompression.None, trailingEntries: true));
        yield return new FuzzSeed("mobi-drm", TestFiles.CreateMobi("<p>Text</p>", encryption: 2));
        yield return new FuzzSeed("mobi-kf8", TestFiles.CreateMobi("<p>Text</p>", fileVersion: 8));
    }

    private static IEnumerable<FuzzSeed> GetKf8Seeds()
    {
        yield return new FuzzSeed("kf8-text", TestFiles.CreateTextKf8());
        yield return new FuzzSeed("kf8-huffcdic", TestFiles.CreateTextKf8(compression: TestFiles.MobiCompression.HuffCdic, trailingEntries: true));
        yield return new FuzzSeed("kf8-joint", TestFiles.CreateTextKf8(mobi6Html: "<html><body><p>The MOBI 6 version</p></body></html>"));
        yield return new FuzzSeed("kf8-joint-corrupt", TestFiles.CreateTextKf8(mobi6Html: "<p>MOBI 6</p>", corruptFdst: true));
        yield return new FuzzSeed("kf8-comic", TestFiles.CreateComicKf8());
        yield return new FuzzSeed("kf8-svg-cover", TestFiles.CreateKf8([TestFiles.CreateKf8Xhtml("""<div><img src="kindle:flow:0002?mime=image/svg+xml" alt=""/></div>""")], ["p { margin: 0 }", """<svg xmlns="http://www.w3.org/2000/svg" xmlns:xlink="http://www.w3.org/1999/xlink" viewBox="0 0 30 40"><image width="30" height="40" xlink:href="kindle:embed:0001?mime=image/jpeg"/></svg>"""], [TestFiles.CreateImage(30, 40, ImageFormat.Jpeg)], coverIndex: 1));
        yield return new FuzzSeed("kf8-font", TestFiles.CreateKf8([TestFiles.CreateKf8Xhtml("<p>Text</p>")], ["@font-face { src: url(kindle:embed:0001?mime=application/x-font-ttf) }"], [TestFiles.CreateFontRecord([0, 1, 0, 0, 1, 2, 3], compress: true, obfuscate: true)]));
    }

    private static IEnumerable<FuzzSeed> GetMobiFontSeeds()
    {
        byte[] font = [0, 1, 0, 0, .. Enumerable.Range(0, 1200).Select(i => (byte)(i % 251))];
        for (var flags = 0; flags < 4; flags++)
        {
            yield return new FuzzSeed($"font-{flags.ToString(CultureInfo.InvariantCulture)}", (byte[])[(byte)flags, .. font]);
            yield return new FuzzSeed($"font-record-{flags.ToString(CultureInfo.InvariantCulture)}", (byte[])[(byte)flags, .. TestFiles.CreateFontRecord(font, compress: (flags & 1) is not 0, obfuscate: (flags & 2) is not 0)]);
        }
    }

    private static IEnumerable<FuzzSeed> GetMobiHtmlSeeds()
    {
        // The first byte is 0 for a UTF-8 text, 1 for a Windows-1252 text
        string[] documents =
        [
            """<html><head><guide><reference type="toc" filepos=5 /></guide></head><body><p>Hello <a filepos="0000000125">link</a></p><img recindex='3' hirecindex="4" alt=""><mbp:pagebreak/><p>End</p></body></html>""",
            """<html><body><p><a filepos=0000000060>Go</a></p><h1>Chapter one</h1><img recindex="00001" /><mbp:pagebreak/><h1>Chapter two</h1></body></html>""",
            """<p>Le café coûte 3 €.</p><a filepos=13>é</a><a filepos=14>split</a>""",
            "",
        ];
        for (var i = 0; i < documents.Length; i++)
        {
            yield return new FuzzSeed($"mobi-html-utf8-{i}", (byte[])[0, .. Encoding.UTF8.GetBytes(documents[i])]);
            yield return new FuzzSeed($"mobi-html-1252-{i}", (byte[])[1, .. Encoding.Latin1.GetBytes(documents[i])]);
        }
    }

    private static IEnumerable<FuzzSeed> GetDetectFormatSeeds() => GetArchiveSeeds().Concat(GetMobiSeeds()).Concat(GetKf8Seeds()).Concat(GetEmailSeeds()).Append(new FuzzSeed("pst", TestFiles.CreatePst())).Append(new FuzzSeed("mbox", TestFiles.CreateMbox("First", "Second")));

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
