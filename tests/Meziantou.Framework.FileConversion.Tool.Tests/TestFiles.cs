using System.Buffers.Binary;
using System.Formats.Tar;
using System.IO.Compression;
using System.IO.Hashing;
using Meziantou.Framework.Compression;
using Meziantou.Framework.Email.Mapi;
using Meziantou.Framework.Email.Storage;
using Meziantou.Framework.Imaging;
using Meziantou.Framework.Imaging.Formats;
using Meziantou.Framework.Pdf;

namespace Meziantou.Framework.FileConversionTool.Tests;

/// <summary>Creates the input files of the tests.</summary>
internal static class TestFiles
{
    public static byte[] CreateImage(int width, int height, ImageFormat format, Rgba32? color = null)
    {
        using var image = new Image<Rgba32>(width, height, color ?? new Rgba32(200, 30, 30));
        using var stream = new MemoryStream();
        image.Save(stream, ImageEncoder.Create(format));
        return stream.ToArray();
    }

    /// <summary>Creates a JPEG file whose stored pixels must be turned to be displayed: <paramref name="width"/> x <paramref name="height"/> stored, displayed with the orientation.</summary>
    public static byte[] CreateOrientedJpeg(int width, int height, Imaging.Metadata.ExifOrientation orientation)
    {
        using var image = new Image<Rgba32>(width, height, new Rgba32(200, 30, 30));
        image.Metadata.Orientation = orientation;
        using var stream = new MemoryStream();
        image.Save(stream, ImageEncoder.Create(ImageFormat.Jpeg));
        return stream.ToArray();
    }

    public static byte[] CreatePdf(int pageCount)
    {
        var document = new PdfDocument();
        for (var i = 0; i < pageCount; i++)
        {
            document.Pages.Add(PdfSize.A5).CreateContentBuilder()
                .SetFillRgb(0, 0, 1)
                .Rectangle(20, 20, 100 + i, 100)
                .Fill();
        }

        return document.ToByteArray();
    }

    public static byte[] CreatePdfForm(string value)
    {
        var document = new PdfDocument();
        var page = document.Pages.Add(PdfSize.A5);
        document.Form.AddTextField(page, "name", new PdfRectangle(20, 300, 300, 330), new PdfTextFieldOptions { Value = value });
        return document.ToByteArray();
    }

    public static byte[] CreateZip(params (string Name, byte[] Content)[] entries)
    {
        using var stream = new MemoryStream();
        using (var archive = new System.IO.Compression.ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var (name, content) in entries)
            {
                // The mimetype entry of an EPUB book is stored first, without compression
                var entry = archive.CreateEntry(name, name is "mimetype" ? CompressionLevel.NoCompression : CompressionLevel.Optimal);
                using var entryStream = entry.Open();
                entryStream.Write(content);
            }
        }

        return stream.ToArray();
    }

    public static byte[] CreateSevenZip(params (string Name, byte[] Content)[] entries)
    {
        using var stream = new MemoryStream();
        using (var archive = new SevenZipArchive(stream, SevenZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var (name, content) in entries)
            {
                var entry = archive.CreateEntry(name);
                if (!name.EndsWith('/', StringComparison.Ordinal))
                {
                    using var entryStream = entry.Open();
                    entryStream.Write(content);
                }
            }
        }

        return stream.ToArray();
    }

    /// <summary>Creates a tar archive, compressed with gzip when <paramref name="gzip"/> is true. A name that ends with <c>/</c> is a directory, and a name with <c>-&gt;</c> is a symbolic link.</summary>
    public static byte[] CreateTar(bool gzip, params (string Name, byte[] Content)[] entries)
    {
        using var stream = new MemoryStream();
        using (var compression = gzip ? new GZipStream(stream, CompressionLevel.Optimal, leaveOpen: true) : null)
        using (var writer = new TarWriter(compression ?? (Stream)stream, TarEntryFormat.Pax, leaveOpen: true))
        {
            foreach (var (name, content) in entries)
            {
                var link = name.Split(" -> ");
                PaxTarEntry entry;
                if (link.Length is 2)
                {
                    entry = new PaxTarEntry(TarEntryType.SymbolicLink, link[0]) { LinkName = link[1] };
                }
                else if (name.EndsWith('/', StringComparison.Ordinal))
                {
                    // A mode that is not the default one, to check that it is kept
                    entry = new PaxTarEntry(TarEntryType.Directory, name) { Mode = UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute };
                }
                else
                {
                    entry = new PaxTarEntry(TarEntryType.RegularFile, name) { DataStream = new MemoryStream(content) };
                }

                entry.ModificationTime = new DateTimeOffset(2024, 5, 6, 7, 8, 10, TimeSpan.Zero);
                writer.WriteEntry(entry);
            }
        }

        return stream.ToArray();
    }

    /// <summary>Creates a RAR5 archive whose files are stored without compression: no library writes RAR archives.</summary>
    public static byte[] CreateRar(params (string Name, byte[] Content)[] entries)
    {
        using var stream = new MemoryStream();
        stream.Write("Rar!\x1A\x07\x01\x00"u8);

        // Main archive header: type 1, no flags, no archive flags
        WriteHeader(stream, [1, 0, 0], data: []);
        foreach (var (name, content) in entries)
        {
            var nameBytes = Encoding.UTF8.GetBytes(name);
            var header = new List<byte>();
            WriteVInt(header, 2); // File header
            WriteVInt(header, 0x0002); // A data area follows
            WriteVInt(header, (ulong)content.Length);
            WriteVInt(header, 0x0004); // The CRC32 of the data is present
            WriteVInt(header, (ulong)content.Length);
            WriteVInt(header, 0x20); // Attributes: archive
            var crc = new byte[4];
            BinaryPrimitives.WriteUInt32LittleEndian(crc, Crc32.HashToUInt32(content));
            header.AddRange(crc);
            WriteVInt(header, 0); // Compression: version 0, stored
            WriteVInt(header, 0); // Host OS: Windows
            WriteVInt(header, (ulong)nameBytes.Length);
            header.AddRange(nameBytes);
            WriteHeader(stream, [.. header], content);
        }

        // End of archive header: type 5, no flags, no end flags
        WriteHeader(stream, [5, 0, 0], data: []);
        return stream.ToArray();

        static void WriteHeader(Stream stream, byte[] header, byte[] data)
        {
            var size = new List<byte>();
            WriteVInt(size, (ulong)header.Length);
            byte[] crcData = [.. size, .. header];
            Span<byte> crc = stackalloc byte[4];
            BinaryPrimitives.WriteUInt32LittleEndian(crc, Crc32.HashToUInt32(crcData));
            stream.Write(crc);
            stream.Write(crcData);
            stream.Write(data);
        }

        static void WriteVInt(List<byte> output, ulong value)
        {
            while (value >= 0x80)
            {
                output.Add((byte)(value | 0x80));
                value >>= 7;
            }

            output.Add((byte)value);
        }
    }

    /// <summary>Creates an EPUB book whose documents are given by name, with the images they show.</summary>
    public static byte[] CreateEpub(IEnumerable<(string Name, string Content)> documents, IEnumerable<(string Name, byte[] Content)>? resources = null, string title = "Test book")
    {
        var documentList = documents.ToList();
        var resourceList = resources?.ToList() ?? [];
        var manifest = new StringBuilder();
        var spine = new StringBuilder();
        for (var i = 0; i < documentList.Count; i++)
        {
            var documentMediaType = documentList[i].Name.EndsWith(".svg", StringComparison.Ordinal) ? "image/svg+xml" : "application/xhtml+xml";
            manifest.Append(CultureInfo.InvariantCulture, $"""<item id="doc{i}" href="{documentList[i].Name}" media-type="{documentMediaType}"/>""");
            spine.Append(CultureInfo.InvariantCulture, $"""<itemref idref="doc{i}"/>""");
        }

        for (var i = 0; i < resourceList.Count; i++)
        {
            var mediaType = Path.GetExtension(resourceList[i].Name) switch
            {
                ".css" => "text/css",
                ".svg" => "image/svg+xml",
                ".jpg" => "image/jpeg",
                _ => "image/png",
            };
            manifest.Append(CultureInfo.InvariantCulture, $"""<item id="res{i}" href="{resourceList[i].Name}" media-type="{mediaType}"/>""");
        }

        var package = $"""
            <?xml version="1.0" encoding="UTF-8"?>
            <package xmlns="http://www.idpf.org/2007/opf" version="3.0" unique-identifier="id">
              <metadata xmlns:dc="http://purl.org/dc/elements/1.1/">
                <dc:identifier id="id">test</dc:identifier>
                <dc:title>{title}</dc:title>
                <dc:creator>Test author</dc:creator>
                <dc:language>en</dc:language>
              </metadata>
              <manifest>{manifest}</manifest>
              <spine>{spine}</spine>
            </package>
            """;

        var entries = new List<(string Name, byte[] Content)>
        {
            ("mimetype", "application/epub+zip"u8.ToArray()),
            ("META-INF/container.xml", Encoding.UTF8.GetBytes("""
                <?xml version="1.0"?>
                <container version="1.0" xmlns="urn:oasis:names:tc:opendocument:xmlns:container">
                  <rootfiles><rootfile full-path="OEBPS/content.opf" media-type="application/oebps-package+xml"/></rootfiles>
                </container>
                """)),
            ("OEBPS/content.opf", Encoding.UTF8.GetBytes(package)),
        };
        entries.AddRange(documentList.Select(document => ("OEBPS/" + document.Name, Encoding.UTF8.GetBytes(document.Content))));
        entries.AddRange(resourceList.Select(resource => ("OEBPS/" + resource.Name, resource.Content)));
        return CreateZip([.. entries]);
    }

    public static string CreateXhtml(string body) => $"""
        <?xml version="1.0" encoding="UTF-8"?>
        <!DOCTYPE html>
        <html xmlns="http://www.w3.org/1999/xhtml"><head><title>Page</title></head><body>{body}</body></html>
        """;

    /// <summary>A book of 3 pages, each showing one image.</summary>
    public static byte[] CreateImageEpub()
    {
        var image = CreateImage(40, 60, ImageFormat.Png);
        return CreateEpub(
            [
                ("page1.xhtml", CreateXhtml("""<img src="images/p1.png" alt="Page 1"/>""")),
                ("page2.xhtml", CreateXhtml("""<div><img src="images/p2.png" alt=""/></div>""")),
                ("page3.xhtml", CreateXhtml("""<svg xmlns="http://www.w3.org/2000/svg" xmlns:xlink="http://www.w3.org/1999/xlink" viewBox="0 0 40 60"><image width="40" height="60" xlink:href="images/p3.png"/></svg>""")),
            ],
            [("images/p1.png", image), ("images/p2.png", image), ("images/p3.png", image)]);
    }

    /// <summary>A book of 2 chapters of text, with a style sheet and an image.</summary>
    public static byte[] CreateTextEpub()
    {
        return CreateEpub(
            [
                ("chapter1.xhtml", CreateXhtml("""<link rel="stylesheet" href="style.css"/><h1>Chapter one</h1><p>The first chapter of the book.</p><img src="images/figure.png" alt="Figure"/>""")),
                ("chapter2.xhtml", CreateXhtml("""<h1>Chapter two</h1><p>The second chapter of the book.</p>""")),
            ],
            [("style.css", "p { color: #333 }"u8.ToArray()), ("images/figure.png", CreateImage(20, 20, ImageFormat.Png))]);
    }

    /// <summary>An email message (.eml) with CRLF line breaks.</summary>
    public static byte[] CreateEml(string subject) => Encoding.UTF8.GetBytes(CreateEmlText(subject, "\r\n"));

    /// <summary>An mbox file of messages, with LF line breaks.</summary>
    public static byte[] CreateMbox(params string[] subjects) => Encoding.UTF8.GetBytes(string.Concat(subjects.Select(subject => "From alice@example.com Thu Jan 15 09:00:00 2026\n" + CreateEmlText(subject, "\n") + "\n")));

    /// <summary>An Outlook item (.msg).</summary>
    public static byte[] CreateMsg(string subject) => MsgFile.ToByteArray(CreateMapiMessage(subject));

    /// <summary>A TNEF stream (winmail.dat).</summary>
    public static byte[] CreateTnef(string subject) => TnefFile.ToByteArray(CreateMapiMessage(subject));

    /// <summary>An Outlook data file with the folders Inbox (message "Welcome"), Inbox/Projects (message "Project plan") and Contacts (a contact).</summary>
    public static byte[] CreatePst(PstWriterOptions? options = null)
    {
        using var stream = new MemoryStream();
        using (var writer = PstWriter.Create(stream, options, leaveOpen: true))
        {
            var inbox = writer.RootFolder.CreateFolder("Inbox", "IPF.Note");
            inbox.AddItem(CreateMapiMessage("Welcome"));
            inbox.CreateFolder("Projects", "IPF.Note").AddItem(CreateMapiMessage("Project plan"));
            writer.RootFolder.CreateFolder("Contacts", "IPF.Contact").AddItem(new MapiContactBuilder { GivenName = "Bob", Surname = "Smith", Email1Address = "bob@example.com" });
        }

        return stream.ToArray();
    }

    private static string CreateEmlText(string subject, string newLine) =>
        string.Join(newLine, "From: Alice <alice@example.com>", "To: bob@example.com", "Subject: " + subject, "Date: Thu, 15 Jan 2026 09:00:00 +0000", "MIME-Version: 1.0", "Content-Type: text/plain; charset=utf-8", "", "Hello from " + subject, "");

    private static MapiMessageBuilder CreateMapiMessage(string subject)
    {
        var message = new MapiMessageBuilder
        {
            Subject = subject,
            Body = "Hello from " + subject,
            SenderName = "Alice",
            SenderEmailAddress = "alice@example.com",
            SenderAddressType = "SMTP",
            ClientSubmitTime = new DateTime(2026, 1, 15, 9, 0, 0, DateTimeKind.Utc),
        };
        message.Recipients.Add(new MapiRecipientBuilder("Bob", "bob@example.com"));
        return message;
    }
}
