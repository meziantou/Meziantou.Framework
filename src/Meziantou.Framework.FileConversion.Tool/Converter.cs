using Meziantou.Framework.Compression;
using Meziantou.Framework.FileConversionTool.Inputs;
using Meziantou.Framework.FileConversionTool.Outputs;
using Meziantou.Framework.Globbing;
using Meziantou.Framework.Imaging;

namespace Meziantou.Framework.FileConversionTool;

/// <summary>Converts the input files to the output format, and names the output files.</summary>
internal sealed class Converter(ConversionSettings settings, TextWriter output, TextWriter error)
{
    private const string SingleOutputTemplate = "{dir}/{name}.{ext}";
    private const string PageOutputTemplate = "{dir}/{name}-{page}.{ext}";

    private readonly HashSet<FullPath> _writtenPaths = [];
    private HashSet<FullPath> _inputPaths = [];

    public void Convert(CancellationToken cancellationToken)
    {
        var inputs = ResolveInputs(settings.Inputs);
        _inputPaths = [.. inputs.Select(FileLinks.GetComparisonKey)];
        var inputFormats = inputs.Select(DetectInputFormat).ToArray();

        var template = settings.Output is null ? null : OutputNameTemplate.Parse(settings.Output);
        var outputFormat = settings.OutputFormat
            ?? (template is not null && !Path.GetExtension(template.Text).Contains('{', StringComparison.Ordinal) ? FileFormat.FromFileName(template.Text) : null)
            ?? throw new ConversionException("The output format is unknown: set --output-format, or end --output with the extension of the format");

        if (!outputFormat.CanWrite)
            throw new ConversionException($"The {outputFormat.Name} format cannot be written");

        if (outputFormat.Kind is FileFormatKind.Pdf)
        {
            ValidatePdfOutputSettings();
        }

        if (outputFormat.Kind is FileFormatKind.Markdown or FileFormatKind.Html)
        {
            ConvertTextDocuments(inputs, inputFormats, template, outputFormat);
        }
        else if (outputFormat.Kind is FileFormatKind.Archive)
        {
            ConvertArchives(inputs, inputFormats, template, outputFormat, cancellationToken);
        }
        else if (outputFormat.IsMultiPage)
        {
            ConvertToMultiPageFiles(inputs, inputFormats, template, outputFormat, cancellationToken);
        }
        else
        {
            ConvertToImageFiles(inputs, inputFormats, template, outputFormat, cancellationToken);
        }
    }

    private void ValidatePdfOutputSettings()
    {
        var encrypted = settings.PdfOutputPassword is not null || settings.PdfOwnerPassword is not null;
        if (encrypted && settings.PdfConformance is not PdfConformance.None)
            throw new ConversionException("PDF/A does not allow encryption: remove --pdf-conformance, or --pdf-output-password and --pdf-owner-password");

        if (encrypted && settings.PdfLinearize)
            throw new ConversionException("A linearized PDF file cannot be encrypted: remove --pdf-linearize, or --pdf-output-password and --pdf-owner-password");

        if (settings.PdfPermissions is not null && settings.PdfOwnerPassword is null)
            throw new ConversionException("--pdf-permissions needs --pdf-owner-password: without an owner password, the user password allows every operation");

        if (settings.PdfOutputIntent is { } outputIntent)
        {
            if (settings.PdfConformance is PdfConformance.None)
                throw new ConversionException("--pdf-output-intent needs --pdf-conformance");

            if (!File.Exists(outputIntent))
                throw new ConversionException($"The ICC profile '{outputIntent}' does not exist");
        }
    }

    /// <summary>Expands the glob patterns of the inputs, sorted in natural order (<c>page2</c> before <c>page10</c>), and keeps the files as they are given.</summary>
    internal static List<FullPath> ResolveInputs(IReadOnlyList<string> inputs)
    {
        var result = new List<FullPath>();
        foreach (var input in inputs)
        {
            if (File.Exists(input) || input.AsSpan().IndexOfAny("*?[{") < 0)
            {
                var path = FullPath.FromPath(input);
                if (!File.Exists(path))
                    throw new ConversionException($"The input file '{path}' does not exist");

                result.Add(path);
                continue;
            }

            // The base folder is the part of the pattern before the first segment with a wildcard
            var normalized = input.Replace('\\', '/');
            var wildcard = normalized.AsSpan().IndexOfAny("*?[{");
            var separator = normalized.AsSpan(0, wildcard).LastIndexOf('/');
            var directory = separator < 0 ? "." : normalized[..(separator + 1)];
            var pattern = separator < 0 ? normalized : normalized[(separator + 1)..];

            var directoryPath = FullPath.FromPath(directory);
            if (!Directory.Exists(directoryPath))
                throw new ConversionException($"The folder '{directoryPath}' of the input pattern '{input}' does not exist");

            if (!Glob.TryParse(pattern, GlobDialect.Standard, GlobOptions.IgnoreCase, out var glob))
                throw new ConversionException($"The input pattern '{input}' is not valid");

            var matches = glob.EnumerateFiles(directoryPath).Select(FullPath.FromPath).OrderBy(path => path.Value, NaturalStringComparer.Instance).ToList();
            if (matches.Count is 0)
                throw new ConversionException($"No file matches the input pattern '{input}'");

            result.AddRange(matches);
        }

        if (result.Count is 0)
            throw new ConversionException("No input file");

        return result;
    }

    /// <summary>
    /// Detects the format of a file from its content: an image format, PDF, EPUB, or an archive (zip, 7z, RAR, tar...). An archive
    /// whose extension is the one of a comic book archive (<c>.cbz</c>, <c>.cbr</c>...) is a comic book archive, whatever its
    /// container. A Markdown or HTML file is recognized by its extension, or an HTML file by its start (<c>&lt;!DOCTYPE html</c>,
    /// <c>&lt;html</c>). The extension is used when the content is not recognized.
    /// </summary>
    internal static FileFormat? DetectFormat(Stream stream, string fileName)
    {
        Span<byte> prefix = stackalloc byte[64];
        var length = stream.ReadAtLeast(prefix, prefix.Length, throwOnEndOfStream: false);
        prefix = prefix[..length];
        stream.Position = 0;

        var extensionFormat = FileFormat.FromFileName(fileName);

        // The text formats have no signature, and their text can start like one ("BM" of BMP, "%PDF-" in a document about PDF)
        if (extensionFormat?.Kind is FileFormatKind.Markdown or FileFormatKind.Html)
            return extensionFormat;

        if (prefix.StartsWith("%PDF-"u8))
            return FileFormat.Pdf;

        // An EPUB file starts with its uncompressed "mimetype" entry
        if (prefix.StartsWith("PK\x03\x04"u8) && ((prefix.Length >= 58 && prefix[30..].StartsWith("mimetypeapplication/epub+zip"u8)) || extensionFormat?.Kind is FileFormatKind.Epub))
            return FileFormat.Epub;

        var imageFormat = Image.DetectFormat(prefix);
        if (imageFormat is not ImageFormat.Unknown and not ImageFormat.Custom)
            return FileFormat.FromImageFormat(imageFormat);

        ArchiveFormatInfo archiveFormat;
        try
        {
            archiveFormat = ArchiveFile.DetectFormat(stream);
        }
        catch (InvalidDataException)
        {
            // A compressed stream whose content is corrupted
            archiveFormat = default;
        }

        var archiveType = archiveFormat switch
        {
            { Format: ArchiveFormat.Zip } => ArchiveType.Zip,
            { Format: ArchiveFormat.SevenZip } => ArchiveType.SevenZip,
            { Format: ArchiveFormat.Rar } => ArchiveType.Rar,
            { Format: ArchiveFormat.Tar, CompressionFormat: CompressionFormat.None } => ArchiveType.Tar,
            { Format: ArchiveFormat.Tar, CompressionFormat: CompressionFormat.GZip } => ArchiveType.TarGzip,
            { Format: ArchiveFormat.Tar, CompressionFormat: CompressionFormat.BZip2 } => ArchiveType.TarBzip2,
            { Format: ArchiveFormat.Tar, CompressionFormat: CompressionFormat.Xz } => ArchiveType.TarXz,
            { Format: ArchiveFormat.Tar, CompressionFormat: CompressionFormat.Lzma } => ArchiveType.TarLzma,
            { Format: ArchiveFormat.Tar, CompressionFormat: CompressionFormat.LZip } => ArchiveType.TarLzip,
            { Format: ArchiveFormat.Tar, CompressionFormat: CompressionFormat.Zstandard } => ArchiveType.TarZstandard,
            { Format: ArchiveFormat.Tar, CompressionFormat: CompressionFormat.UnixCompress } => ArchiveType.TarUnixCompress,
            _ => ArchiveType.None,
        };

        if (archiveType is not ArchiveType.None)
            return FileFormat.FromArchiveType(archiveType, comic: extensionFormat?.Kind is FileFormatKind.Comic);

        return extensionFormat ?? (IsHtml(prefix) ? FileFormat.Html : null);
    }

    /// <summary>Gets the name of a format with its indefinite article: <c>a PNG</c>, <c>an EPUB</c>, <c>an HTML</c>.</summary>
    private static string WithArticle(string name) => (name is "HTML" || "AEIOaeio".Contains(name[0], StringComparison.Ordinal) ? "an " : "a ") + name;

    private static bool IsHtml(ReadOnlySpan<byte> prefix)
    {
        if (prefix.StartsWith((ReadOnlySpan<byte>)[0xEF, 0xBB, 0xBF]))
        {
            prefix = prefix[3..];
        }

        prefix = prefix.TrimStart(" \t\r\n"u8);
        return StartsWithIgnoreCase(prefix, "<!doctype html"u8) || StartsWithIgnoreCase(prefix, "<html"u8);

        static bool StartsWithIgnoreCase(ReadOnlySpan<byte> value, ReadOnlySpan<byte> prefix)
            => value.Length >= prefix.Length && Ascii.EqualsIgnoreCase(value[..prefix.Length], prefix);
    }

    private FileFormat DetectInputFormat(FullPath path)
    {
        if (settings.InputFormat is { } format)
        {
            if (!format.CanRead)
                throw new ConversionException($"The {format.Name} format cannot be read");

            return format;
        }

        using var stream = File.OpenRead(path);
        return DetectFormat(stream, path.Name)
            ?? throw new ConversionException($"The format of '{path}' is not supported. Set --input-format if its content is in a supported format");
    }

    /// <summary>Converts each archive to its own archive of the output format, by copying its entries.</summary>
    private void ConvertArchives(List<FullPath> inputs, FileFormat[] inputFormats, OutputNameTemplate? template, FileFormat outputFormat, CancellationToken cancellationToken)
    {
        template ??= OutputNameTemplate.Parse(SingleOutputTemplate);
        for (var i = 0; i < inputs.Count; i++)
        {
            var input = inputs[i];
            if (inputFormats[i].Kind is not (FileFormatKind.Archive or FileFormatKind.Comic or FileFormatKind.Epub))
                throw new ConversionException($"The input '{input}' is {WithArticle(inputFormats[i].Name)} file, which cannot be converted to an archive: only archives can");

            var values = new OutputNameValues(FileFormat.GetNameWithoutExtension(input.Name), outputFormat.DefaultExtension[1..], input.Parent, Page: 1, PageCount: 1, i + 1, inputs.Count);
            var outputPath = GetOutputPath(template, values, input);
            outputPath.CreateParentDirectory();
            ArchiveOutputWriter.Write(outputPath, input, outputFormat, settings, error, cancellationToken);
            output.WriteLine(outputPath);
        }
    }

    /// <summary>Converts each Markdown document to HTML, or each HTML document to Markdown.</summary>
    private void ConvertTextDocuments(List<FullPath> inputs, FileFormat[] inputFormats, OutputNameTemplate? template, FileFormat outputFormat)
    {
        var inputKind = outputFormat.Kind is FileFormatKind.Html ? FileFormatKind.Markdown : FileFormatKind.Html;
        template ??= OutputNameTemplate.Parse(SingleOutputTemplate);
        var extension = GetExtension(template, outputFormat);
        for (var i = 0; i < inputs.Count; i++)
        {
            var input = inputs[i];
            if (inputFormats[i].Kind != inputKind)
                throw new ConversionException($"The input '{input}' is {WithArticle(inputFormats[i].Name)} file, which cannot be converted to {outputFormat.Name}: only {(inputKind is FileFormatKind.Markdown ? "Markdown" : "HTML")} documents can");

            var name = FileFormat.GetNameWithoutExtension(input.Name);
            var values = new OutputNameValues(name, extension[1..], input.Parent, Page: 1, PageCount: 1, i + 1, inputs.Count);
            var outputPath = GetOutputPath(template, values, input);

            var text = File.ReadAllText(input);
            var result = outputFormat.Kind is FileFormatKind.Html
                ? TextDocumentConverter.MarkdownToHtml(text, name, settings, settings.HtmlFragment).Html
                : TextDocumentConverter.HtmlToMarkdown(text, settings);

            outputPath.CreateParentDirectory();
            var bytes = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false).GetBytes(result);
            ArchiveFiles.WriteAtomically(outputPath, stream => stream.Write(bytes));
            output.WriteLine(outputPath);
        }
    }

    private void ConvertToMultiPageFiles(List<FullPath> inputs, FileFormat[] inputFormats, OutputNameTemplate? template, FileFormat outputFormat, CancellationToken cancellationToken)
    {
        if (settings.PerPage)
        {
            // Each page goes to its own file, named like the images of a conversion to single-image files
            ConvertPages(inputs, inputFormats, template, outputFormat, (outputPath, document, page) => WriteMultiPageFile(outputPath, outputFormat, document, [page], pageCount: 1, cancellationToken), cancellationToken);
            return;
        }

        // All the inputs go to one file, or each input to its own file with --per-input
        var groups = settings.PerInput || inputs.Count is 1
            ? inputs.Select((input, index) => (Inputs: (IReadOnlyList<int>)[index], Index: index)).ToList()
            : [(Inputs: Enumerable.Range(0, inputs.Count).ToArray(), Index: 0)];

        if (groups.Count is 1 && inputs.Count > 1 && template is null)
            throw new ConversionException($"The {inputs.Count.ToString(CultureInfo.InvariantCulture)} inputs go to one {outputFormat.Name} file: set its path with --output, or use --per-input to convert each input to its own file");

        template ??= OutputNameTemplate.Parse(SingleOutputTemplate);
        foreach (var group in groups)
        {
            var first = inputs[group.Inputs[0]];
            var outputPath = GetOutputPath(template, new OutputNameValues(FileFormat.GetNameWithoutExtension(first.Name), outputFormat.DefaultExtension[1..], first.Parent, Page: 1, PageCount: 1, group.Index + 1, groups.Count), first);

            var documents = new List<InputDocument>();
            try
            {
                foreach (var index in group.Inputs)
                {
                    documents.Add(InputDocument.Open(inputs[index], inputFormats[index], settings, outputFormat, cancellationToken));
                }

                var pageCount = documents.All(document => document.PageCount is not null) ? documents.Sum(document => settings.Pages.Count(document.PageCount!.Value)) : (int?)null;
                // The metadata of a document is copied when it is the only input of the output
                var source = documents.Count is 1 ? documents[0] : null;
                WriteMultiPageFile(outputPath, outputFormat, source, SelectPages(documents, cancellationToken), pageCount, cancellationToken);
            }
            finally
            {
                foreach (var document in documents)
                {
                    document.Dispose();
                }
            }

            output.WriteLine(outputPath);
        }
    }

    private void ConvertToImageFiles(List<FullPath> inputs, FileFormat[] inputFormats, OutputNameTemplate? template, FileFormat outputFormat, CancellationToken cancellationToken)
    {
        var encoder = settings.CreateEncoder(outputFormat.ImageFormat, GetExtension(template, outputFormat));
        ConvertPages(inputs, inputFormats, template, outputFormat, (outputPath, document, page) =>
        {
            using var image = page.RenderImage(cancellationToken);
            settings.ResizeImage(image, cancellationToken);
            outputPath.CreateParentDirectory();
            ArchiveFiles.WriteAtomically(outputPath, stream => image.Save(stream, encoder));
        }, cancellationToken);
    }

    /// <summary>Writes each selected page of the inputs to its own file.</summary>
    private void ConvertPages(List<FullPath> inputs, FileFormat[] inputFormats, OutputNameTemplate? template, FileFormat outputFormat, Action<FullPath, InputDocument, ConversionPage> write, CancellationToken cancellationToken)
    {
        var extension = GetExtension(template, outputFormat);

        // The documents are opened one at a time, but their page counts are needed first to number the output files
        var pageCounts = new int?[inputs.Count];
        for (var i = 0; i < inputs.Count; i++)
        {
            using var document = InputDocument.Open(inputs[i], inputFormats[i], settings, outputFormat, cancellationToken);
            pageCounts[i] = document.PageCount is { } count ? settings.Pages.Count(count) : null;
        }

        int? indexCount = pageCounts.All(count => count is not null) ? pageCounts.Sum() : null;
        var index = 0;
        for (var i = 0; i < inputs.Count; i++)
        {
            var input = inputs[i];
            var inputTemplate = template ?? OutputNameTemplate.Parse(pageCounts[i] is 1 ? SingleOutputTemplate : PageOutputTemplate);
            using var document = InputDocument.Open(input, inputFormats[i], settings, outputFormat, cancellationToken);
            var written = 0;
            foreach (var page in SelectPages([document], cancellationToken))
            {
                index++;
                written++;
                var values = new OutputNameValues(FileFormat.GetNameWithoutExtension(input.Name), extension[1..], input.Parent, page.PageNumber, document.PageCount, index, indexCount);
                var outputPath = GetOutputPath(inputTemplate, values, input);
                write(outputPath, document, page);
                output.WriteLine(outputPath);
            }

            if (written is 0)
                throw new ConversionException($"The input '{input}' has no page to convert");
        }
    }

    /// <summary>Writes pages to a PDF, comic book archive or TIFF file. The title and the author of <paramref name="source"/> go to the metadata of a PDF file.</summary>
    private void WriteMultiPageFile(FullPath outputPath, FileFormat outputFormat, InputDocument? source, IEnumerable<ConversionPage> pages, int? pageCount, CancellationToken cancellationToken)
    {
        outputPath.CreateParentDirectory();
        switch (outputFormat.Kind)
        {
            case FileFormatKind.Pdf:
                PdfOutputWriter.Write(outputPath, pages, source?.Title, source?.Author, settings, cancellationToken);
                break;
            case FileFormatKind.Comic:
                ComicOutputWriter.Write(outputPath, outputFormat, pages, pageCount, settings, cancellationToken);
                break;
            case FileFormatKind.Image:
                TiffOutputWriter.Write(outputPath, pages, settings, cancellationToken);
                break;
            default:
                throw new ConversionException($"The {outputFormat.Name} format cannot be written");
        }
    }

    /// <summary>Gets the pages of the documents that <c>--pages</c> selects, in order.</summary>
    private IEnumerable<ConversionPage> SelectPages(IEnumerable<InputDocument> documents, CancellationToken cancellationToken)
    {
        foreach (var document in documents)
        {
            foreach (var page in document.GetPages(cancellationToken))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (settings.Pages.Contains(page.PageNumber))
                {
                    yield return page;
                }
                else
                {
                    page.Discard();
                }
            }
        }
    }

    /// <summary>Gets the extension of the output files: the one of the output template when it is an extension of the output format (<c>.apng</c>, <c>.c7z</c>), or the default extension of the format.</summary>
    private static string GetExtension(OutputNameTemplate? template, FileFormat outputFormat)
        => template is not null && FileFormat.FromExtension(Path.GetExtension(template.Text)) == outputFormat ? Path.GetExtension(template.Text) : outputFormat.DefaultExtension;

    private FullPath GetOutputPath(OutputNameTemplate template, OutputNameValues values, FullPath input)
    {
        var path = FullPath.FromPath(template.Format(values));
        if (!_writtenPaths.Add(path))
        {
            var hint = template.HasPageNumber ? "" : ". Add {page} or {index} to the output template to name each page";
            throw new ConversionException($"Several outputs have the same path '{path}'{hint}");
        }

        // A link (of the file or of a parent folder) can make an output path another name of an input
        if (_inputPaths.Contains(FileLinks.GetComparisonKey(path)))
            throw new ConversionException($"The output '{path}' is the input file '{input}'");

        if (!settings.Overwrite && File.Exists(path))
            throw new ConversionException($"The output file '{path}' already exists. Use --overwrite to replace it");

        return path;
    }
}
