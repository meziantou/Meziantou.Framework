using System.CommandLine;
using System.CommandLine.Parsing;
using System.IO.Compression;
using System.Runtime.CompilerServices;
using Meziantou.Framework.Compression;
using Meziantou.Framework.Email.Storage;
using Meziantou.Framework.Imaging;
using Meziantou.Framework.Imaging.Formats;
using Meziantou.Framework.Pdf;

[assembly: InternalsVisibleTo("Meziantou.Framework.FileConversion.Tool.Tests")]
[assembly: InternalsVisibleTo("Meziantou.Framework.FileConversion.Tool.Fuzz")]

namespace Meziantou.Framework.FileConversionTool;

internal static class Program
{
    public static Task<int> Main(string[] args)
    {
        Console.OutputEncoding = Encoding.UTF8;
        return MainImpl(args, configure: null);
    }

    internal static Task<int> MainImpl(string[] args, Action<InvocationConfiguration>? configure)
    {
        var defaults = new ConversionSettings();

        // The options of the formats, listed by --list-formats
        var formatOptions = new List<(Func<FileFormat, bool> AppliesTo, Option Option)>();
        T AddFormatOption<T>(T option, Func<FileFormat, bool> appliesTo)
            where T : Option
        {
            formatOptions.Add((appliesTo, option));
            return option;
        }

        var inputsArgument = new Argument<string[]>("inputs")
        {
            Description = "The files to convert, or glob patterns such as 'scans/*.jpg'. The files a pattern matches are sorted in natural order (page2 before page10)",
            Arity = ArgumentArity.ZeroOrMore,
        };
        var outputOption = new Option<string?>("--output", "-o")
        {
            Description = "The path of the output files, with the placeholders {name}, {ext}, {dir}, {page}, {index} and {count}. {page:000} pads the number with zeros. Default: '{dir}/{name}.{ext}', or '{dir}/{name}-{page}.{ext}' when an input gives several images",
        };
        var inputFormatOption = new Option<FileFormat?>("--input-format")
        {
            Description = "The format of the inputs, when it must not be detected from their content",
            CustomParser = ParseFormat,
        };
        var outputFormatOption = new Option<FileFormat?>("--output-format")
        {
            Description = "The format of the outputs. Default: the format of the extension of --output",
            CustomParser = ParseFormat,
        };
        var overwriteOption = new Option<bool>("--overwrite")
        {
            Description = "Replace the output files that already exist",
        };
        var perInputOption = new Option<bool>("--per-input")
        {
            Description = "Convert each input to its own PDF, comic book archive, TIFF file or mailbox, instead of all the inputs to one file",
        };
        var perPageOption = new Option<bool>("--per-page")
        {
            Description = "Write each page to its own PDF, comic book archive or TIFF file, such as to split a PDF file. The output template needs {page} or {index}",
        };
        var pagesOption = new Option<PageRange>("--pages")
        {
            Description = "The pages of each input to convert, such as '1-3,7,10-'. Default: all the pages",
            CustomParser = result => Parse(result, PageRange.Parse),
        };
        var listFormatsOption = new Option<bool>("--list-formats")
        {
            Description = "List the formats, their extensions and their options",
        };

        // Images
        var maxWidthOption = new Option<int?>("--max-width") { Description = "Shrink the images to this width, in pixels, keeping their aspect ratio" };
        maxWidthOption.Validators.Add(result => ValidateRange(result, maxWidthOption, 1, 65535));
        var maxHeightOption = new Option<int?>("--max-height") { Description = "Shrink the images to this height, in pixels, keeping their aspect ratio" };
        maxHeightOption.Validators.Add(result => ValidateRange(result, maxHeightOption, 1, 65535));
        var backgroundOption = AddFormatOption(new Option<string>("--background")
        {
            Description = "The color the transparent pixels are composited over in JPEG output, in hexadecimal notation",
            DefaultValueFactory = _ => "#ffffff",
        }, format => format.ImageFormat is ImageFormat.Jpeg);
        backgroundOption.Validators.Add(result => Validate(result, backgroundOption, value => ParseColor(value)));
        var jpegQualityOption = AddFormatOption(new Option<int?>("--jpeg-quality") { Description = "The quality of the JPEG images, from 1 to 100. Default: 90" }, format => format.ImageFormat is ImageFormat.Jpeg);
        jpegQualityOption.Validators.Add(result => ValidateRange(result, jpegQualityOption, 1, 100));
        var webpQualityOption = AddFormatOption(new Option<int?>("--webp-quality") { Description = "The quality of the lossy WebP images, from 0 to 100" }, format => format.ImageFormat is ImageFormat.WebP);
        webpQualityOption.Validators.Add(result => ValidateRange(result, webpQualityOption, 0, 100));
        var webpLosslessOption = AddFormatOption(new Option<bool>("--webp-lossless") { Description = "Compress the WebP images without loss" }, format => format.ImageFormat is ImageFormat.WebP);
        var avifQualityOption = AddFormatOption(new Option<int?>("--avif-quality") { Description = "The quality of the lossy AVIF images, from 0 to 100" }, format => format.ImageFormat is ImageFormat.Avif);
        avifQualityOption.Validators.Add(result => ValidateRange(result, avifQualityOption, 0, 100));
        var avifEffortOption = AddFormatOption(new Option<int?>("--avif-effort") { Description = "The compression effort of the AVIF images, from 0 (fastest) to 9 (smallest)" }, format => format.ImageFormat is ImageFormat.Avif);
        avifEffortOption.Validators.Add(result => ValidateRange(result, avifEffortOption, 0, 9));
        var avifLosslessOption = AddFormatOption(new Option<bool>("--avif-lossless") { Description = "Compress the AVIF images without loss" }, format => format.ImageFormat is ImageFormat.Avif);
        var heicQualityOption = AddFormatOption(new Option<int?>("--heic-quality") { Description = "The quality of the lossy HEIC images, from 0 to 100" }, format => format.ImageFormat is ImageFormat.Heic);
        heicQualityOption.Validators.Add(result => ValidateRange(result, heicQualityOption, 0, 100));
        var jpegXlQualityOption = AddFormatOption(new Option<int?>("--jxl-quality") { Description = "The quality of the lossy JPEG XL images, from 0 to 100" }, format => format.ImageFormat is ImageFormat.JpegXl);
        jpegXlQualityOption.Validators.Add(result => ValidateRange(result, jpegXlQualityOption, 0, 100));
        var jpegXlLosslessOption = AddFormatOption(new Option<bool>("--jxl-lossless") { Description = "Compress the JPEG XL images without loss" }, format => format.ImageFormat is ImageFormat.JpegXl);
        var pngCompressionLevelOption = AddFormatOption(new Option<CompressionLevel?>("--png-compression-level") { Description = "The compression level of the PNG images" }, format => format.ImageFormat is ImageFormat.Png);
        var tiffCompressionOption = AddFormatOption(new Option<TiffCompression?>("--tiff-compression") { Description = "The compression of the TIFF images" }, format => format.ImageFormat is ImageFormat.Tiff);
        var gifMaxColorsOption = AddFormatOption(new Option<int?>("--gif-max-colors") { Description = "The maximum number of colors of the GIF images, from 2 to 256" }, format => format.ImageFormat is ImageFormat.Gif);
        gifMaxColorsOption.Validators.Add(result => ValidateRange(result, gifMaxColorsOption, 2, 256));

        // Rendering of PDF, EPUB and MOBI pages to images
        var dpiOption = new Option<double>("--dpi")
        {
            Description = "The resolution of the images of the PDF, EPUB and MOBI pages, in dots per inch",
            DefaultValueFactory = _ => defaults.Dpi,
        };
        dpiOption.Validators.Add(result =>
        {
            if (result.GetValue(dpiOption) is < 1 or > 2400)
            {
                result.AddError("--dpi must be between 1 and 2400");
            }
        });

        // PDF input
        var pdfPasswordOption = AddFormatOption(new Option<string?>("--pdf-password") { Description = "The password of the encrypted PDF inputs" }, format => format.Kind is FileFormatKind.Pdf);
        var pdfRenderAnnotationsOption = AddFormatOption(new Option<bool>("--pdf-render-annotations")
        {
            Description = "Render the annotations of the PDF pages (form fields, stamps, comments) in the images. Use '--pdf-render-annotations false' to leave them out. Default: true",
            DefaultValueFactory = _ => defaults.PdfRenderAnnotations,
        }, format => format.Kind is FileFormatKind.Pdf);

        // PDF output
        var pageSizeOption = AddFormatOption(new Option<PageSize>("--page-size")
        {
            Description = "The size of the pages of the images in a PDF output: 'fit' (the size of each image), A3, A4, A5, Letter, Legal, or a size such as 210x297mm, 8.5x11in or 595x842pt",
            DefaultValueFactory = _ => defaults.PageSize,
            CustomParser = result => Parse(result, PageSize.Parse),
        }, format => format.Kind is FileFormatKind.Pdf);
        var pageMarginOption = AddFormatOption(new Option<double>("--page-margin")
        {
            Description = "The margin around the images in a PDF output, in millimeters",
            DefaultValueFactory = _ => defaults.PageMargin,
        }, format => format.Kind is FileFormatKind.Pdf);
        pageMarginOption.Validators.Add(result =>
        {
            if (result.GetValue(pageMarginOption) is < 0 or > 500)
            {
                result.AddError("--page-margin must be between 0 and 500");
            }
        });
        var pageOrientationOption = AddFormatOption(new Option<PageOrientation>("--page-orientation")
        {
            Description = "The orientation of the pages of a fixed --page-size. Auto turns the page of a landscape image",
            DefaultValueFactory = _ => defaults.PageOrientation,
        }, format => format.Kind is FileFormatKind.Pdf);
        var pdfImageEncodingOption = AddFormatOption(new Option<PdfImageEncoding>("--pdf-image-encoding")
        {
            Description = "The compression of the images in a PDF output. Auto embeds the JPEG files as they are, and compresses the other images without loss",
            DefaultValueFactory = _ => defaults.PdfImageEncoding,
        }, format => format.Kind is FileFormatKind.Pdf);

        var pdfConformanceOption = AddFormatOption(new Option<PdfConformance>("--pdf-conformance")
        {
            Description = "Save the PDF output as PDF/A, for archiving. PdfA2B is the usual choice; the U levels also make the text extractable as Unicode, and PDF/A-3 allows embedded files of any format",
            DefaultValueFactory = _ => defaults.PdfConformance,
        }, format => format.Kind is FileFormatKind.Pdf);
        var pdfOutputIntentOption = AddFormatOption(new Option<FileInfo?>("--pdf-output-intent")
        {
            Description = "The ICC profile of the output intent of a PDF/A output, such as the CMYK profile of its printing condition. Default: sRGB",
        }, format => format.Kind is FileFormatKind.Pdf);
        var pdfMaxImageDpiOption = AddFormatOption(new Option<double?>("--pdf-max-image-dpi")
        {
            Description = "Resample the images of the PDF output whose resolution is above this one, in dots per inch, to make the file smaller",
        }, format => format.Kind is FileFormatKind.Pdf);
        pdfMaxImageDpiOption.Validators.Add(result =>
        {
            if (result.GetValue(pdfMaxImageDpiOption) is < 1 or > 2400)
            {
                result.AddError("--pdf-max-image-dpi must be between 1 and 2400");
            }
        });
        var pdfOptimizeOption = AddFormatOption(new Option<bool>("--pdf-optimize") { Description = "Remove the resources the pages do not use and the page thumbnails from the PDF output" }, format => format.Kind is FileFormatKind.Pdf);
        var pdfLinearizeOption = AddFormatOption(new Option<bool>("--pdf-linearize") { Description = "Linearize the PDF output (fast web view): a viewer shows the first pages before the file is downloaded" }, format => format.Kind is FileFormatKind.Pdf);
        var pdfOutputPasswordOption = AddFormatOption(new Option<string?>("--pdf-output-password") { Description = "Encrypt the PDF output (AES-256) with this password, which is needed to open it" }, format => format.Kind is FileFormatKind.Pdf);
        var pdfOwnerPasswordOption = AddFormatOption(new Option<string?>("--pdf-owner-password") { Description = "Encrypt the PDF output (AES-256) with this owner password, which allows every operation. Without --pdf-output-password, the file opens without a password, with the --pdf-permissions" }, format => format.Kind is FileFormatKind.Pdf);
        var pdfPermissionsOption = AddFormatOption(new Option<PdfPermissions?>("--pdf-permissions")
        {
            Description = "The operations allowed without the owner password, separated by commas: None, Print, PrintHighQuality, Modify, CopyContent, Annotate, FillForms, ExtractForAccessibility, Assemble, All. Needs --pdf-owner-password. Default: All",
            CustomParser = result => Parse(result, ParsePermissions),
        }, format => format.Kind is FileFormatKind.Pdf);
        var pdfFlattenFormsOption = AddFormatOption(new Option<bool>("--pdf-flatten-forms") { Description = "Draw the form fields of the PDF inputs in the content of their pages, so that their values are kept in the PDF output. Without it, the PDF output leaves the form fields out" }, format => format.Kind is FileFormatKind.Pdf);

        // EPUB input
        var epubModeOption = AddFormatOption(new Option<EpubMode>("--epub-mode")
        {
            Description = "How the EPUB pages are made. Auto uses the images of a book whose pages are images (comics), and lays out the text of the other books",
            DefaultValueFactory = _ => defaults.EpubMode,
        }, format => format.Kind is FileFormatKind.Epub);
        // Layout of the text of EPUB, MOBI, Markdown and HTML documents
        static bool IsLaidOut(FileFormat format) => format.Kind is FileFormatKind.Epub or FileFormatKind.Mobi or FileFormatKind.Markdown or FileFormatKind.Html;
        var layoutPageSizeOption = AddFormatOption(new Option<PageSize?>("--layout-page-size")
        {
            Description = "The size of the pages the text of EPUB, MOBI, Markdown and HTML documents is laid out on: A3, A4, A5, Letter, Legal, or a size such as 148x210mm. Default: A5 for EPUB and MOBI, A4 for Markdown and HTML",
            CustomParser = result => Parse(result, PageSize.ParseFixed),
        }, IsLaidOut);
        var layoutMarginOption = AddFormatOption(new Option<double>("--layout-margin")
        {
            Description = "The margin of the pages the text of EPUB, MOBI, Markdown and HTML documents is laid out on, in millimeters",
            DefaultValueFactory = _ => defaults.LayoutMargin,
        }, IsLaidOut);
        layoutMarginOption.Validators.Add(result =>
        {
            if (result.GetValue(layoutMarginOption) is < 0 or > 100)
            {
                result.AddError("--layout-margin must be between 0 and 100");
            }
        });
        var layoutFontSizeOption = AddFormatOption(new Option<double?>("--layout-font-size") { Description = "The size of the text of EPUB, MOBI, Markdown and HTML documents laid out on pages, in points. Default: the size of the style sheets of the document" }, IsLaidOut);
        layoutFontSizeOption.Validators.Add(result =>
        {
            if (result.GetValue(layoutFontSizeOption) is < 1 or > 200)
            {
                result.AddError("--layout-font-size must be between 1 and 200");
            }
        });
        var htmlRootOption = AddFormatOption(new Option<DirectoryInfo?>("--html-root")
        {
            Description = "The folder whose files (images, style sheets, fonts) Markdown and HTML documents laid out on pages can load, such as the root of a documentation site. Default: the folder of each document. Nothing is loaded from the network",
        }, format => format.Kind is FileFormatKind.Markdown or FileFormatKind.Html);

        // Markdown input and HTML output
        var markdownExtensionsOption = AddFormatOption(new Option<string>("--markdown-extensions")
        {
            Description = "The Markdown extensions of a Markdown input, separated by '+': 'common' for CommonMark only, 'advanced' (tables, task lists, footnotes, alerts...), or extensions such as 'pipetables+tasklists+emojis'",
            DefaultValueFactory = _ => defaults.MarkdownExtensions,
        }, format => format.Kind is FileFormatKind.Markdown);
        markdownExtensionsOption.Validators.Add(result =>
        {
            try
            {
                TextDocumentConverter.CreateOptions(result.GetValue(markdownExtensionsOption) ?? "", disableHtml: false);
            }
            catch (ArgumentException ex)
            {
                result.AddError($"--markdown-extensions is not valid: {ex.Message}");
            }
        });
        var markdownDisableHtmlOption = AddFormatOption(new Option<bool>("--markdown-disable-html") { Description = "Write the raw HTML of a Markdown input as text, for a document from an untrusted source" }, format => format.Kind is FileFormatKind.Markdown);
        var htmlFragmentOption = AddFormatOption(new Option<bool>("--html-fragment") { Description = "Write only the content of the body of the HTML output, without <html>, <head> and <body>" }, format => format.Kind is FileFormatKind.Html);
        var htmlTitleOption = AddFormatOption(new Option<string?>("--html-title") { Description = "The title of the HTML output. Default: the title of the front matter of the Markdown document, its first heading, or its file name" }, format => format.Kind is FileFormatKind.Html);
        var htmlStylesheetOption = AddFormatOption(new Option<string[]>("--html-stylesheet") { Description = "The URL of a style sheet the HTML output links to. Repeat the option for several style sheets" }, format => format.Kind is FileFormatKind.Html);

        // Markdown output
        var markdownHeadingStyleOption = AddFormatOption(new Option<HeadingStyle>("--markdown-heading-style") { Description = "The style of the headings of the Markdown output", DefaultValueFactory = _ => defaults.MarkdownOutput.HeadingStyle }, format => format.Kind is FileFormatKind.Markdown);
        var markdownCodeBlockStyleOption = AddFormatOption(new Option<CodeBlockStyle>("--markdown-code-block-style") { Description = "The style of the code blocks of the Markdown output", DefaultValueFactory = _ => defaults.MarkdownOutput.CodeBlockStyle }, format => format.Kind is FileFormatKind.Markdown);
        var markdownEmphasisMarkerOption = AddFormatOption(new Option<EmphasisMarker>("--markdown-emphasis-marker") { Description = "The marker of the emphasis of the Markdown output", DefaultValueFactory = _ => defaults.MarkdownOutput.EmphasisMarker }, format => format.Kind is FileFormatKind.Markdown);
        var markdownListMarkerOption = AddFormatOption(new Option<char>("--markdown-list-marker")
        {
            Description = "The marker of the unordered lists of the Markdown output: '-', '*' or '+'",
            DefaultValueFactory = _ => defaults.MarkdownOutput.UnorderedListMarker,
            CustomParser = result => Parse(result, ParseListMarker),
        }, format => format.Kind is FileFormatKind.Markdown);
        var markdownUnknownElementsOption = AddFormatOption(new Option<UnknownElementHandling>("--markdown-unknown-elements") { Description = "What the Markdown output does with the HTML elements Markdown cannot represent", DefaultValueFactory = _ => defaults.MarkdownOutput.UnknownElementHandling }, format => format.Kind is FileFormatKind.Markdown);
        var markdownComplexTablesOption = AddFormatOption(new Option<ComplexTableHandling>("--markdown-complex-tables") { Description = "What the Markdown output does with the tables a pipe table cannot represent", DefaultValueFactory = _ => defaults.MarkdownOutput.ComplexTableHandling }, format => format.Kind is FileFormatKind.Markdown);

        // Comic book archive output
        var comicImageFormatOption = AddFormatOption(new Option<ComicImageFormat>("--comic-image-format")
        {
            Description = "The format of the images of a comic book archive output (CBZ, CB7, CBT). Keep copies the JPEG, PNG, GIF and WebP images as they are, and writes the other pages as PNG",
            DefaultValueFactory = _ => defaults.ComicImageFormat,
        }, format => format.Kind is FileFormatKind.Comic && format.CanWrite);

        // Archives
        static bool IsArchive(FileFormat format) => format.Kind is FileFormatKind.Archive or FileFormatKind.Comic;
        var archivePasswordOption = AddFormatOption(new Option<string?>("--archive-password") { Description = "The password of the encrypted archive inputs (zip, 7z, RAR)" }, format => IsArchive(format) && format.ArchiveType is ArchiveType.Zip or ArchiveType.SevenZip or ArchiveType.Rar);
        var archiveCompressionLevelOption = AddFormatOption(new Option<CompressionLevel?>("--archive-compression-level")
        {
            Description = "The compression level of an archive output. Default: Optimal, and no compression for the images of a comic book archive",
        }, format => IsArchive(format) && format.CanWrite && format.ArchiveType is not ArchiveType.Tar);
        var zipMethodOption = AddFormatOption(new Option<ZipMethod?>("--zip-compression-method") { Description = "The compression method of the entries of a zip output. Default: Deflate. Zstandard needs .NET 11" }, format => IsArchive(format) && format.ArchiveType is ArchiveType.Zip);
        var zipPasswordOption = AddFormatOption(new Option<string?>("--zip-password") { Description = "Encrypt the entries of a zip output with this password" }, format => IsArchive(format) && format.ArchiveType is ArchiveType.Zip);
        var zipEncryptionOption = AddFormatOption(new Option<ZipEncryptionMethod>("--zip-encryption")
        {
            Description = "The encryption of the entries of a zip output, with --zip-password. ZipCrypto is weak, but older tools read only this one",
            DefaultValueFactory = _ => defaults.ZipEncryption,
        }, format => IsArchive(format) && format.ArchiveType is ArchiveType.Zip);
        var sevenZipMethodOption = AddFormatOption(new Option<SevenZipCompressionMethod?>("--7z-compression-method") { Description = "The compression method of a 7z output. Default: Lzma2" }, format => IsArchive(format) && format.ArchiveType is ArchiveType.SevenZip);
        var sevenZipSolidOption = AddFormatOption(new Option<bool>("--7z-solid")
        {
            Description = "Compress the files of a 7z output together, which makes it smaller. Use '--7z-solid false' to compress each file on its own. Default: true",
            DefaultValueFactory = _ => defaults.SevenZipSolid,
        }, format => IsArchive(format) && format.ArchiveType is ArchiveType.SevenZip);
        var sevenZipPasswordOption = AddFormatOption(new Option<string?>("--7z-password") { Description = "Encrypt the content of a 7z output with this password (AES-256)" }, format => IsArchive(format) && format.ArchiveType is ArchiveType.SevenZip);
        var sevenZipEncryptHeadersOption = AddFormatOption(new Option<bool>("--7z-encrypt-headers") { Description = "Also encrypt the names of the files of a 7z output, with --7z-password" }, format => IsArchive(format) && format.ArchiveType is ArchiveType.SevenZip);

        // Mailboxes
        var mboxFormatOption = AddFormatOption(new Option<MboxFormat>("--mbox-format")
        {
            Description = "The variant of the mbox files of an mbox input or output: how the lines of the messages that start with 'From ' are escaped. Mboxrd is the format of most mail tools",
            DefaultValueFactory = _ => defaults.MboxFormat,
        }, format => format == FileFormat.Mbox);
        var emailIncludeNonMailItemsOption = AddFormatOption(new Option<bool>("--email-include-non-mail-items")
        {
            Description = "Write the items of a PST or OST input that are not emails (contacts, appointments, tasks, notes...) as messages in an mbox, Maildir or EML output. Without it, they are skipped. A PST, OST or MSG output keeps them as they are",
        }, format => format == FileFormat.Pst || format == FileFormat.Ost || format == FileFormat.Mbox || format == FileFormat.Maildir || format == FileFormat.Eml);
        var pstFormatOption = AddFormatOption(new Option<PstFormat?>("--pst-format")
        {
            Description = "The format of a PST output: Unicode (Outlook 2003 and later) or Ansi (Outlook 97 to 2002, files up to 2 GB). Default: Unicode. An OST output has the Unicode4K format",
        }, format => format == FileFormat.Pst);
        var pstEncryptionOption = AddFormatOption(new Option<PstEncryption>("--pst-encryption")
        {
            Description = "How the data of a PST or OST output is obfuscated: None, Permute (the default of Outlook) or Cyclic. None of them needs a password, so they do not protect the data",
            DefaultValueFactory = _ => defaults.PstEncryption,
        }, format => format == FileFormat.Pst || format == FileFormat.Ost);

        var rootCommand = new RootCommand("Convert files from one format to another: images (PNG, JPEG, WebP, AVIF, HEIC, JPEG XL, TIFF, GIF...), PDF, EPUB, MOBI, AZW3, Markdown, HTML, comic book archives (CBZ, CBR, CB7, CBT), archives (zip, 7z, RAR, tar...), mailboxes (PST, OST, mbox, Maildir) and email messages (EML, MSG, EMLX, winmail.dat)");
        rootCommand.Arguments.Add(inputsArgument);
        Option[] options =
        [
            outputOption, inputFormatOption, outputFormatOption, overwriteOption, perInputOption, perPageOption, pagesOption, listFormatsOption,
            maxWidthOption, maxHeightOption, dpiOption,
            backgroundOption, jpegQualityOption, webpQualityOption, webpLosslessOption, avifQualityOption, avifEffortOption, avifLosslessOption,
            heicQualityOption, jpegXlQualityOption, jpegXlLosslessOption, pngCompressionLevelOption, tiffCompressionOption, gifMaxColorsOption,
            pdfPasswordOption, pdfRenderAnnotationsOption,
            pageSizeOption, pageMarginOption, pageOrientationOption, pdfImageEncodingOption,
            pdfConformanceOption, pdfOutputIntentOption, pdfMaxImageDpiOption, pdfOptimizeOption, pdfLinearizeOption,
            pdfOutputPasswordOption, pdfOwnerPasswordOption, pdfPermissionsOption, pdfFlattenFormsOption,
            epubModeOption, layoutPageSizeOption, layoutMarginOption, layoutFontSizeOption, htmlRootOption,
            markdownExtensionsOption, markdownDisableHtmlOption, htmlFragmentOption, htmlTitleOption, htmlStylesheetOption,
            markdownHeadingStyleOption, markdownCodeBlockStyleOption, markdownEmphasisMarkerOption, markdownListMarkerOption, markdownUnknownElementsOption, markdownComplexTablesOption,
            comicImageFormatOption,
            archivePasswordOption, archiveCompressionLevelOption, zipMethodOption, zipPasswordOption, zipEncryptionOption,
            sevenZipMethodOption, sevenZipSolidOption, sevenZipPasswordOption, sevenZipEncryptHeadersOption,
            mboxFormatOption, emailIncludeNonMailItemsOption, pstFormatOption, pstEncryptionOption,
        ];
        foreach (var option in options)
        {
            rootCommand.Options.Add(option);
        }

        rootCommand.SetAction((parseResult, cancellationToken) =>
        {
            var output = parseResult.InvocationConfiguration.Output;
            var error = parseResult.InvocationConfiguration.Error;
            if (parseResult.GetValue(listFormatsOption))
            {
                ListFormats(output, formatOptions);
                return Task.FromResult(0);
            }

            var settings = new ConversionSettings
            {
                Inputs = parseResult.GetValue(inputsArgument) ?? [],
                Output = parseResult.GetValue(outputOption),
                InputFormat = parseResult.GetValue(inputFormatOption),
                OutputFormat = parseResult.GetValue(outputFormatOption),
                Overwrite = parseResult.GetValue(overwriteOption),
                PerInput = parseResult.GetValue(perInputOption),
                PerPage = parseResult.GetValue(perPageOption),
                Pages = parseResult.GetValue(pagesOption) ?? PageRange.All,
                MaxWidth = parseResult.GetValue(maxWidthOption),
                MaxHeight = parseResult.GetValue(maxHeightOption),
                Background = ParseColor(parseResult.GetRequiredValue(backgroundOption)),
                JpegQuality = parseResult.GetValue(jpegQualityOption),
                WebPQuality = parseResult.GetValue(webpQualityOption),
                WebPLossless = parseResult.GetValue(webpLosslessOption),
                AvifQuality = parseResult.GetValue(avifQualityOption),
                AvifEffort = parseResult.GetValue(avifEffortOption),
                AvifLossless = parseResult.GetValue(avifLosslessOption),
                HeicQuality = parseResult.GetValue(heicQualityOption),
                JpegXlQuality = parseResult.GetValue(jpegXlQualityOption),
                JpegXlLossless = parseResult.GetValue(jpegXlLosslessOption),
                PngCompressionLevel = parseResult.GetValue(pngCompressionLevelOption),
                TiffCompression = parseResult.GetValue(tiffCompressionOption),
                GifMaxColors = parseResult.GetValue(gifMaxColorsOption),
                Dpi = parseResult.GetValue(dpiOption),
                PdfPassword = parseResult.GetValue(pdfPasswordOption),
                PdfRenderAnnotations = parseResult.GetValue(pdfRenderAnnotationsOption),
                PageSize = parseResult.GetValue(pageSizeOption) ?? PageSize.Fit,
                PageMargin = parseResult.GetValue(pageMarginOption),
                PageOrientation = parseResult.GetValue(pageOrientationOption),
                PdfImageEncoding = parseResult.GetValue(pdfImageEncodingOption),
                PdfConformance = parseResult.GetValue(pdfConformanceOption),
                PdfOutputIntent = parseResult.GetValue(pdfOutputIntentOption) is { } outputIntent ? FullPath.FromPath(outputIntent.FullName) : null,
                PdfMaxImageDpi = parseResult.GetValue(pdfMaxImageDpiOption),
                PdfOptimize = parseResult.GetValue(pdfOptimizeOption),
                PdfLinearize = parseResult.GetValue(pdfLinearizeOption),
                PdfOutputPassword = parseResult.GetValue(pdfOutputPasswordOption),
                PdfOwnerPassword = parseResult.GetValue(pdfOwnerPasswordOption),
                PdfPermissions = parseResult.GetValue(pdfPermissionsOption),
                PdfFlattenForms = parseResult.GetValue(pdfFlattenFormsOption),
                EpubMode = parseResult.GetValue(epubModeOption),
                LayoutPageSize = parseResult.GetValue(layoutPageSizeOption)?.Size,
                LayoutMargin = parseResult.GetValue(layoutMarginOption),
                LayoutFontSize = parseResult.GetValue(layoutFontSizeOption),
                HtmlRoot = parseResult.GetValue(htmlRootOption) is { } htmlRoot ? FullPath.FromPath(htmlRoot.FullName) : null,
                MarkdownExtensions = parseResult.GetRequiredValue(markdownExtensionsOption),
                MarkdownDisableHtml = parseResult.GetValue(markdownDisableHtmlOption),
                HtmlFragment = parseResult.GetValue(htmlFragmentOption),
                HtmlTitle = parseResult.GetValue(htmlTitleOption),
                HtmlStylesheets = parseResult.GetValue(htmlStylesheetOption) ?? [],
                MarkdownOutput = new HtmlToMarkdownOptions
                {
                    HeadingStyle = parseResult.GetValue(markdownHeadingStyleOption),
                    CodeBlockStyle = parseResult.GetValue(markdownCodeBlockStyleOption),
                    EmphasisMarker = parseResult.GetValue(markdownEmphasisMarkerOption),
                    UnorderedListMarker = parseResult.GetValue(markdownListMarkerOption),
                    UnknownElementHandling = parseResult.GetValue(markdownUnknownElementsOption),
                    ComplexTableHandling = parseResult.GetValue(markdownComplexTablesOption),
                },
                ComicImageFormat = parseResult.GetValue(comicImageFormatOption),
                ArchivePassword = parseResult.GetValue(archivePasswordOption),
                ArchiveCompressionLevel = parseResult.GetValue(archiveCompressionLevelOption),
                ZipMethod = parseResult.GetValue(zipMethodOption),
                ZipPassword = parseResult.GetValue(zipPasswordOption),
                ZipEncryption = parseResult.GetValue(zipEncryptionOption),
                SevenZipMethod = parseResult.GetValue(sevenZipMethodOption),
                SevenZipSolid = parseResult.GetValue(sevenZipSolidOption),
                SevenZipPassword = parseResult.GetValue(sevenZipPasswordOption),
                SevenZipEncryptHeaders = parseResult.GetValue(sevenZipEncryptHeadersOption),
                MboxFormat = parseResult.GetValue(mboxFormatOption),
                EmailIncludeNonMailItems = parseResult.GetValue(emailIncludeNonMailItemsOption),
                PstFileFormat = parseResult.GetValue(pstFormatOption),
                PstEncryption = parseResult.GetValue(pstEncryptionOption),
            };

            if (settings.Inputs.Count is 0)
            {
                error.WriteLine("No input file. Run with --help to see the usage");
                return Task.FromResult(1);
            }

            return Task.FromResult(Convert(settings, output, error, cancellationToken));
        });

        var invocationConfiguration = new InvocationConfiguration();
        configure?.Invoke(invocationConfiguration);
        return rootCommand.Parse(args).InvokeAsync(invocationConfiguration);
    }

    private static int Convert(ConversionSettings settings, TextWriter output, TextWriter error, CancellationToken cancellationToken)
    {
        try
        {
            new Converter(settings, output, error).Convert(cancellationToken);
            return 0;
        }
        catch (ConversionException ex)
        {
            error.WriteLine(ex.Message);
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or FormatException or ArgumentException or InvalidOperationException or NotSupportedException or UnauthorizedAccessException or ImageException or Pdf.PdfException or Email.EmailException)
        {
            // The errors of the files: invalid or unsupported content, files that cannot be read or written
            error.WriteLine(ex.Message);
        }

        return 1;
    }

    private static void ListFormats(TextWriter output, List<(Func<FileFormat, bool> AppliesTo, Option Option)> formatOptions)
    {
        foreach (var format in FileFormat.All)
        {
            var access = (format.CanRead, format.CanWrite) switch
            {
                (true, true) => "read, write",
                (true, false) => "read",
                _ => "write",
            };

            output.Write(format.Name);
            output.Write(" (");
            output.Write(string.Join(", ", format.Extensions));
            output.Write("): ");
            output.WriteLine(access);

            var names = formatOptions
                .Where(item => item.AppliesTo(format))
                .Select(item => item.Option.Name)
                .ToList();
            if (names.Count > 0)
            {
                output.Write("  Options: ");
                output.WriteLine(string.Join(", ", names));
            }
        }
    }

    private static FileFormat? ParseFormat(ArgumentResult result)
    {
        var value = result.Tokens.Count is 1 ? result.Tokens[0].Value : "";
        var format = FileFormat.FromName(value);
        if (format is null)
        {
            result.AddError($"The format '{value}' is not supported. Run with --list-formats to see the formats");
        }

        return format;
    }

    private static T Parse<T>(ArgumentResult result, Func<string, T> parse)
    {
        var value = result.Tokens.Count is 1 ? result.Tokens[0].Value : "";
        try
        {
            return parse(value);
        }
        catch (FormatException ex)
        {
            result.AddError(ex.Message);
            return default!;
        }
    }

    private static void Validate<T>(OptionResult result, Option<T> option, Func<T, object> parse)
    {
        if (result.GetValue(option) is not { } value)
            return;

        try
        {
            parse(value);
        }
        catch (FormatException ex)
        {
            result.AddError(ex.Message);
        }
    }

    /// <summary>Parses the names of <see cref="PdfPermissions"/> values, separated by commas.</summary>
    internal static PdfPermissions ParsePermissions(string value)
    {
        var result = PdfPermissions.None;
        foreach (var part in value.Split(',', StringSplitOptions.TrimEntries))
        {
            // Enum.TryParse accepts numbers: only the names are valid
            if (part.Length is 0 || !char.IsAsciiLetter(part[0]) || !Enum.TryParse<PdfPermissions>(part, ignoreCase: true, out var permission))
                throw new FormatException($"The PDF permission '{part}' is not valid. Use None, Print, PrintHighQuality, Modify, CopyContent, Annotate, FillForms, ExtractForAccessibility, Assemble or All, separated by commas");

            result |= permission;
        }

        return result;
    }

    private static char ParseListMarker(string value) => value is "-" or "*" or "+"
        ? value[0]
        : throw new FormatException($"The list marker '{value}' is not valid. Use '-', '*' or '+'");

    private static Rgba64 ParseColor(string value)
    {
        var hex = value.StartsWith('#', StringComparison.Ordinal) ? value.AsSpan(1) : value.AsSpan();
        if (hex.Length is not 6 || !uint.TryParse(hex, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out var rgb))
            throw new FormatException($"The color '{value}' is not valid. Use the hexadecimal notation, such as '#ffffff'");

        return new Rgba64(Expand((byte)(rgb >> 16)), Expand((byte)(rgb >> 8)), Expand((byte)rgb));

        static ushort Expand(byte value) => (ushort)(value * 257);
    }

    private static void ValidateRange(OptionResult result, Option<int?> option, int min, int max)
    {
        if (result.GetValue(option) is { } value && (value < min || value > max))
        {
            result.AddError(string.Create(CultureInfo.InvariantCulture, $"{option.Name} must be between {min} and {max}"));
        }
    }
}
