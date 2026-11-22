using System.IO.Compression;
using Meziantou.Framework.Email;
using Meziantou.Framework.Email.Mapi;
using Meziantou.Framework.Email.Storage;
using Meziantou.Framework.Imaging;
using Meziantou.Framework.Pdf;

namespace Meziantou.Framework.FileConversionTool.Tests;

public sealed class FileConversionToolTests(ITestOutputHelper testOutputHelper)
{
    private async Task<(int ExitCode, ConsoleHelper Console)> RunAsync(params string[] args)
    {
        var console = new ConsoleHelper(testOutputHelper);
        var exitCode = await Program.MainImpl(args, console.ConfigureConsole);
        return (exitCode, console);
    }

    private static async Task<FullPath> WriteFileAsync(TemporaryDirectory temp, string name, byte[] content)
    {
        var path = temp.GetFullPath(name);
        path.CreateParentDirectory();
        await File.WriteAllBytesAsync(path, content, XunitCancellationToken);
        return path;
    }

    private static ImageInfo Identify(FullPath path) => Image.Identify(File.ReadAllBytes(path));

    /// <summary>Reads the entries of an archive of any format: the content of the files as text, null for the directories, and "-> target" for the links.</summary>
    private static Dictionary<string, string?> ReadArchive(FullPath path, string? password = null)
    {
        using var archive = Compression.ArchiveFile.Open(path, new Compression.ArchiveReaderOptions { Password = password });
        var result = new Dictionary<string, string?>(StringComparer.Ordinal);
        foreach (var entry in archive.Entries)
        {
            if (entry.IsDirectory)
            {
                result.Add(entry.FullName.TrimEnd('/'), null);
            }
            else if (entry.LinkTarget is { } target)
            {
                result.Add(entry.FullName, "-> " + target);
            }
            else
            {
                using var stream = entry.Open();
                using var reader = new StreamReader(stream);
                result.Add(entry.FullName, reader.ReadToEnd());
            }
        }

        return result;
    }

    [Fact]
    public async Task PngToJpeg_NextToTheInput()
    {
        await using var temp = TemporaryDirectory.Create();
        var input = await WriteFileAsync(temp, "photo.png", TestFiles.CreateImage(30, 20, ImageFormat.Png));

        var (exitCode, console) = await RunAsync(input, "--output-format", "jpg");

        Assert.Equal(0, exitCode);
        Assert.Empty(console.Error);
        var info = Identify(temp.GetFullPath("photo.jpg"));
        Assert.Equal(ImageFormat.Jpeg, info.Format);
        Assert.Equal(new Size(30, 20), info.Size);
    }

    [Fact]
    public async Task JpegQuality_ChangesTheSize()
    {
        await using var temp = TemporaryDirectory.Create();
        using (var image = new Image<Rgba32>(64, 64))
        {
            for (var y = 0; y < 64; y++)
            {
                for (var x = 0; x < 64; x++)
                {
                    image.Frames[0][x, y] = new Rgba32((byte)(x * 4), (byte)(y * 4), (byte)((x * y) % 256));
                }
            }

            image.Save(temp.GetFullPath("gradient.png"));
        }

        var input = temp.GetFullPath("gradient.png");
        Assert.Equal(0, (await RunAsync(input, "-o", temp.GetFullPath("low.jpg"), "--jpeg-quality", "10")).ExitCode);
        Assert.Equal(0, (await RunAsync(input, "-o", temp.GetFullPath("high.jpg"), "--jpeg-quality", "100")).ExitCode);

        Assert.True(new FileInfo(temp.GetFullPath("low.jpg")).Length < new FileInfo(temp.GetFullPath("high.jpg")).Length);
    }

    [Fact]
    public async Task TransparentPngToJpeg_UsesTheBackground()
    {
        await using var temp = TemporaryDirectory.Create();
        var input = await WriteFileAsync(temp, "transparent.png", TestFiles.CreateImage(4, 4, ImageFormat.Png, new Rgba32(0, 0, 0, 0)));

        var (exitCode, _) = await RunAsync(input, "-o", temp.GetFullPath("out.jpg"), "--background", "#00ff00");

        Assert.Equal(0, exitCode);
        using var image = Image.Load(temp.GetFullPath("out.jpg"));
        using var rgba = image.CloneAs<Rgba32>();
        var pixel = rgba.Frames[0][1, 1];
        Assert.True(pixel.G > 200 && pixel.R < 50 && pixel.B < 50);
    }

    [Fact]
    public async Task PngToAvif_WithOptions()
    {
        await using var temp = TemporaryDirectory.Create();
        var input = await WriteFileAsync(temp, "image.png", TestFiles.CreateImage(16, 16, ImageFormat.Png));

        var (exitCode, _) = await RunAsync(input, "-o", "{dir}/converted/{name}.{ext}", "--output-format", "avif", "--avif-quality", "50", "--avif-effort", "0");

        Assert.Equal(0, exitCode);
        Assert.Equal(ImageFormat.Avif, Identify(temp.GetFullPath("converted/image.avif")).Format);
    }

    [Fact]
    public async Task MaxWidth_ShrinksKeepingTheAspectRatio()
    {
        await using var temp = TemporaryDirectory.Create();
        var input = await WriteFileAsync(temp, "large.png", TestFiles.CreateImage(200, 100, ImageFormat.Png));

        var (exitCode, _) = await RunAsync(input, "-o", temp.GetFullPath("small.webp"), "--max-width", "50");

        Assert.Equal(0, exitCode);
        Assert.Equal(new Size(50, 25), Identify(temp.GetFullPath("small.webp")).Size);
    }

    [Fact]
    public async Task PdfToPng_OneFilePerPage()
    {
        await using var temp = TemporaryDirectory.Create();
        var input = await WriteFileAsync(temp, "doc.pdf", TestFiles.CreatePdf(12));

        var (exitCode, console) = await RunAsync(input, "--output-format", "png", "--dpi", "36");

        Assert.Equal(0, exitCode);
        Assert.Empty(console.Error);
        Assert.True(File.Exists(temp.GetFullPath("doc-01.png")));
        Assert.True(File.Exists(temp.GetFullPath("doc-12.png")));

        // A5 at 36 DPI
        Assert.Equal(new Size(209, 297), Identify(temp.GetFullPath("doc-01.png")).Size);
    }

    [Fact]
    public async Task PdfToPng_TemplateAndPages()
    {
        await using var temp = TemporaryDirectory.Create();
        var input = await WriteFileAsync(temp, "doc.pdf", TestFiles.CreatePdf(5));

        var (exitCode, _) = await RunAsync(input, "-o", temp.GetFullPath("out/page-{page:000}-of-{count}.png"), "--pages", "2-3,5", "--dpi", "20");

        Assert.Equal(0, exitCode);
        var files = Directory.GetFiles(temp.GetFullPath("out")).Select(Path.GetFileName).Order(StringComparer.Ordinal).ToArray();
        Assert.Equal(["page-002-of-5.png", "page-003-of-5.png", "page-005-of-5.png"], files);
    }

    [Fact]
    public async Task ImagesToPdf_NaturalOrderAndJpegPassthrough()
    {
        await using var temp = TemporaryDirectory.Create();
        var jpeg = TestFiles.CreateImage(96, 48, ImageFormat.Jpeg);
        await WriteFileAsync(temp, "scans/page10.jpg", jpeg);
        await WriteFileAsync(temp, "scans/page2.png", TestFiles.CreateImage(48, 96, ImageFormat.Png));
        await WriteFileAsync(temp, "scans/page1.jpg", jpeg);

        var (exitCode, console) = await RunAsync(temp.GetFullPath("scans") + "/*.*", "-o", temp.GetFullPath("book.pdf"));

        Assert.Equal(0, exitCode);
        Assert.Empty(console.Error);
        var document = PdfDocument.Open(File.ReadAllBytes(temp.GetFullPath("book.pdf")));
        Assert.HasCount(3, document.Pages);

        // page1, page2, page10: the size of each page is the size of its image at 96 DPI
        Assert.Equal(72, document.Pages[0].MediaBox.Width, 0.01);
        Assert.Equal(36, document.Pages[1].MediaBox.Width, 0.01);
        Assert.Equal(72, document.Pages[2].MediaBox.Width, 0.01);

        // The JPEG file is embedded as it is
        var embedded = document.Pages[0].GetImages().Single();
        Assert.Equal(jpeg, embedded.EncodedData.ToArray());
    }

    [Fact]
    public async Task ImagesToPdf_FixedPageSize()
    {
        await using var temp = TemporaryDirectory.Create();
        var portrait = await WriteFileAsync(temp, "a.png", TestFiles.CreateImage(10, 20, ImageFormat.Png));
        var landscape = await WriteFileAsync(temp, "b.png", TestFiles.CreateImage(20, 10, ImageFormat.Png));

        var (exitCode, _) = await RunAsync(portrait, landscape, "-o", temp.GetFullPath("out.pdf"), "--page-size", "A4", "--page-margin", "10");

        Assert.Equal(0, exitCode);
        var document = PdfDocument.Open(File.ReadAllBytes(temp.GetFullPath("out.pdf")));
        Assert.Equal(PdfSize.A4.Width, document.Pages[0].MediaBox.Width, 0.01);
        Assert.Equal(PdfSize.A4.Height, document.Pages[1].MediaBox.Width, 0.01);
    }

    [Fact]
    public async Task PdfMerge_ImportsThePages()
    {
        await using var temp = TemporaryDirectory.Create();
        var first = await WriteFileAsync(temp, "a.pdf", TestFiles.CreatePdf(2));
        var second = await WriteFileAsync(temp, "b.pdf", TestFiles.CreatePdf(3));

        var (exitCode, _) = await RunAsync(first, second, "-o", temp.GetFullPath("merged.pdf"), "--pages", "1-2");

        Assert.Equal(0, exitCode);
        var document = PdfDocument.Open(File.ReadAllBytes(temp.GetFullPath("merged.pdf")));
        Assert.HasCount(4, document.Pages);
        Assert.Equal(PdfSize.A5.Width, document.Pages[0].MediaBox.Width, 0.01);
    }

    [Fact]
    public async Task PerPage_SplitsAPdf()
    {
        await using var temp = TemporaryDirectory.Create();
        var input = await WriteFileAsync(temp, "doc.pdf", TestFiles.CreatePdf(3));

        var (exitCode, console) = await RunAsync(input, "--output-format", "pdf", "--per-page", "--pages", "2-");

        Assert.Equal(0, exitCode);
        Assert.Empty(console.Error);
        Assert.False(File.Exists(temp.GetFullPath("doc-1.pdf")));
        Assert.HasCount(1, PdfDocument.Open(File.ReadAllBytes(temp.GetFullPath("doc-2.pdf"))).Pages);
        var last = PdfDocument.Open(File.ReadAllBytes(temp.GetFullPath("doc-3.pdf")));
        Assert.HasCount(1, last.Pages);

        // The third page of the input: its rectangle is 2 points wider than the one of the first page
        Assert.Contains("102 100 re", System.Text.Encoding.ASCII.GetString(last.Pages[0].GetContentData()));
    }

    [Fact]
    public async Task PdfConformance_SavesAPdfA()
    {
        await using var temp = TemporaryDirectory.Create();
        var image = await WriteFileAsync(temp, "a.png", TestFiles.CreateImage(20, 10, ImageFormat.Png));
        var pdf = await WriteFileAsync(temp, "b.pdf", TestFiles.CreatePdf(1));

        var (exitCode, console) = await RunAsync(image, pdf, "-o", temp.GetFullPath("out.pdf"), "--pdf-conformance", "PdfA2B", "--pdf-linearize");

        Assert.Equal(0, exitCode);
        Assert.Empty(console.Error);
        var bytes = File.ReadAllBytes(temp.GetFullPath("out.pdf"));
        var text = System.Text.Encoding.Latin1.GetString(bytes);
        Assert.Contains("pdfaid:part", text);
        Assert.Contains("/Linearized", text[..1024]);
        Assert.HasCount(2, PdfDocument.Open(bytes).Pages);
    }

    [Fact]
    public async Task PdfOutputPassword_EncryptsTheOutput()
    {
        await using var temp = TemporaryDirectory.Create();
        var input = await WriteFileAsync(temp, "doc.pdf", TestFiles.CreatePdf(1));

        var (exitCode, _) = await RunAsync(input, "-o", temp.GetFullPath("out.pdf"), "--pdf-output-password", "user", "--pdf-owner-password", "owner", "--pdf-permissions", "Print, CopyContent");

        Assert.Equal(0, exitCode);
        var bytes = File.ReadAllBytes(temp.GetFullPath("out.pdf"));
        Assert.Throws<PdfPasswordException>(() => PdfDocument.Open(bytes));
        var document = PdfDocument.Open(bytes, new PdfReadOptions { Password = "user" });
        Assert.True(document.IsEncrypted);
        Assert.Equal(PdfPermissions.Print | PdfPermissions.CopyContent, document.Permissions);
        Assert.Equal(PdfPermissions.All, PdfDocument.Open(bytes, new PdfReadOptions { Password = "owner" }).Permissions);
    }

    [Fact]
    public async Task PdfOwnerPassword_OpensWithoutAPassword()
    {
        await using var temp = TemporaryDirectory.Create();
        var input = await WriteFileAsync(temp, "doc.pdf", TestFiles.CreatePdf(1));

        var (exitCode, _) = await RunAsync(input, "-o", temp.GetFullPath("out.pdf"), "--pdf-owner-password", "owner", "--pdf-permissions", "none");

        Assert.Equal(0, exitCode);
        var document = PdfDocument.Open(File.ReadAllBytes(temp.GetFullPath("out.pdf")));
        Assert.True(document.IsEncrypted);
        Assert.Equal(PdfPermissions.None, document.Permissions);
    }

    [Fact]
    public async Task PdfMaxImageDpi_ResamplesTheImages()
    {
        await using var temp = TemporaryDirectory.Create();
        var input = await WriteFileAsync(temp, "a.png", TestFiles.CreateImage(400, 400, ImageFormat.Png));

        // 400 pixels on a page of 2 inches: 200 DPI
        var (exitCode, console) = await RunAsync(input, "-o", temp.GetFullPath("out.pdf"), "--page-size", "2x2in", "--pdf-max-image-dpi", "100", "--pdf-optimize");

        Assert.Equal(0, exitCode);
        Assert.Empty(console.Error);
        var image = PdfDocument.Open(File.ReadAllBytes(temp.GetFullPath("out.pdf"))).Pages[0].GetImages().Single();
        Assert.InRange(image.Width, 150, 250);
    }

    [Fact]
    public async Task PdfFlattenForms_KeepsTheValues()
    {
        await using var temp = TemporaryDirectory.Create();
        var input = await WriteFileAsync(temp, "form.pdf", TestFiles.CreatePdfForm("Hello form"));

        Assert.Equal(0, (await RunAsync(input, "-o", temp.GetFullPath("plain.pdf"))).ExitCode);
        Assert.Equal(0, (await RunAsync(input, "-o", temp.GetFullPath("flat.pdf"), "--pdf-flatten-forms")).ExitCode);

        Assert.DoesNotContain("Hello form", PdfDocument.Open(File.ReadAllBytes(temp.GetFullPath("plain.pdf"))).Pages[0].ExtractText());
        Assert.Contains("Hello form", PdfDocument.Open(File.ReadAllBytes(temp.GetFullPath("flat.pdf"))).Pages[0].ExtractText());
    }

    [Theory]
    [InlineData("--pdf-conformance PdfA2U --pdf-output-password x", "PDF/A does not allow encryption")]
    [InlineData("--pdf-linearize --pdf-owner-password x", "cannot be encrypted")]
    [InlineData("--pdf-output-password x --pdf-permissions Print", "--pdf-owner-password")]
    [InlineData("--pdf-output-intent missing.icc", "--pdf-conformance")]
    [InlineData("--pdf-owner-password x --pdf-permissions Print,4", "'4' is not valid")]
    public async Task Error_InvalidPdfOutputOptions(string options, string expectedError)
    {
        await using var temp = TemporaryDirectory.Create();
        var input = await WriteFileAsync(temp, "doc.pdf", TestFiles.CreatePdf(1));

        var (exitCode, console) = await RunAsync([input, "-o", temp.GetFullPath("out.pdf"), .. options.Split(' ')]);

        Assert.NotEqual(0, exitCode);
        Assert.Contains(expectedError, console.Error);
        Assert.False(File.Exists(temp.GetFullPath("out.pdf")));
    }

    [Theory]
    [InlineData("All", PdfPermissions.All)]
    [InlineData("none", PdfPermissions.None)]
    [InlineData("print,PrintHighQuality", PdfPermissions.Print | PdfPermissions.PrintHighQuality)]
    public void ParsePermissions(string value, PdfPermissions expected)
    {
        Assert.Equal(expected, Program.ParsePermissions(value));
    }

    [Fact]
    public async Task PerInput_OneOutputPerInput()
    {
        await using var temp = TemporaryDirectory.Create();
        var first = await WriteFileAsync(temp, "a.png", TestFiles.CreateImage(10, 10, ImageFormat.Png));
        var second = await WriteFileAsync(temp, "b.png", TestFiles.CreateImage(10, 10, ImageFormat.Png));

        var (exitCode, _) = await RunAsync(first, second, "--output-format", "pdf", "--per-input");

        Assert.Equal(0, exitCode);
        Assert.True(File.Exists(temp.GetFullPath("a.pdf")));
        Assert.True(File.Exists(temp.GetFullPath("b.pdf")));
    }

    [Fact]
    public async Task ImagesToTiff_MultiPage()
    {
        await using var temp = TemporaryDirectory.Create();
        var first = await WriteFileAsync(temp, "a.png", TestFiles.CreateImage(10, 10, ImageFormat.Png));
        var second = await WriteFileAsync(temp, "b.png", TestFiles.CreateImage(20, 10, ImageFormat.Png));

        Assert.Equal(0, (await RunAsync(first, second, "-o", temp.GetFullPath("pages.tiff"))).ExitCode);
        using (var collection = ImageCollection.Load(temp.GetFullPath("pages.tiff")))
        {
            Assert.HasCount(2, collection.Entries);
        }

        // Each page of a multi-page TIFF file is an image
        Assert.Equal(0, (await RunAsync(temp.GetFullPath("pages.tiff"), "--output-format", "png")).ExitCode);
        Assert.Equal(new Size(20, 10), Identify(temp.GetFullPath("pages-2.png")).Size);
    }

    [Fact]
    public async Task ImagesToCbz_AndBackToPdf()
    {
        await using var temp = TemporaryDirectory.Create();
        var jpeg = TestFiles.CreateImage(30, 40, ImageFormat.Jpeg);
        await WriteFileAsync(temp, "in/1.jpg", jpeg);
        await WriteFileAsync(temp, "in/2.bmp", TestFiles.CreateImage(30, 40, ImageFormat.Bmp));

        Assert.Equal(0, (await RunAsync(temp.GetFullPath("in") + "/*", "-o", temp.GetFullPath("comic.cbz"))).ExitCode);
        using (var archive = ZipFile.OpenRead(temp.GetFullPath("comic.cbz")))
        {
            Assert.Equal(["0001.jpg", "0002.png"], archive.Entries.Select(entry => entry.FullName).ToArray());

            // The JPEG file is copied as it is, the BMP file is written as PNG
            using var stream = archive.Entries[0].Open();
            using var copy = new MemoryStream();
            stream.CopyTo(copy);
            Assert.Equal(jpeg, copy.ToArray());
        }

        Assert.Equal(0, (await RunAsync(temp.GetFullPath("comic.cbz"), "--output-format", "pdf")).ExitCode);
        Assert.HasCount(2, PdfDocument.Open(File.ReadAllBytes(temp.GetFullPath("comic.pdf"))).Pages);
    }

    [Fact]
    public async Task ComicImageFormat_ConvertsThePages()
    {
        await using var temp = TemporaryDirectory.Create();
        var input = await WriteFileAsync(temp, "a.png", TestFiles.CreateImage(30, 40, ImageFormat.Png));

        Assert.Equal(0, (await RunAsync(input, "-o", temp.GetFullPath("a.cbz"), "--comic-image-format", "webp")).ExitCode);

        using var archive = ZipFile.OpenRead(temp.GetFullPath("a.cbz"));
        Assert.Equal("0001.webp", archive.Entries.Single().FullName);
    }

    [Fact]
    public async Task ImageEpubToCbz_CopiesTheImages()
    {
        await using var temp = TemporaryDirectory.Create();
        var input = await WriteFileAsync(temp, "comic.epub", TestFiles.CreateImageEpub());

        var (exitCode, console) = await RunAsync(input, "--output-format", "cbz");

        Assert.Equal(0, exitCode);
        Assert.Empty(console.Error);
        using var archive = ZipFile.OpenRead(temp.GetFullPath("comic.cbz"));
        Assert.Equal(["0001.png", "0002.png", "0003.png"], archive.Entries.Select(entry => entry.FullName).ToArray());
    }

    [Fact]
    public async Task ImageEpubToPdf_OnePagePerImage()
    {
        await using var temp = TemporaryDirectory.Create();
        var input = await WriteFileAsync(temp, "comic.epub", TestFiles.CreateImageEpub());

        Assert.Equal(0, (await RunAsync(input, "--output-format", "pdf")).ExitCode);

        var document = PdfDocument.Open(File.ReadAllBytes(temp.GetFullPath("comic.pdf")));
        Assert.HasCount(3, document.Pages);
        Assert.Equal("Test book", document.Information.Title);
        Assert.Equal("Test author", document.Information.Author);
    }

    [Fact]
    public async Task TextEpubToPdf_RendersTheText()
    {
        await using var temp = TemporaryDirectory.Create();
        var input = await WriteFileAsync(temp, "book.epub", TestFiles.CreateTextEpub());

        var (exitCode, console) = await RunAsync(input, "--output-format", "pdf", "--layout-page-size", "A6x", "--dpi", "50");
        Assert.Equal(1, exitCode);
        Assert.Contains("A6x", console.Error);

        (exitCode, console) = await RunAsync(input, "--output-format", "pdf", "--layout-font-size", "14");
        Assert.Equal(0, exitCode);
        Assert.Empty(console.Error);

        // Each document of the reading order starts a page
        var document = PdfDocument.Open(File.ReadAllBytes(temp.GetFullPath("book.pdf")));
        Assert.HasCount(2, document.Pages);
        Assert.Equal(PdfSize.A5.Width, document.Pages[0].MediaBox.Width, 0.5);
        Assert.Contains("Chapter one", document.Pages[0].ExtractText());
        Assert.Contains("second chapter", document.Pages[1].ExtractText());
    }

    [Fact]
    public async Task TextEpubToPng_RendersThePages()
    {
        await using var temp = TemporaryDirectory.Create();
        var input = await WriteFileAsync(temp, "book.epub", TestFiles.CreateTextEpub());

        var (exitCode, _) = await RunAsync(input, "--output-format", "png", "--dpi", "48");

        Assert.Equal(0, exitCode);

        // The number of pages is not known before the rendering: the numbers have 4 digits
        Assert.Equal(new Size(280, 397), Identify(temp.GetFullPath("book-0001.png")).Size);
        Assert.True(File.Exists(temp.GetFullPath("book-0002.png")));
    }

    [Fact]
    public async Task TextEpub_ImagesModeFails()
    {
        await using var temp = TemporaryDirectory.Create();
        var input = await WriteFileAsync(temp, "book.epub", TestFiles.CreateTextEpub());

        var (exitCode, console) = await RunAsync(input, "--output-format", "cbz", "--epub-mode", "images");

        Assert.Equal(1, exitCode);
        Assert.Contains("--epub-mode render", console.Error);
    }

    [Fact]
    public async Task Error_ExistingOutput()
    {
        await using var temp = TemporaryDirectory.Create();
        var input = await WriteFileAsync(temp, "a.png", TestFiles.CreateImage(10, 10, ImageFormat.Png));
        await WriteFileAsync(temp, "a.jpg", [1, 2, 3]);

        var (exitCode, console) = await RunAsync(input, "--output-format", "jpg");
        Assert.Equal(1, exitCode);
        Assert.Contains("--overwrite", console.Error);

        Assert.Equal(0, (await RunAsync(input, "--output-format", "jpg", "--overwrite")).ExitCode);
        Assert.Equal(ImageFormat.Jpeg, Identify(temp.GetFullPath("a.jpg")).Format);
    }

    [Fact]
    public async Task Error_SameOutputForSeveralPages()
    {
        await using var temp = TemporaryDirectory.Create();
        var input = await WriteFileAsync(temp, "doc.pdf", TestFiles.CreatePdf(2));

        var (exitCode, console) = await RunAsync(input, "-o", temp.GetFullPath("page.png"), "--dpi", "10");

        Assert.Equal(1, exitCode);
        Assert.Contains("{page}", console.Error);
    }

    [Fact]
    public async Task Error_SeveralInputsToOneFileWithoutOutput()
    {
        await using var temp = TemporaryDirectory.Create();
        var first = await WriteFileAsync(temp, "a.png", TestFiles.CreateImage(10, 10, ImageFormat.Png));
        var second = await WriteFileAsync(temp, "b.png", TestFiles.CreateImage(10, 10, ImageFormat.Png));

        var (exitCode, console) = await RunAsync(first, second, "--output-format", "pdf");

        Assert.Equal(1, exitCode);
        Assert.Contains("--per-input", console.Error);
    }

    [Fact]
    public async Task Error_UnknownFormat()
    {
        await using var temp = TemporaryDirectory.Create();
        var input = await WriteFileAsync(temp, "a.png", TestFiles.CreateImage(10, 10, ImageFormat.Png));

        var (exitCode, console) = await RunAsync(input, "--output-format", "docx");
        Assert.Equal(1, exitCode);
        Assert.Contains("--list-formats", console.Error);

        await WriteFileAsync(temp, "notes.txt", "hello"u8.ToArray());
        (exitCode, console) = await RunAsync(temp.GetFullPath("notes.txt"), "--output-format", "png");
        Assert.Equal(1, exitCode);
        Assert.Contains("is not supported", console.Error);
    }

    [Fact]
    public async Task Error_OutputIsTheInput()
    {
        await using var temp = TemporaryDirectory.Create();
        var input = await WriteFileAsync(temp, "a.png", TestFiles.CreateImage(10, 10, ImageFormat.Png));

        var (exitCode, console) = await RunAsync(input, "--output-format", "png", "--overwrite");

        Assert.Equal(1, exitCode);
        Assert.Contains("is the input file", console.Error);
    }

    [Fact]
    public async Task ListFormats()
    {
        var (exitCode, console) = await RunAsync("--list-formats");

        Assert.Equal(0, exitCode);
        Assert.Contains("Jpeg (.jpg, .jpeg): read, write", console.Output);
        Assert.Contains("--jpeg-quality", console.Output);
        Assert.Contains("EPUB (.epub): read", console.Output);
        Assert.Contains("--layout-page-size", console.Output);
        Assert.Contains("TarGz (.tar.gz, .tgz): read, write", console.Output);
        Assert.Contains("CBR (.cbr): read", console.Output);
        Assert.Contains("--7z-password", console.Output);
        Assert.Contains("Markdown (.md, .markdown): read, write", console.Output);
        Assert.Contains("HTML (.html, .htm): read, write", console.Output);
        Assert.Contains("--html-stylesheet", console.Output);
        Assert.Contains("PST (.pst): read, write", console.Output);
        Assert.Contains("OST (.ost): read, write", console.Output);
        Assert.Contains("Mbox (.mbox, .mbx): read, write", console.Output);
        Assert.Contains("MSG (.msg): read, write", console.Output);
        Assert.Contains("--pst-encryption", console.Output);
        Assert.Contains("--mbox-format", console.Output);
        Assert.Contains("--email-include-non-mail-items", console.Output);
    }

    [Fact]
    public async Task PstToMbox_WritesAThunderbirdFolder()
    {
        await using var temp = TemporaryDirectory.Create();
        var input = await WriteFileAsync(temp, "mail.pst", TestFiles.CreatePst());

        var (exitCode, console) = await RunAsync(input, "--output-format", "mbox");

        Assert.Equal(0, exitCode);
        var output = temp.GetFullPath("mail.mbox");
        Assert.Equal(output.Value, console.Output.Trim());
        Assert.Equal(["Welcome"], MboxReader.ReadMessages(output / "Inbox").Select(message => message.Message.Subject));
        Assert.Equal(["Project plan"], MboxReader.ReadMessages(output / "Inbox.sbd" / "Projects").Select(message => message.Message.Subject));

        // The contact is not an email
        Assert.Contains("skipped the items that are not emails", console.Error);
    }

    [Fact]
    public async Task MboxToOst_ToPst()
    {
        await using var temp = TemporaryDirectory.Create();
        var input = await WriteFileAsync(temp, "mail.mbox", TestFiles.CreateMbox("First", "Second"));

        Assert.Equal(0, (await RunAsync(input, "--output-format", "ost")).ExitCode);
        using (var ost = PstFile.Open(temp.GetFullPath("mail.ost")))
        {
            Assert.True(ost.IsOfflineFile);
            Assert.Equal(["First", "Second"], ost.GetFolder("mail")!.EnumerateItems().Select(item => item.Subject));
        }

        Assert.Equal(0, (await RunAsync(temp.GetFullPath("mail.ost"), "-o", temp.GetFullPath("copy.pst"), "--pst-encryption", "None")).ExitCode);
        using var pst = PstFile.Open(temp.GetFullPath("copy.pst"));
        Assert.False(pst.IsOfflineFile);
        Assert.Equal(["First", "Second"], pst.GetFolder("mail")!.EnumerateItems().Select(item => item.Subject));
    }

    [Theory]
    [InlineData("ost", "Unicode", "An OST file has the Unicode4K format")]
    [InlineData("pst", "Unicode4K", "use --output-format ost")]
    public async Task PstFormat_MustMatchTheOutput(string outputFormat, string pstFormat, string expectedError)
    {
        await using var temp = TemporaryDirectory.Create();
        var input = await WriteFileAsync(temp, "mail.mbox", TestFiles.CreateMbox("First"));

        var (exitCode, console) = await RunAsync(input, "--output-format", outputFormat, "--pst-format", pstFormat);

        Assert.Equal(1, exitCode);
        Assert.Contains(expectedError, console.Error);
        Assert.DoesNotContain(Directory.EnumerateFileSystemEntries(temp.FullPath), path => path != input.Value);
    }

    [Fact]
    public async Task MaildirFolderToPst()
    {
        await using var temp = TemporaryDirectory.Create();
        var maildir = MaildirFolder.Create(temp.GetFullPath("mail"));
        maildir.AddMessage(TestFiles.CreateEml("Read"), MaildirFlags.Seen);
        maildir.CreateFolder("Archive").AddMessage(TestFiles.CreateEml("Archived"));

        var (exitCode, _) = await RunAsync(temp.GetFullPath("mail"), "--output-format", "pst");

        Assert.Equal(0, exitCode);
        using var pst = PstFile.Open(temp.GetFullPath("mail.pst"));
        Assert.Equal([("Read", true)], pst.GetFolder("Inbox")!.EnumerateItems().Select(item => (item.Subject!, item.IsRead)));
        Assert.Equal([("Archived", false)], pst.GetFolder("Archive")!.EnumerateItems().Select(item => (item.Subject!, item.IsRead)));
    }

    [Fact]
    public async Task PstToMessageFiles()
    {
        await using var temp = TemporaryDirectory.Create();
        var input = await WriteFileAsync(temp, "mail.pst", TestFiles.CreatePst());

        Assert.Equal(0, (await RunAsync(input, "--output-format", "eml")).ExitCode);
        Assert.Equal("Welcome", MimeMessage.Load(temp.GetFullPath("mail.eml/Inbox/Welcome.eml")).Subject);
        Assert.Equal("Project plan", MimeMessage.Load(temp.GetFullPath("mail.eml/Inbox/Projects/Project plan.eml")).Subject);
        Assert.Empty(Directory.EnumerateFiles(temp.GetFullPath("mail.eml/Contacts")));

        // An .msg file keeps the contact
        Assert.Equal(0, (await RunAsync(input, "-o", temp.GetFullPath("items"), "--output-format", "msg")).ExitCode);
        Assert.Equal("Welcome", MsgFile.Load(temp.GetFullPath("items/Inbox/Welcome.msg")).Subject);
        Assert.IsType<MapiContact>(MsgFile.Load(Assert.Single(Directory.EnumerateFiles(temp.GetFullPath("items/Contacts")))));
    }

    [Fact]
    public async Task MailboxOutput_DoesNotReplaceAFolder()
    {
        await using var temp = TemporaryDirectory.Create();
        var input = await WriteFileAsync(temp, "mail.pst", TestFiles.CreatePst());
        Assert.Equal(0, (await RunAsync(input, "--output-format", "maildir")).ExitCode);

        var (exitCode, console) = await RunAsync(input, "--output-format", "maildir");
        Assert.Equal(1, exitCode);
        Assert.Contains("already exists", console.Error);

        (exitCode, console) = await RunAsync(input, "--output-format", "maildir", "--overwrite");
        Assert.Equal(1, exitCode);
        Assert.Contains("is not empty", console.Error);
        Assert.Equal(["Welcome"], MaildirFolder.Open(temp.GetFullPath("mail.maildir")).EnumerateMessages().Select(message => message.Load().Subject));

        // An empty folder is filled
        Directory.CreateDirectory(temp.GetFullPath("empty"));
        Assert.Equal(0, (await RunAsync(input, "-o", temp.GetFullPath("empty"), "--output-format", "maildir", "--overwrite")).ExitCode);
        Assert.Equal(MailboxFormat.Maildir, MailboxConverter.DetectFormat(temp.GetFullPath("empty")));

        // An Outlook data file is replaced
        Assert.Equal(0, (await RunAsync(temp.GetFullPath("mail.maildir"), "-o", temp.GetFullPath("copy.pst"))).ExitCode);
        Assert.Equal(0, (await RunAsync(temp.GetFullPath("mail.maildir"), "-o", temp.GetFullPath("copy.pst"), "--overwrite")).ExitCode);
        Assert.Empty(Directory.EnumerateFiles(temp.FullPath, "*.tmp"));
    }

    [Fact]
    public async Task MailboxOutput_InTheInputFolder()
    {
        await using var temp = TemporaryDirectory.Create();
        MaildirFolder.Create(temp.GetFullPath("mail")).AddMessage(TestFiles.CreateEml("Hello"));

        var (exitCode, console) = await RunAsync(temp.GetFullPath("mail"), "-o", temp.GetFullPath("mail/copy.pst"));

        Assert.Equal(1, exitCode);
        Assert.Contains("is in the input folder", console.Error);
    }

    [Fact]
    public async Task MessageFormats_RoundTrip()
    {
        await using var temp = TemporaryDirectory.Create();
        var eml = TestFiles.CreateEml("Hello");
        var input = await WriteFileAsync(temp, "hello.eml", eml);

        Assert.Equal(0, (await RunAsync(input, "--output-format", "msg")).ExitCode);
        var item = MsgFile.Load(temp.GetFullPath("hello.msg"));
        Assert.Equal("Hello", item.Subject);
        Assert.True(item.IsRead);

        Assert.Equal(0, (await RunAsync(temp.GetFullPath("hello.msg"), "-o", temp.GetFullPath("from-msg.eml"))).ExitCode);
        Assert.Equal("Hello", MimeMessage.Load(temp.GetFullPath("from-msg.eml")).Subject);

        Assert.Equal(0, (await RunAsync(temp.GetFullPath("hello.msg"), "--output-format", "oft")).ExitCode);
        Assert.True(MsgFile.IsTemplate(await File.ReadAllBytesAsync(temp.GetFullPath("hello.oft"), XunitCancellationToken)));

        // The bytes of an email message are kept through an Apple Mail message
        Assert.Equal(0, (await RunAsync(input, "--output-format", "emlx")).ExitCode);
        Assert.True(EmlxFile.Load(temp.GetFullPath("hello.emlx")).Flags.HasFlag(EmlxFlags.Read));
        Assert.Equal(0, (await RunAsync(temp.GetFullPath("hello.emlx"), "-o", temp.GetFullPath("from-emlx.eml"))).ExitCode);
        Assert.Equal(eml, await File.ReadAllBytesAsync(temp.GetFullPath("from-emlx.eml"), XunitCancellationToken));
    }

    [Fact]
    public async Task WinmailDat_DetectedFromItsContent()
    {
        await using var temp = TemporaryDirectory.Create();
        var input = await WriteFileAsync(temp, "winmail.dat", TestFiles.CreateTnef("Forwarded"));

        Assert.Equal(0, (await RunAsync(input, "--output-format", "eml")).ExitCode);

        Assert.Equal("Forwarded", MimeMessage.Load(temp.GetFullPath("winmail.eml")).Subject);
    }

    [Fact]
    public async Task MessagesToOneMbox()
    {
        await using var temp = TemporaryDirectory.Create();
        await WriteFileAsync(temp, "inbox/message10.eml", TestFiles.CreateEml("Ten"));
        await WriteFileAsync(temp, "inbox/message2.eml", TestFiles.CreateEml("Two"));
        await WriteFileAsync(temp, "inbox/message1.eml", TestFiles.CreateEml("One"));

        var (exitCode, console) = await RunAsync(temp.GetFullPath("inbox") + "/*.eml", "-o", temp.GetFullPath("all.mbox"));

        Assert.Equal(0, exitCode);
        Assert.Equal(temp.GetFullPath("all.mbox").Value, console.Output.Trim());

        // One mbox file, with the messages in the natural order of the names, as they are (the lines of an mbox file end with LF)
        var messages = MboxReader.ReadMessages(temp.GetFullPath("all.mbox")).ToList();
        Assert.Equal(["One", "Two", "Ten"], messages.Select(message => message.Message.Subject));
        Assert.Equal(Encoding.UTF8.GetString(TestFiles.CreateEml("One")).ReplaceLineEndings("\n"), Encoding.UTF8.GetString(messages[0].Content.Span));
        Assert.Equal("From alice@example.com Thu Jan 15 09:00:00 2026", messages[0].FromLine);

        // The mbox file is a mailbox of one folder
        Assert.Equal(0, (await RunAsync(temp.GetFullPath("all.mbox"), "--output-format", "maildir")).ExitCode);
        Assert.HasCount(3, MaildirFolder.Open(temp.GetFullPath("all.maildir")).EnumerateFolders().Single().Folder.EnumerateMessages());
    }

    [Fact]
    public async Task MessagesToOneMbox_NeedsAnOutput()
    {
        await using var temp = TemporaryDirectory.Create();
        var first = await WriteFileAsync(temp, "a.eml", TestFiles.CreateEml("A"));
        var second = await WriteFileAsync(temp, "b.eml", TestFiles.CreateEml("B"));

        var (exitCode, console) = await RunAsync(first, second, "--output-format", "mbox");
        Assert.Equal(1, exitCode);
        Assert.Contains("set its path with --output", console.Error);

        Assert.Equal(0, (await RunAsync(first, second, "--output-format", "mbox", "--per-input")).ExitCode);
        Assert.Equal("A", MboxReader.ReadMessages(temp.GetFullPath("a.mbox")).Single().Message.Subject);
        Assert.Equal("B", MboxReader.ReadMessages(temp.GetFullPath("b.mbox")).Single().Message.Subject);
    }

    [Fact]
    public async Task MessagesToPstAndMaildir()
    {
        await using var temp = TemporaryDirectory.Create();
        var eml = await WriteFileAsync(temp, "a.eml", TestFiles.CreateEml("From EML"));
        var msg = await WriteFileAsync(temp, "b.msg", TestFiles.CreateMsg("From MSG"));

        Assert.Equal(0, (await RunAsync(eml, msg, "-o", temp.GetFullPath("mail.pst"))).ExitCode);
        using (var pst = PstFile.Open(temp.GetFullPath("mail.pst")))
        {
            Assert.Equal(["From EML", "From MSG"], pst.GetFolder("Inbox")!.EnumerateItems().Select(item => item.Subject));
        }

        Assert.Equal(0, (await RunAsync(eml, msg, "-o", temp.GetFullPath("mail"), "--output-format", "maildir")).ExitCode);
        Assert.Equal(["From EML", "From MSG"], MaildirFolder.Open(temp.GetFullPath("mail")).EnumerateMessages().Select(message => message.Load().Subject).Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task MessagesToOneMbox_SkipsTheInvalidMessages()
    {
        await using var temp = TemporaryDirectory.Create();
        var valid = await WriteFileAsync(temp, "valid.eml", TestFiles.CreateEml("Valid"));
        var invalid = await WriteFileAsync(temp, "invalid.msg", "not an Outlook item"u8.ToArray());

        var (exitCode, console) = await RunAsync(valid, invalid, "-o", temp.GetFullPath("all.mbox"));

        Assert.Equal(0, exitCode);
        Assert.Contains(invalid.Value + ": ", console.Error);
        Assert.Equal(["Valid"], MboxReader.ReadMessages(temp.GetFullPath("all.mbox")).Select(message => message.Message.Subject));

        (exitCode, console) = await RunAsync(invalid, "-o", temp.GetFullPath("none.mbox"));
        Assert.Equal(1, exitCode);
        Assert.Contains("No message can be converted", console.Error);
        Assert.False(File.Exists(temp.GetFullPath("none.mbox")));
    }

    [Fact]
    public async Task MessagesAndMailboxToMailbox_IsRejected()
    {
        await using var temp = TemporaryDirectory.Create();
        var message = await WriteFileAsync(temp, "hello.eml", TestFiles.CreateEml("Hello"));
        var mailbox = await WriteFileAsync(temp, "mail.pst", TestFiles.CreatePst());

        var (exitCode, console) = await RunAsync(message, mailbox, "-o", temp.GetFullPath("all.mbox"));

        Assert.Equal(1, exitCode);
        Assert.Contains("convert the mailboxes and the messages separately", console.Error);
    }

    [Fact]
    public async Task FolderToPdf_IsRejected()
    {
        await using var temp = TemporaryDirectory.Create();
        Directory.CreateDirectory(temp.GetFullPath("folder"));

        var (exitCode, console) = await RunAsync(temp.GetFullPath("folder"), "--output-format", "pdf");

        Assert.Equal(1, exitCode);
        Assert.Contains("is a folder", console.Error);
    }

    [Theory]
    [InlineData("PST")]
    [InlineData("MSG")]
    [InlineData("TNEF")]
    [InlineData("EML")]
    [InlineData("Mbox")]
    public void DetectFormat_Email(string expected)
    {
        var content = expected switch
        {
            "PST" => TestFiles.CreatePst(),
            "MSG" => TestFiles.CreateMsg("Hello"),
            "TNEF" => TestFiles.CreateTnef("Hello"),
            "EML" => TestFiles.CreateEml("Hello"),
            _ => TestFiles.CreateMbox("Hello"),
        };
        using var stream = new MemoryStream(content);
        Assert.Equal(expected, Converter.DetectFormat(stream, "file")?.Name);
        Assert.Equal(0, stream.Position);
    }

    [Fact]
    public void DetectFormat_MarkdownThatLooksLikeAMessage()
    {
        using var stream = new MemoryStream("Subject: notes\r\n\r\nSome notes"u8.ToArray());
        Assert.Equal("Markdown", Converter.DetectFormat(stream, "notes.md")?.Name);
    }

    [Theory]
    [InlineData("readme.md", "BM is also the start of a BMP file", "Markdown")]
    [InlineData("notes.markdown", "%PDF- is the start of a PDF file", "Markdown")]
    [InlineData("page.htm", "<p>Hello</p>", "HTML")]
    [InlineData("page", "\uFEFF  <!DOCTYPE html><html></html>", "HTML")]
    [InlineData("page", "<HTML lang=en>", "HTML")]
    [InlineData("page", "<p>Hello</p>", null)]
    public void DetectFormat_TextDocuments(string fileName, string content, string? expected)
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(System.Text.RegularExpressions.Regex.Unescape(content)));
        Assert.Equal(expected, Converter.DetectFormat(stream, fileName)?.Name);
    }

    [Fact]
    public async Task MarkdownToHtml_CompleteDocument()
    {
        await using var temp = TemporaryDirectory.Create();
        var input = await WriteFileAsync(temp, "docs/readme.md", Encoding.UTF8.GetBytes("""
            ---
            title: "Front & title"
            ---
            # Heading

            | a | b |
            |---|---|
            | 1 | 2 |

            - [x] done
            """));

        var (exitCode, console) = await RunAsync(input, "--output-format", "html", "--html-stylesheet", "site.css", "--html-stylesheet", "print.css?a=1&b=2");

        Assert.Equal(0, exitCode);
        Assert.Empty(console.Error);
        var html = File.ReadAllText(temp.GetFullPath("docs/readme.html"));
        Assert.StartsWith("<!DOCTYPE html>\n<html>\n<head>\n<meta charset=\"utf-8\">", html);
        Assert.Contains("<title>Front &amp; title</title>", html);
        Assert.Contains("<link rel=\"stylesheet\" href=\"site.css\">", html);
        Assert.Contains("<link rel=\"stylesheet\" href=\"print.css?a=1&amp;b=2\">", html);
        Assert.Contains("<table>", html);
        Assert.Contains("type=\"checkbox\"", html);
        Assert.DoesNotContain("title:", html);
        Assert.EndsWith("</body>\n</html>\n", html);
    }

    [Theory]
    [InlineData("# Tom & *Jerry*\n\nText", null, "Tom &amp; Jerry")]
    [InlineData("Title\n=====\n\nText", null, "Title")]
    [InlineData("Text only", null, "notes")]
    [InlineData("# Heading", "Custom <title>", "Custom &lt;title&gt;")]
    public async Task MarkdownToHtml_Title(string markdown, string? title, string expected)
    {
        await using var temp = TemporaryDirectory.Create();
        var input = await WriteFileAsync(temp, "notes.md", Encoding.UTF8.GetBytes(System.Text.RegularExpressions.Regex.Unescape(markdown)));

        string[] args = title is null ? [input, "-o", temp.GetFullPath("out.html")] : [input, "-o", temp.GetFullPath("out.html"), "--html-title", title];
        Assert.Equal(0, (await RunAsync(args)).ExitCode);

        Assert.Contains("<title>" + expected + "</title>", File.ReadAllText(temp.GetFullPath("out.html")));
    }

    [Fact]
    public async Task MarkdownToHtml_FragmentAndOptions()
    {
        await using var temp = TemporaryDirectory.Create();
        var input = await WriteFileAsync(temp, "a.md", Encoding.UTF8.GetBytes("<script>alert(1)</script>\n\n| a | b |\n|---|---|\n| 1 | 2 |\n"));

        var (exitCode, _) = await RunAsync(input, "-o", temp.GetFullPath("a.html"), "--html-fragment", "--markdown-disable-html", "--markdown-extensions", "common");

        Assert.Equal(0, exitCode);
        var html = File.ReadAllText(temp.GetFullPath("a.html"));
        Assert.StartsWith("<p>&lt;script&gt;", html);
        Assert.DoesNotContain("<table>", html);
        Assert.DoesNotContain("<html>", html);
    }

    [Fact]
    public async Task MarkdownToPdf_LaysOutTheText()
    {
        await using var temp = TemporaryDirectory.Create();
        await WriteFileAsync(temp, "docs/images/chart.png", TestFiles.CreateImage(40, 20, ImageFormat.Png));
        var input = await WriteFileAsync(temp, "docs/report.md", Encoding.UTF8.GetBytes("# Quarterly report\n\nHello **world**\n\n![chart](images/chart.png)\n"));

        var (exitCode, console) = await RunAsync(input, "--output-format", "pdf", "--layout-margin", "10");

        Assert.Equal(0, exitCode);
        Assert.Empty(console.Error);
        var document = PdfDocument.Open(File.ReadAllBytes(temp.GetFullPath("docs/report.pdf")));
        Assert.Equal(PdfSize.A4.Width, document.Pages[0].MediaBox.Width, 0.5);
        Assert.Equal("Quarterly report", document.Information.Title);
        var text = document.Pages[0].ExtractText();
        Assert.Contains("Quarterly report", text);
        Assert.Contains("Hello world", text);

        // The image of the folder of the document is loaded
        Assert.HasCount(1, document.Pages[0].GetImages());
    }

    [Fact]
    public async Task HtmlToPng_RendersThePages()
    {
        await using var temp = TemporaryDirectory.Create();
        var input = await WriteFileAsync(temp, "page.html", Encoding.UTF8.GetBytes("<!DOCTYPE html><html><head><title>Page</title><style>p { color: red }</style></head><body><p>Hello</p></body></html>"));

        var (exitCode, console) = await RunAsync(input, "--output-format", "png", "--layout-page-size", "100x50mm", "--dpi", "50");

        Assert.Equal(0, exitCode);
        Assert.Empty(console.Error);

        // The number of pages is known once the document is laid out: the number is padded to 4 digits
        // 100 millimeters at 50 DPI
        var info = Identify(temp.GetFullPath("page-0001.png"));
        Assert.InRange(info.Size.Width, 195, 199);
        Assert.InRange(info.Size.Height, 97, 100);
    }

    [Fact]
    public async Task HtmlRoot_LoadsTheFilesOfTheRoot()
    {
        await using var temp = TemporaryDirectory.Create();
        await WriteFileAsync(temp, "site/logo.png", TestFiles.CreateImage(40, 20, ImageFormat.Png));
        var input = await WriteFileAsync(temp, "site/pages/page.html", Encoding.UTF8.GetBytes("<p>Text</p><img src=\"../logo.png\">"));

        // The files outside of the folder of the document are not loaded by default
        Assert.Equal(0, (await RunAsync(input, "-o", temp.GetFullPath("default.pdf"))).ExitCode);
        Assert.Empty(PdfDocument.Open(File.ReadAllBytes(temp.GetFullPath("default.pdf"))).Pages[0].GetImages());

        Assert.Equal(0, (await RunAsync(input, "-o", temp.GetFullPath("root.pdf"), "--html-root", temp.GetFullPath("site"))).ExitCode);
        Assert.HasCount(1, PdfDocument.Open(File.ReadAllBytes(temp.GetFullPath("root.pdf"))).Pages[0].GetImages());

        var (exitCode, console) = await RunAsync(input, "-o", temp.GetFullPath("error.pdf"), "--html-root", temp.GetFullPath("other"));
        Assert.Equal(1, exitCode);
        Assert.Contains("--html-root", console.Error);
    }

    [Fact]
    public async Task HtmlRoot_DoesNotFollowLinksOutOfTheRoot()
    {
        await using var temp = TemporaryDirectory.Create();
        await WriteFileAsync(temp, "private/secret.png", TestFiles.CreateImage(40, 20, ImageFormat.Png));
        var input = await WriteFileAsync(temp, "site/page.html", Encoding.UTF8.GetBytes("<p>Text</p><img src=\"assets/secret.png\"><img src=\"secret-file.png\">"));
        Directory.CreateSymbolicLink(temp.GetFullPath("site/assets"), temp.GetFullPath("private"));
        File.CreateSymbolicLink(temp.GetFullPath("site/secret-file.png"), temp.GetFullPath("private/secret.png"));

        Assert.Equal(0, (await RunAsync(input, "-o", temp.GetFullPath("out.pdf"))).ExitCode);

        Assert.Empty(PdfDocument.Open(File.ReadAllBytes(temp.GetFullPath("out.pdf"))).Pages[0].GetImages());
    }

    [Fact]
    public async Task HtmlToPdf_TitleUsesTheEncodingOfTheDocument()
    {
        await using var temp = TemporaryDirectory.Create();
        var html = "<!DOCTYPE html><html><head><meta charset=\"windows-1252\"><title>Caf\u00e9 cr\u00e8me</title></head><body><p>Text</p></body></html>";
        var input = await WriteFileAsync(temp, "page.html", Encoding.Latin1.GetBytes(html));

        Assert.Equal(0, (await RunAsync(input, "--output-format", "pdf")).ExitCode);

        Assert.Equal("Caf\u00e9 cr\u00e8me", PdfDocument.Open(File.ReadAllBytes(temp.GetFullPath("page.pdf"))).Information.Title);
    }

    [Theory]
    [InlineData("https://local.invalid/a/b.png", "a/b.png")]
    [InlineData("https://local.invalid/a/./b.css?v=1#top", "a/b.css")]
    [InlineData("https://local.invalid/a/../b%20c.png", "b c.png")]
    [InlineData("https://local.invalid/../secret.txt", null)]
    [InlineData("https://local.invalid/a/../../secret.txt", null)]
    [InlineData("https://local.invalid/%2e%2e/secret.txt", null)]
    [InlineData("https://local.invalid/a%2F..%2F..%2Fsecret.txt", null)]
    [InlineData("https://local.invalid/C:%5Csecret.txt", null)]
    [InlineData("https://local.invalid/", null)]
    [InlineData("https://example.com/a.png", null)]
    [InlineData("file:///etc/passwd", null)]
    public void LocalFileDocumentLoader_ResolvesThePathsOfTheRoot(string url, string? expected)
    {
        var root = FullPath.FromPath("/root/site");
        var resolved = Layout.LocalFileDocumentLoader.TryResolvePath(root, url, out var path);

        Assert.Equal(expected is not null, resolved);
        if (expected is not null)
        {
            Assert.Equal(root / expected, path);
        }
    }

    [Fact]
    public async Task PdfPassword_RemovesTheEncryption()
    {
        await using var temp = TemporaryDirectory.Create();
        var input = await WriteFileAsync(temp, "doc.pdf", TestFiles.CreatePdf(2));
        Assert.Equal(0, (await RunAsync(input, "-o", temp.GetFullPath("protected.pdf"), "--pdf-output-password", "secret", "--pdf-owner-password", "owner", "--pdf-permissions", "Print")).ExitCode);

        var (exitCode, console) = await RunAsync(temp.GetFullPath("protected.pdf"), "-o", temp.GetFullPath("plain.pdf"));
        Assert.Equal(1, exitCode);
        Assert.NotEmpty(console.Error);

        Assert.Equal(0, (await RunAsync(temp.GetFullPath("protected.pdf"), "-o", temp.GetFullPath("plain.pdf"), "--pdf-password", "secret")).ExitCode);
        var document = PdfDocument.Open(File.ReadAllBytes(temp.GetFullPath("plain.pdf")));
        Assert.False(document.IsEncrypted);
        Assert.Equal(PdfPermissions.All, document.Permissions);
        Assert.HasCount(2, document.Pages);
    }

    [Fact]
    public async Task ArchivePassword_RemovesTheEncryption()
    {
        await using var temp = TemporaryDirectory.Create();
        var input = await WriteFileAsync(temp, "data.zip", TestFiles.CreateZip(("a.txt", "Secret"u8.ToArray())));
        Assert.Equal(0, (await RunAsync(input, "-o", temp.GetFullPath("protected.7z"), "--7z-password", "pass", "--7z-encrypt-headers")).ExitCode);

        Assert.Equal(0, (await RunAsync(temp.GetFullPath("protected.7z"), "-o", temp.GetFullPath("plain.zip"), "--archive-password", "pass")).ExitCode);
        Assert.Equal("Secret", ReadArchive(temp.GetFullPath("plain.zip"))["a.txt"]);
    }

    [Fact]
    public async Task HtmlToMarkdown_ConvertsTheBody()
    {
        await using var temp = TemporaryDirectory.Create();
        var first = await WriteFileAsync(temp, "pages/a.html", Encoding.UTF8.GetBytes("<!DOCTYPE html><html><head><title>Head title</title></head><body><h1>Hello</h1><p>Some <b>bold</b> text</p><ul><li>item</li></ul></body></html>"));
        await WriteFileAsync(temp, "pages/b.htm", Encoding.UTF8.GetBytes("<p>Fragment</p>"));

        var (exitCode, console) = await RunAsync(temp.GetFullPath("pages") + "/*.htm*", "-o", temp.GetFullPath("out") + "/{name}.{ext}", "--output-format", "md", "--markdown-heading-style", "Setext", "--markdown-list-marker", "*");

        Assert.Equal(0, exitCode);
        Assert.Empty(console.Error);
        Assert.Equal("Hello\n=====\n\nSome **bold** text\n\n* item\n", File.ReadAllText(temp.GetFullPath("out/a.md")));
        Assert.Equal("Fragment\n", File.ReadAllText(temp.GetFullPath("out/b.md")));
        Assert.True(File.Exists(first));
    }

    [Theory]
    [InlineData("doc.md", "zip", "cannot be converted to an archive")]
    [InlineData("doc.html", "7z", "cannot be converted to an archive")]
    [InlineData("doc.md", "md", "only HTML documents can")]
    [InlineData("doc.png", "html", "only Markdown documents can")]
    public async Task Error_TextDocumentConversions(string inputName, string outputFormat, string expectedError)
    {
        await using var temp = TemporaryDirectory.Create();
        var content = inputName.EndsWith(".png", StringComparison.Ordinal) ? TestFiles.CreateImage(1, 1, ImageFormat.Png) : Encoding.UTF8.GetBytes("# Title");
        var input = await WriteFileAsync(temp, inputName, content);

        var (exitCode, console) = await RunAsync(input, "--output-format", outputFormat, "-o", temp.GetFullPath("out/{name}.{ext}"));

        Assert.Equal(1, exitCode);
        Assert.Contains(expectedError, console.Error);
    }

    [Theory]
    [InlineData("--markdown-extensions", "advanced+unknown")]
    [InlineData("--markdown-list-marker", "#")]
    public async Task Error_InvalidMarkdownOptions(string option, string value)
    {
        await using var temp = TemporaryDirectory.Create();
        var input = await WriteFileAsync(temp, "doc.md", Encoding.UTF8.GetBytes("# Title"));

        var (exitCode, console) = await RunAsync(input, "--output-format", "html", option, value);

        Assert.NotEqual(0, exitCode);
        Assert.Contains(option, console.Error + (value == "#" ? "--markdown-list-marker" : ""));
        Assert.False(File.Exists(temp.GetFullPath("doc.html")));
    }

    [Theory]
    [InlineData("{name}-{page}.{ext}", "doc-07.png")]
    [InlineData("{name}-{page:0000}.{ext}", "doc-0007.png")]
    [InlineData("{index:0}_{count}", "3_12")]
    [InlineData("{{literal}}-{name}", "{literal}-doc")]
    [InlineData("{dir}/x", "/in/x")]
    public void OutputNameTemplate_Format(string template, string expected)
    {
        var values = new OutputNameValues("doc", "png", "/in", Page: 7, PageCount: 12, Index: 3, IndexCount: 20);

        Assert.Equal(expected, OutputNameTemplate.Parse(template).Format(values));
    }

    [Theory]
    [InlineData("{page")]
    [InlineData("page}")]
    [InlineData("{unknown}")]
    [InlineData("{page:abc}")]
    [InlineData("{name:00}")]
    public void OutputNameTemplate_Invalid(string template)
    {
        Assert.Throws<FormatException>(() => OutputNameTemplate.Parse(template));
    }

    [Theory]
    [InlineData("1-3,7,10-", new[] { 1, 2, 3, 7, 10, 11 }, new[] { 4, 6, 8, 9 })]
    [InlineData("5", new[] { 5 }, new[] { 4, 6 })]
    public void PageRange_Contains(string value, int[] included, int[] excluded)
    {
        var range = PageRange.Parse(value);

        Assert.All(included, page => Assert.True(range.Contains(page)));
        Assert.All(excluded, page => Assert.False(range.Contains(page)));
    }

    [Theory]
    [InlineData("")]
    [InlineData("0")]
    [InlineData("3-1")]
    [InlineData("a")]
    [InlineData("1,,2")]
    public void PageRange_Invalid(string value)
    {
        Assert.Throws<FormatException>(() => PageRange.Parse(value));
    }

    [Fact]
    public void NaturalStringComparer_SortsNumbers()
    {
        string[] names = ["page10.jpg", "Page2.jpg", "page1.jpg", "page01.jpg", "cover.jpg"];

        Assert.Equal(["cover.jpg", "page1.jpg", "page01.jpg", "Page2.jpg", "page10.jpg"], names.Order(NaturalStringComparer.Instance).ToArray());
    }

    [Theory]
    [InlineData("OEBPS/content.opf", "text/ch1.xhtml", "OEBPS/text/ch1.xhtml")]
    [InlineData("OEBPS/text/ch1.xhtml", "../images/a%20b.png#frag", "OEBPS/images/a b.png")]
    [InlineData("content.opf", "./a.xhtml", "a.xhtml")]
    [InlineData("OEBPS/content.opf", "../../escape.xhtml", null)]
    [InlineData("OEBPS/content.opf", "/absolute.xhtml", null)]
    [InlineData("OEBPS/content.opf", "https://example.com/a.png", null)]
    public void EpubPackage_ResolvePath(string basePath, string reference, string? expected)
    {
        Assert.Equal(expected, Epub.EpubPackage.ResolvePath(basePath, reference));
    }

    [Theory]
    [InlineData("tiny.png", "PNG")]
    [InlineData("doc.bin", "PDF")]
    [InlineData("book.bin", "EPUB")]
    [InlineData("archive.zip", "Zip")]
    [InlineData("archive.bin", "Zip")]
    [InlineData("comic.cbz", "CBZ")]
    [InlineData("comic.cb7", "CB7")]
    [InlineData("comic.cbt", "CBT")]
    [InlineData("comic.cbr", "CBR")]
    [InlineData("data.bin", "7z")]
    [InlineData("data.dat", "RAR")]
    [InlineData("data.tar", "Tar")]
    [InlineData("data.tgz", "TarGz")]
    [InlineData("renamed.cbz", "CBR")]
    public void DetectFormat(string fileName, string expected)
    {
        (string, byte[])[] entries = [("a.png", TestFiles.CreateImage(1, 1, ImageFormat.Png))];
        var content = (expected, Path.GetExtension(fileName)) switch
        {
            ("PNG", _) => TestFiles.CreateImage(1, 1, ImageFormat.Png),
            ("PDF", _) => TestFiles.CreatePdf(1),
            ("EPUB", _) => TestFiles.CreateImageEpub(),
            ("7z" or "CB7", _) => TestFiles.CreateSevenZip(entries),
            ("RAR" or "CBR", _) => TestFiles.CreateRar(entries),
            ("Tar" or "CBT", _) => TestFiles.CreateTar(gzip: false, entries),
            ("TarGz", _) => TestFiles.CreateTar(gzip: true, entries),
            _ => TestFiles.CreateZip(entries),
        };

        using var stream = new MemoryStream(content);
        Assert.Equal(expected, Converter.DetectFormat(stream, fileName)?.Name, StringComparer.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("book.tar.gz", "book", "TarGz")]
    [InlineData("book.TGZ", "book", "TarGz")]
    [InlineData("book.tar", "book", "Tar")]
    [InlineData("my.book.cb7", "my.book", "CB7")]
    [InlineData("notes.txt", "notes", null)]
    public void FileFormat_FromFileName(string fileName, string expectedName, string? expectedFormat)
    {
        Assert.Equal(expectedFormat, FileFormat.FromFileName(fileName)?.Name);
        Assert.Equal(expectedName, FileFormat.GetNameWithoutExtension(fileName));
    }

    [Theory]
    [InlineData("zip")]
    [InlineData("7z")]
    [InlineData("tar")]
    [InlineData("tar.gz")]
    [InlineData("tar.bz2")]
    [InlineData("tar.xz")]
    [InlineData("tar.lzma")]
    [InlineData("tar.lz")]
    public async Task Archive_CopiesTheEntries(string extension)
    {
        await using var temp = TemporaryDirectory.Create();
        var input = await WriteFileAsync(temp, "data.zip", TestFiles.CreateZip(("docs/", []), ("docs/readme.txt", "Hello"u8.ToArray()), ("a.txt", "Content of a"u8.ToArray())));

        var (exitCode, console) = await RunAsync(input, "--output-format", extension, "-o", temp.GetFullPath("out/{name}.{ext}"));

        Assert.Equal(0, exitCode);
        Assert.Empty(console.Error);
        var output = temp.GetFullPath("out/data." + extension);
        Assert.Equal(output.Value, console.Output.Trim());
        Assert.Equal(new Dictionary<string, string?>(StringComparer.Ordinal) { ["docs"] = null, ["docs/readme.txt"] = "Hello", ["a.txt"] = "Content of a" }, ReadArchive(output));
    }

    [Fact]
    public async Task Archive_KeepsTheTimesAndTheLinks()
    {
        await using var temp = TemporaryDirectory.Create();
        var input = await WriteFileAsync(temp, "data.tar.gz", TestFiles.CreateTar(gzip: true, ("a.txt", "A"u8.ToArray()), ("link -> a.txt", [])));

        Assert.Equal(0, (await RunAsync(input, "-o", temp.GetFullPath("{name}.zip"))).ExitCode);

        Assert.Equal(new Dictionary<string, string?>(StringComparer.Ordinal) { ["a.txt"] = "A", ["link"] = "-> a.txt" }, ReadArchive(temp.GetFullPath("data.zip")));
        using var archive = Compression.ArchiveFile.Open(temp.GetFullPath("data.zip"));
        Assert.Equal(new DateTimeOffset(2024, 5, 6, 7, 8, 10, TimeSpan.Zero), archive.GetEntry("a.txt")!.LastWriteTime?.ToUniversalTime());
    }

    [Fact]
    public async Task Archive_SkipsTheLinksTheOutputCannotStore()
    {
        await using var temp = TemporaryDirectory.Create();
        var input = await WriteFileAsync(temp, "data.tar", TestFiles.CreateTar(gzip: false, ("a.txt", "A"u8.ToArray()), ("link -> a.txt", [])));

        var (exitCode, console) = await RunAsync(input, "--output-format", "7z");

        Assert.Equal(0, exitCode);
        Assert.Contains("The symbolic link 'link' is skipped", console.Error);
        Assert.Equal(new Dictionary<string, string?>(StringComparer.Ordinal) { ["a.txt"] = "A" }, ReadArchive(temp.GetFullPath("data.7z")));
    }

    [Fact]
    public async Task Archive_KeepsTheModeOfTheFolders()
    {
        await using var temp = TemporaryDirectory.Create();
        var input = await WriteFileAsync(temp, "data.tar", TestFiles.CreateTar(gzip: false, ("dir/", []), ("dir/a.txt", "A"u8.ToArray())));

        foreach (var extension in new[] { "zip", "7z", "tar.gz" })
        {
            Assert.Equal(0, (await RunAsync(input, "-o", temp.GetFullPath("out/{name}." + extension))).ExitCode);
            using var archive = Compression.ArchiveFile.Open(temp.GetFullPath("out/data." + extension));
            var directory = archive.Entries.Single(entry => entry.IsDirectory);
            Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute, directory.UnixFileMode);
        }
    }

    [Fact]
    public async Task Output_KeepsAnExistingTemporaryFile()
    {
        await using var temp = TemporaryDirectory.Create();
        var input = await WriteFileAsync(temp, "a.png", TestFiles.CreateImage(10, 10, ImageFormat.Png));
        await WriteFileAsync(temp, "a.pdf.tmp", "user data"u8.ToArray());
        await WriteFileAsync(temp, "a.zip.tmp", "user data"u8.ToArray());

        Assert.Equal(0, (await RunAsync(input, "--output-format", "pdf")).ExitCode);
        Assert.Equal(0, (await RunAsync(temp.GetFullPath("a.pdf"), "-o", temp.GetFullPath("a.cbz"))).ExitCode);

        Assert.Equal("user data", File.ReadAllText(temp.GetFullPath("a.pdf.tmp")));
        Assert.Equal("user data", File.ReadAllText(temp.GetFullPath("a.zip.tmp")));
        Assert.Equal(["a.cbz", "a.pdf", "a.pdf.tmp", "a.png", "a.zip.tmp"], Directory.GetFiles(temp.FullPath).Select(Path.GetFileName).Order(StringComparer.Ordinal).ToArray());
    }

    [Fact]
    public async Task Output_KeepsThePermissionsOfTheReplacedFile()
    {
        // Windows has no Unix permissions
        if (OperatingSystem.IsWindows())
            return;

        await using var temp = TemporaryDirectory.Create();
        var input = await WriteFileAsync(temp, "a.png", TestFiles.CreateImage(10, 10, ImageFormat.Png));
        var output = await WriteFileAsync(temp, "a.pdf", "private"u8.ToArray());
        File.SetUnixFileMode(output, UnixFileMode.UserRead | UnixFileMode.UserWrite);

        Assert.Equal(0, (await RunAsync(input, "--output-format", "pdf", "--overwrite")).ExitCode);

        Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, File.GetUnixFileMode(output));
        Assert.HasCount(1, PdfDocument.Open(File.ReadAllBytes(output)).Pages);
    }

    [Fact]
    public async Task Output_LongFileName()
    {
        await using var temp = TemporaryDirectory.Create();
        var input = await WriteFileAsync(temp, "a.png", TestFiles.CreateImage(10, 10, ImageFormat.Png));

        // 250 characters: the temporary file must not add the name of the output to its own name
        var name = new string('n', 246) + ".pdf";
        Assert.Equal(0, (await RunAsync(input, "-o", temp.GetFullPath(name))).ExitCode);
        Assert.True(File.Exists(temp.GetFullPath(name)));
    }

    [Fact]
    public async Task Output_LinkToTheInputIsRejected()
    {
        await using var temp = TemporaryDirectory.Create();
        var content = TestFiles.CreateImage(10, 10, ImageFormat.Png);
        var input = await WriteFileAsync(temp, "in/a.png", content);
        File.CreateSymbolicLink(temp.GetFullPath("out.jpg"), input);
        Directory.CreateSymbolicLink(temp.GetFullPath("linked"), temp.GetFullPath("in"));

        // The link of the output file, and the link of a parent folder of the output file
        var (exitCode, console) = await RunAsync(input, "-o", temp.GetFullPath("out.jpg"), "--overwrite");
        Assert.Equal(1, exitCode);
        Assert.Contains("is the input file", console.Error);

        (exitCode, console) = await RunAsync(input, "-o", temp.GetFullPath("linked/a.png"), "--overwrite");
        Assert.Equal(1, exitCode);
        Assert.Contains("is the input file", console.Error);

        Assert.Equal(content, File.ReadAllBytes(input));
    }

    [Fact]
    public async Task Output_ReplacesALinkInsteadOfItsTarget()
    {
        await using var temp = TemporaryDirectory.Create();
        var input = await WriteFileAsync(temp, "a.png", TestFiles.CreateImage(10, 10, ImageFormat.Png));
        var other = await WriteFileAsync(temp, "other.txt", "other file"u8.ToArray());
        File.CreateSymbolicLink(temp.GetFullPath("a.jpg"), other);

        Assert.Equal(0, (await RunAsync(input, "--output-format", "jpg", "--overwrite")).ExitCode);

        Assert.Equal("other file", File.ReadAllText(other));
        Assert.Null(new FileInfo(temp.GetFullPath("a.jpg")).LinkTarget);
        Assert.Equal(ImageFormat.Jpeg, Identify(temp.GetFullPath("a.jpg")).Format);
    }

    [Fact]
    public async Task ImagesToPdf_AppliesTheOrientation()
    {
        await using var temp = TemporaryDirectory.Create();

        // Stored as 40x20, displayed as 20x40
        var input = await WriteFileAsync(temp, "photo.jpg", TestFiles.CreateOrientedJpeg(40, 20, Imaging.Metadata.ExifOrientation.RightTop));
        Assert.Equal(Imaging.Metadata.ExifOrientation.RightTop, Identify(input).Metadata.Orientation);

        Assert.Equal(0, (await RunAsync(input, "--output-format", "pdf")).ExitCode);

        var page = PdfDocument.Open(File.ReadAllBytes(temp.GetFullPath("photo.pdf"))).Pages[0];
        Assert.Equal(15, page.MediaBox.Width, 0.01);
        Assert.Equal(30, page.MediaBox.Height, 0.01);
        var image = page.GetImages().Single();
        Assert.Equal(20, image.Width);
        Assert.Equal(40, image.Height);
    }

    [Fact]
    public async Task ComicImageFormat_EncoderOptionsEncodeTheImagesAgain()
    {
        await using var temp = TemporaryDirectory.Create();
        var jpeg = TestFiles.CreateImage(64, 64, ImageFormat.Jpeg);
        var input = await WriteFileAsync(temp, "a.jpg", jpeg);

        Assert.Equal(0, (await RunAsync(input, "-o", temp.GetFullPath("copy.cbz"), "--comic-image-format", "jpeg")).ExitCode);
        Assert.Equal(0, (await RunAsync(input, "-o", temp.GetFullPath("low.cbz"), "--comic-image-format", "jpeg", "--jpeg-quality", "5")).ExitCode);

        static byte[] ReadPage(FullPath path)
        {
            using var archive = new System.IO.Compression.ZipArchive(File.OpenRead(path));
            using var stream = archive.Entries.Single().Open();
            using var memory = new MemoryStream();
            stream.CopyTo(memory);
            return memory.ToArray();
        }

        Assert.Equal(jpeg, ReadPage(temp.GetFullPath("copy.cbz")));
        Assert.NotEqual(jpeg, ReadPage(temp.GetFullPath("low.cbz")));
    }

    [Fact]
    public async Task SvgEpubToPdf_LaysOutTheSvgPages()
    {
        await using var temp = TemporaryDirectory.Create();
        const string Svg = """<svg xmlns="http://www.w3.org/2000/svg" width="100" height="100"><rect width="100" height="50" fill="red"/></svg>""";
        var svgPage = await WriteFileAsync(temp, "svg-page.epub", TestFiles.CreateEpub([("page.svg", Svg)]));
        var svgImage = await WriteFileAsync(temp, "svg-image.epub", TestFiles.CreateEpub([("page.xhtml", TestFiles.CreateXhtml("""<img src="image.svg" alt=""/>"""))], [("image.svg", Encoding.UTF8.GetBytes(Svg))]));

        var (exitCode, console) = await RunAsync(svgPage, svgImage, "--output-format", "pdf", "--per-input");

        Assert.Equal(0, exitCode);
        Assert.Empty(console.Error);
        Assert.NotEmpty(PdfDocument.Open(File.ReadAllBytes(temp.GetFullPath("svg-page.pdf"))).Pages);
        Assert.NotEmpty(PdfDocument.Open(File.ReadAllBytes(temp.GetFullPath("svg-image.pdf"))).Pages);
    }

    [Fact]
    public async Task RarToZip()
    {
        await using var temp = TemporaryDirectory.Create();
        var input = await WriteFileAsync(temp, "data.rar", TestFiles.CreateRar(("a.txt", "A"u8.ToArray()), ("dir/b.txt", "B"u8.ToArray())));

        Assert.Equal(0, (await RunAsync(input, "--output-format", "zip")).ExitCode);

        Assert.Equal(new Dictionary<string, string?>(StringComparer.Ordinal) { ["a.txt"] = "A", ["dir/b.txt"] = "B" }, ReadArchive(temp.GetFullPath("data.zip")));
    }

    [Fact]
    public async Task Archive_WithPassword()
    {
        await using var temp = TemporaryDirectory.Create();
        var input = await WriteFileAsync(temp, "data.zip", TestFiles.CreateZip(("a.txt", "Secret"u8.ToArray())));

        Assert.Equal(0, (await RunAsync(input, "-o", temp.GetFullPath("encrypted.zip"), "--zip-password", "pass")).ExitCode);
        Assert.Throws<Compression.ArchivePasswordException>(() => ReadArchive(temp.GetFullPath("encrypted.zip")));
        Assert.Equal("Secret", ReadArchive(temp.GetFullPath("encrypted.zip"), password: "pass")["a.txt"]);

        // The password of the input is needed to read it
        var (exitCode, console) = await RunAsync(temp.GetFullPath("encrypted.zip"), "--output-format", "7z");
        Assert.Equal(1, exitCode);
        Assert.NotEmpty(console.Error);

        Assert.Equal(0, (await RunAsync(temp.GetFullPath("encrypted.zip"), "--output-format", "7z", "--archive-password", "pass", "--7z-password", "other", "--7z-encrypt-headers")).ExitCode);
        Assert.Throws<Compression.ArchivePasswordException>(() => ReadArchive(temp.GetFullPath("encrypted.7z")));
        Assert.Equal("Secret", ReadArchive(temp.GetFullPath("encrypted.7z"), password: "other")["a.txt"]);
    }

    [Fact]
    public async Task Archive_FromAFileThatIsNotAnArchive()
    {
        await using var temp = TemporaryDirectory.Create();
        var input = await WriteFileAsync(temp, "doc.pdf", TestFiles.CreatePdf(1));

        var (exitCode, console) = await RunAsync(input, "--output-format", "zip");

        Assert.Equal(1, exitCode);
        Assert.Contains("cannot be converted to an archive", console.Error);
    }

    [Fact]
    public async Task CbrToPdfAndCbz()
    {
        await using var temp = TemporaryDirectory.Create();
        var jpeg = TestFiles.CreateImage(30, 40, ImageFormat.Jpeg);
        var input = await WriteFileAsync(temp, "comic.cbr", TestFiles.CreateRar(("page10.jpg", jpeg), ("page2.png", TestFiles.CreateImage(30, 40, ImageFormat.Png)), ("ComicInfo.xml", "<ComicInfo/>"u8.ToArray())));

        Assert.Equal(0, (await RunAsync(input, "--output-format", "pdf")).ExitCode);
        Assert.HasCount(2, PdfDocument.Open(File.ReadAllBytes(temp.GetFullPath("comic.pdf"))).Pages);

        Assert.Equal(0, (await RunAsync(input, "--output-format", "cbz")).ExitCode);
        using var archive = ZipFile.OpenRead(temp.GetFullPath("comic.cbz"));
        Assert.Equal(["0001.png", "0002.jpg"], archive.Entries.Select(entry => entry.FullName).ToArray());
    }

    [Theory]
    [InlineData("cb7", Compression.ArchiveFormat.SevenZip)]
    [InlineData("cbt", Compression.ArchiveFormat.Tar)]
    [InlineData("cbz", Compression.ArchiveFormat.Zip)]
    public async Task ImagesToComic(string extension, Compression.ArchiveFormat expectedFormat)
    {
        await using var temp = TemporaryDirectory.Create();
        var jpeg = TestFiles.CreateImage(30, 40, ImageFormat.Jpeg);
        await WriteFileAsync(temp, "in/1.jpg", jpeg);
        await WriteFileAsync(temp, "in/2.jpg", jpeg);

        Assert.Equal(0, (await RunAsync(temp.GetFullPath("in") + "/*", "-o", temp.GetFullPath("comic." + extension))).ExitCode);

        var output = temp.GetFullPath("comic." + extension);
        Assert.Equal(expectedFormat, Compression.ArchiveFile.DetectFormat(output).Format);
        using (var archive = Compression.ArchiveFile.Open(output))
        {
            Assert.Equal(["0001.jpg", "0002.jpg"], archive.Entries.Select(entry => entry.FullName).ToArray());
        }

        // The comic book archive converts back to images
        Assert.Equal(0, (await RunAsync(output, "-o", temp.GetFullPath("out/{page}.png"))).ExitCode);
        Assert.True(File.Exists(temp.GetFullPath("out/1.png")));
        Assert.True(File.Exists(temp.GetFullPath("out/2.png")));
    }

    [Fact]
    public async Task ComicToArchive_CopiesTheEntries()
    {
        await using var temp = TemporaryDirectory.Create();
        var input = await WriteFileAsync(temp, "comic.cbr", TestFiles.CreateRar(("1.png", TestFiles.CreateImage(2, 2, ImageFormat.Png)), ("ComicInfo.xml", "<ComicInfo/>"u8.ToArray())));

        Assert.Equal(0, (await RunAsync(input, "--output-format", "zip")).ExitCode);

        Assert.Equal(["1.png", "ComicInfo.xml"], ReadArchive(temp.GetFullPath("comic.zip")).Keys.ToArray());
    }
}
