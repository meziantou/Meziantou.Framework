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

    /// <summary>The compressions of the text of a MOBI book.</summary>
    public enum MobiCompression
    {
        None = 1,
        PalmDoc = 2,
        HuffCdic = 17480,
    }

    /// <summary>A book of 2 chapters separated by a page break, with an image, a link to the second chapter and a cover the text does not show.</summary>
    public static byte[] CreateTextMobi(MobiCompression compression = MobiCompression.PalmDoc, bool utf8 = true, bool trailingEntries = false)
    {
        const string Html = """<html><head><guide><reference type="toc" title="Contents" filepos=0000000000 /></guide></head><body><p><a filepos=0000000000>Go to chapter two</a></p><h1>Chapter one</h1><p>The first chapter of the book.</p><img recindex="00001" /><mbp:pagebreak/><h1>Chapter two</h1><p>The second chapter of the book.</p></body></html>""";

        // The link goes to the start of the second chapter
        var position = Html.IndexOf("<h1>Chapter two", StringComparison.Ordinal);
        var html = Html.Replace("filepos=0000000000", "filepos=" + position.ToString("D10", CultureInfo.InvariantCulture), StringComparison.Ordinal);
        return CreateMobi(html, [CreateImage(20, 20, ImageFormat.Png), CreateImage(30, 40, ImageFormat.Jpeg)], compression, utf8, coverIndex: 2, trailingEntries: trailingEntries);
    }

    /// <summary>
    /// Creates a MOBI 6 book whose text is split into records of 4096 bytes. The images follow the text records: the first
    /// one is <c>recindex="1"</c>. <paramref name="coverIndex"/> is the <c>recindex</c> of the cover.
    /// </summary>
    public static byte[] CreateMobi(string html, IReadOnlyList<byte[]>? images = null, MobiCompression compression = MobiCompression.PalmDoc, bool utf8 = true, string title = "Test book", string author = "Test author", int? coverIndex = null, bool trailingEntries = false, ushort encryption = 0, uint fileVersion = 6)
    {
        var encoding = utf8 ? Encoding.UTF8 : CodePagesEncodingProvider.Instance.GetEncoding(1252)!;
        var text = encoding.GetBytes(html);
        images ??= [];

        var textRecords = CreateTextRecords(text, compression, trailingEntries);
        var huffCdicRecords = compression is MobiCompression.HuffCdic ? CreateHuffCdicRecords() : [];
        var record0 = CreateRecord0(new MobiRecord0
        {
            Compression = compression,
            TextLength = text.Length,
            TextRecordCount = textRecords.Count,
            Utf8 = utf8,
            FileVersion = fileVersion,
            Encryption = encryption,
            TrailingEntries = trailingEntries,
            FirstResourceIndex = images.Count > 0 ? (uint)(1 + textRecords.Count) : uint.MaxValue,
            HuffmanRecordIndex = (uint)(1 + textRecords.Count + images.Count),
            HuffmanRecordCount = (uint)huffCdicRecords.Count,
            Title = title,
            Author = author,
            CoverIndex = coverIndex,
        });

        return CreatePalmDatabase([record0, .. textRecords, .. images, .. huffCdicRecords, CreateEndOfFileRecord()]);
    }

    /// <summary>
    /// Creates a KF8 book from XHTML documents: the content of the body of each document is its fragment, and the rest its
    /// skeleton. The style sheets are the flows that follow the text (<c>kindle:flow:0001</c>...), and the resources are
    /// numbered from 1 (<c>kindle:embed:0001</c>...). With <paramref name="mobi6Html"/>, the file is a joint file that
    /// holds a MOBI 6 version of the book first, which shares the resources.
    /// </summary>
    public static byte[] CreateKf8(IReadOnlyList<string> parts, IReadOnlyList<string>? styleSheets = null, IReadOnlyList<byte[]>? resources = null, int? coverIndex = null, string? mobi6Html = null, MobiCompression compression = MobiCompression.PalmDoc, bool trailingEntries = false, string title = "Test book", string author = "Test author", bool corruptFdst = false)
    {
        styleSheets ??= [];
        resources ??= [];

        // The text: the skeleton then the fragment of each document, then the style sheets
        var text = new List<byte>();
        var skeletons = new List<(string Name, (int Tag, uint[] Values)[] Tags)>();
        var fragments = new List<(string Name, (int Tag, uint[] Values)[] Tags)>();
        for (var i = 0; i < parts.Count; i++)
        {
            var part = Encoding.UTF8.GetBytes(parts[i]);
            var bodyStart = part.AsSpan().IndexOf("<body"u8);
            bodyStart += part.AsSpan(bodyStart).IndexOf((byte)'>') + 1;
            var bodyEnd = part.AsSpan().IndexOf("</body>"u8);
            var start = (uint)text.Count;
            var skeletonLength = (uint)(part.Length - (bodyEnd - bodyStart));
            text.AddRange(part.AsSpan(0, bodyStart));
            text.AddRange(part.AsSpan(bodyEnd));
            text.AddRange(part.AsSpan(bodyStart, bodyEnd - bodyStart));

            var insertPosition = start + (uint)bodyStart;
            skeletons.Add(("SKEL" + i.ToString("D10", CultureInfo.InvariantCulture), [(1, [1]), (6, [start, skeletonLength])]));
            fragments.Add((insertPosition.ToString("D10", CultureInfo.InvariantCulture), [(2, [0]), (3, [(uint)i]), (4, [(uint)i]), (6, [insertPosition, (uint)(bodyEnd - bodyStart)])]));
        }

        var flowStarts = new List<uint> { 0 };
        foreach (var styleSheet in styleSheets)
        {
            flowStarts.Add((uint)text.Count);
            text.AddRange(Encoding.UTF8.GetBytes(styleSheet));
        }

        var fdst = new byte[12 + (flowStarts.Count * 8)];
        (corruptFdst ? "XXXX"u8 : "FDST"u8).CopyTo(fdst);
        BinaryPrimitives.WriteUInt32BigEndian(fdst.AsSpan(4), 12);
        BinaryPrimitives.WriteUInt32BigEndian(fdst.AsSpan(8), (uint)flowStarts.Count);
        for (var i = 0; i < flowStarts.Count; i++)
        {
            BinaryPrimitives.WriteUInt32BigEndian(fdst.AsSpan(12 + (i * 8)), flowStarts[i]);
            BinaryPrimitives.WriteUInt32BigEndian(fdst.AsSpan(16 + (i * 8)), i + 1 < flowStarts.Count ? flowStarts[i + 1] : (uint)text.Count);
        }

        var skeletonIndex = CreateIndex(skeletons, [(1, 1, 0x03), (6, 2, 0x0C)]);
        var fragmentIndex = CreateIndex(fragments, [(2, 1, 0x01), (3, 1, 0x02), (4, 1, 0x04), (6, 2, 0x08)]);
        var textRecords = CreateTextRecords([.. text], compression, trailingEntries);
        var huffCdicRecords = compression is MobiCompression.HuffCdic ? CreateHuffCdicRecords() : [];

        // The records of the KF8 version, whose indexes are relative to its first record
        var joint = mobi6Html is not null;
        var kf8Resources = joint ? [] : resources;
        var skeletonRecord = 1 + textRecords.Count + kf8Resources.Count;
        var fragmentRecord = skeletonRecord + skeletonIndex.Count;
        var fdstRecord = fragmentRecord + fragmentIndex.Count;
        var kf8Record0 = CreateRecord0(new MobiRecord0
        {
            Compression = compression,
            TextLength = text.Count,
            TextRecordCount = textRecords.Count,
            Utf8 = true,
            FileVersion = 8,
            TrailingEntries = trailingEntries,
            FirstResourceIndex = kf8Resources.Count > 0 ? (uint)(1 + textRecords.Count) : uint.MaxValue,
            HuffmanRecordIndex = (uint)(fdstRecord + 1),
            HuffmanRecordCount = (uint)huffCdicRecords.Count,
            FdstIndex = (uint)fdstRecord,
            SkeletonIndex = (uint)skeletonRecord,
            FragmentIndex = (uint)fragmentRecord,
            Title = title,
            Author = author,
            CoverIndex = coverIndex,
        });

        List<byte[]> kf8Records = [kf8Record0, .. textRecords, .. kf8Resources, .. skeletonIndex, .. fragmentIndex, fdst, .. huffCdicRecords];
        if (!joint)
            return CreatePalmDatabase([.. kf8Records, CreateEndOfFileRecord()]);

        // A joint file: the MOBI 6 version and the resources, then a BOUNDARY record and the KF8 version
        var mobi6Text = Encoding.UTF8.GetBytes(mobi6Html!);
        var mobi6TextRecords = CreateTextRecords(mobi6Text, MobiCompression.PalmDoc, trailingEntries: false);
        var kf8Index = 1 + mobi6TextRecords.Count + resources.Count + 1;
        var mobi6Record0 = CreateRecord0(new MobiRecord0
        {
            Compression = MobiCompression.PalmDoc,
            TextLength = mobi6Text.Length,
            TextRecordCount = mobi6TextRecords.Count,
            Utf8 = true,
            FileVersion = 6,
            FirstResourceIndex = resources.Count > 0 ? (uint)(1 + mobi6TextRecords.Count) : uint.MaxValue,
            Title = title,
            Author = author,
            CoverIndex = coverIndex,
            Kf8RecordIndex = (uint)kf8Index,
        });

        return CreatePalmDatabase([mobi6Record0, .. mobi6TextRecords, .. resources, "BOUNDARY"u8.ToArray(), .. kf8Records, CreateEndOfFileRecord()]);
    }

    /// <summary>A KF8 book of 2 documents, with a style sheet, an image, a link to the second document and a cover the documents do not show.</summary>
    public static byte[] CreateTextKf8(string? mobi6Html = null, MobiCompression compression = MobiCompression.PalmDoc, bool trailingEntries = false, bool corruptFdst = false)
    {
        return CreateKf8(
            [
                CreateKf8Xhtml("""<p><a href="kindle:pos:fid:0001:off:0000000000">Go to chapter two</a></p><h1>Chapter one</h1><p>The first chapter of the book.</p><img src="kindle:embed:0001?mime=image/png" alt=""/>"""),
                CreateKf8Xhtml("""<h1 id="chapter2">Chapter two</h1><p>The second chapter of the book.</p>"""),
            ],
            ["h1 { color: #036 }"],
            [CreateImage(20, 20, ImageFormat.Png), CreateImage(30, 40, ImageFormat.Jpeg)],
            coverIndex: 2,
            mobi6Html,
            compression,
            trailingEntries,
            corruptFdst: corruptFdst);
    }

    /// <summary>A KF8 comic: 3 documents that each show one image, the first one of which is the cover.</summary>
    public static byte[] CreateComicKf8()
    {
        return CreateKf8(
            [
                CreateKf8Xhtml("""<div><img src="kindle:embed:0001?mime=image/png" alt=""/></div>"""),
                CreateKf8Xhtml("""<div><img src="kindle:embed:0002?mime=image/png" alt=""/></div>"""),
                CreateKf8Xhtml("""<svg xmlns="http://www.w3.org/2000/svg" xmlns:xlink="http://www.w3.org/1999/xlink" viewBox="0 0 40 60"><image width="40" height="60" xlink:href="kindle:embed:0003?mime=image/png"/></svg>"""),
            ],
            resources: [CreateImage(40, 60, ImageFormat.Png), CreateImage(40, 60, ImageFormat.Png), CreateImage(40, 60, ImageFormat.Png)],
            coverIndex: 1);
    }

    public static string CreateKf8Xhtml(string body) => $"""
        <?xml version="1.0" encoding="UTF-8"?>
        <html xmlns="http://www.w3.org/1999/xhtml"><head><title>Part</title><link rel="stylesheet" type="text/css" href="kindle:flow:0001?mime=text/css"/></head><body aid="0">{body}</body></html>
        """;

    /// <summary>Creates a FONT record: the font file, compressed with zlib and obfuscated with a key of 16 bytes when asked.</summary>
    public static byte[] CreateFontRecord(byte[] font, bool compress, bool obfuscate)
    {
        var data = font;
        if (compress)
        {
            using var output = new MemoryStream();
            using (var zlib = new ZLibStream(output, CompressionLevel.Optimal, leaveOpen: true))
            {
                zlib.Write(font);
            }

            data = output.ToArray();
        }

        var key = obfuscate ? Enumerable.Range(0, 16).Select(i => (byte)(0x35 + (i * 7))).ToArray() : [];
        data = [.. data];
        for (var i = 0; i < Math.Min(1040, data.Length) && key.Length > 0; i++)
        {
            data[i] ^= key[i % key.Length];
        }

        var header = new byte[24];
        "FONT"u8.CopyTo(header);
        BinaryPrimitives.WriteUInt32BigEndian(header.AsSpan(4), (uint)font.Length);
        BinaryPrimitives.WriteUInt32BigEndian(header.AsSpan(8), (compress ? 1u : 0u) | (obfuscate ? 2u : 0u));
        BinaryPrimitives.WriteUInt32BigEndian(header.AsSpan(12), (uint)(24 + key.Length));
        BinaryPrimitives.WriteUInt32BigEndian(header.AsSpan(16), (uint)key.Length);
        BinaryPrimitives.WriteUInt32BigEndian(header.AsSpan(20), 24);
        return [.. header, .. key, .. data];
    }

    private static List<byte[]> CreateTextRecords(byte[] text, MobiCompression compression, bool trailingEntries)
    {
        var records = new List<byte[]>();
        for (var offset = 0; offset < text.Length; offset += 4096)
        {
            var chunk = text.AsSpan(offset, Math.Min(4096, text.Length - offset));
            var data = compression switch
            {
                MobiCompression.None => chunk.ToArray(),
                MobiCompression.PalmDoc => CompressPalmDoc(chunk),
                _ => CompressHuffCdic(chunk),
            };

            // A multibyte entry of 1 byte (its count), then an entry of 3 bytes whose last byte is its size
            records.Add(trailingEntries ? [.. data, 0x00, 0xAA, 0xBB, 0x83] : data);
        }

        return records;
    }

    private static byte[] CreateRecord0(MobiRecord0 options)
    {
        var encoding = options.Utf8 ? Encoding.UTF8 : CodePagesEncodingProvider.Instance.GetEncoding(1252)!;

        // EXTH: author, title, cover and KF8 version
        var exthRecords = new List<(uint Type, byte[] Value)> { (100, encoding.GetBytes(options.Author)), (503, encoding.GetBytes(options.Title)) };
        if (options.CoverIndex is { } cover)
        {
            exthRecords.Add((201, ToBigEndian((uint)(cover - 1))));
        }

        if (options.Kf8RecordIndex is { } kf8RecordIndex)
        {
            exthRecords.Add((121, ToBigEndian(kf8RecordIndex)));
        }

        using var exth = new MemoryStream();
        exth.Write("EXTH"u8);
        var exthLength = 12 + exthRecords.Sum(record => 8 + record.Value.Length);
        var exthPadding = (4 - (exthLength % 4)) % 4;
        exth.Write(ToBigEndian((uint)(exthLength + exthPadding)));
        exth.Write(ToBigEndian((uint)exthRecords.Count));
        foreach (var (type, value) in exthRecords)
        {
            exth.Write(ToBigEndian(type));
            exth.Write(ToBigEndian((uint)(8 + value.Length)));
            exth.Write(value);
        }

        exth.Write(new byte[exthPadding]);

        const int MobiHeaderLength = 264;
        var fullName = encoding.GetBytes(options.Title);
        var record0 = new byte[16 + MobiHeaderLength + exth.Length + fullName.Length + 4];
        var header = record0.AsSpan();
        BinaryPrimitives.WriteUInt16BigEndian(header[0..], (ushort)options.Compression);
        BinaryPrimitives.WriteUInt32BigEndian(header[4..], (uint)options.TextLength);
        BinaryPrimitives.WriteUInt16BigEndian(header[8..], (ushort)options.TextRecordCount);
        BinaryPrimitives.WriteUInt16BigEndian(header[10..], 4096);
        BinaryPrimitives.WriteUInt16BigEndian(header[12..], options.Encryption);
        "MOBI"u8.CopyTo(header[16..]);
        BinaryPrimitives.WriteUInt32BigEndian(header[20..], MobiHeaderLength);
        BinaryPrimitives.WriteUInt32BigEndian(header[24..], 2); // Mobipocket book
        BinaryPrimitives.WriteUInt32BigEndian(header[28..], options.Utf8 ? 65001u : 1252u);
        BinaryPrimitives.WriteUInt32BigEndian(header[36..], options.FileVersion);
        header[40..80].Fill(0xFF); // No index
        BinaryPrimitives.WriteUInt32BigEndian(header[84..], (uint)(16 + MobiHeaderLength + exth.Length));
        BinaryPrimitives.WriteUInt32BigEndian(header[88..], (uint)fullName.Length);
        BinaryPrimitives.WriteUInt32BigEndian(header[104..], options.FileVersion);
        BinaryPrimitives.WriteUInt32BigEndian(header[108..], options.FirstResourceIndex);
        BinaryPrimitives.WriteUInt32BigEndian(header[112..], options.HuffmanRecordCount > 0 ? options.HuffmanRecordIndex : 0);
        BinaryPrimitives.WriteUInt32BigEndian(header[116..], options.HuffmanRecordCount);
        BinaryPrimitives.WriteUInt32BigEndian(header[128..], 0x50); // EXTH
        BinaryPrimitives.WriteUInt32BigEndian(header[168..], uint.MaxValue); // No DRM
        if (options.FileVersion >= 8)
        {
            BinaryPrimitives.WriteUInt32BigEndian(header[192..], options.FdstIndex);
            BinaryPrimitives.WriteUInt32BigEndian(header[196..], 1);
        }
        else
        {
            BinaryPrimitives.WriteUInt16BigEndian(header[192..], 1);
            BinaryPrimitives.WriteUInt16BigEndian(header[194..], (ushort)Math.Max(1, options.TextRecordCount));
        }

        BinaryPrimitives.WriteUInt16BigEndian(header[242..], options.TrailingEntries ? (ushort)0b11 : (ushort)0);
        BinaryPrimitives.WriteUInt32BigEndian(header[244..], uint.MaxValue); // No NCX
        BinaryPrimitives.WriteUInt32BigEndian(header[248..], options.FragmentIndex);
        BinaryPrimitives.WriteUInt32BigEndian(header[252..], options.SkeletonIndex);
        BinaryPrimitives.WriteUInt32BigEndian(header[256..], uint.MaxValue); // No DATP
        BinaryPrimitives.WriteUInt32BigEndian(header[260..], uint.MaxValue); // No guide
        exth.ToArray().CopyTo(header[(16 + MobiHeaderLength)..]);
        fullName.CopyTo(header[(16 + MobiHeaderLength + (int)exth.Length)..]);
        return record0;
    }

    /// <summary>Creates the records of an index: a record with the TAGX table, then a record with the entries and their IDXT table.</summary>
    private static List<byte[]> CreateIndex(List<(string Name, (int Tag, uint[] Values)[] Tags)> entries, (int Tag, int ValuesPerEntry, int Mask)[] tagTable)
    {
        const int HeaderLength = 192;

        var tagx = new List<byte>();
        tagx.AddRange("TAGX"u8);
        tagx.AddRange(ToBigEndian((uint)(12 + ((tagTable.Length + 1) * 4))));
        tagx.AddRange(ToBigEndian(1)); // One control byte
        foreach (var (tag, valuesPerEntry, mask) in tagTable)
        {
            tagx.AddRange([(byte)tag, (byte)valuesPerEntry, (byte)mask, 0]);
        }

        tagx.AddRange([0, 0, 0, 1]);

        var main = CreateIndexHeader(HeaderLength, idxtOffset: 0, count: 1, total: (uint)entries.Count);
        var data = new List<byte>();
        var offsets = new List<int>();
        foreach (var (name, tags) in entries)
        {
            offsets.Add(HeaderLength + data.Count);
            data.Add((byte)name.Length);
            data.AddRange(Encoding.ASCII.GetBytes(name));

            // Each tag has one group of values: the lowest bit of its mask
            var controlByte = 0;
            foreach (var (tag, _) in tags)
            {
                var mask = tagTable.Single(item => item.Tag == tag).Mask;
                controlByte |= mask & -mask;
            }

            data.Add((byte)controlByte);
            foreach (var (_, values) in tags)
            {
                foreach (var value in values)
                {
                    WriteVariableWidthValue(data, value);
                }
            }
        }

        var idxtOffset = HeaderLength + data.Count;
        data.AddRange("IDXT"u8);
        foreach (var offset in offsets)
        {
            data.AddRange([(byte)(offset >> 8), (byte)offset]);
        }

        var record = CreateIndexHeader(HeaderLength, (uint)idxtOffset, (uint)entries.Count, total: 0);
        return [[.. main, .. tagx], [.. record, .. data]];

        static byte[] CreateIndexHeader(int length, uint idxtOffset, uint count, uint total)
        {
            var header = new byte[length];
            "INDX"u8.CopyTo(header);
            BinaryPrimitives.WriteUInt32BigEndian(header.AsSpan(4), (uint)length);
            BinaryPrimitives.WriteUInt32BigEndian(header.AsSpan(20), idxtOffset);
            BinaryPrimitives.WriteUInt32BigEndian(header.AsSpan(24), count);
            BinaryPrimitives.WriteUInt32BigEndian(header.AsSpan(28), 65001);
            BinaryPrimitives.WriteUInt32BigEndian(header.AsSpan(32), uint.MaxValue);
            BinaryPrimitives.WriteUInt32BigEndian(header.AsSpan(36), total);
            return header;
        }

        static void WriteVariableWidthValue(List<byte> output, uint value)
        {
            // 7 bits per byte, most significant first, and the high bit set on the last byte
            var bytes = new Stack<byte>();
            do
            {
                bytes.Push((byte)(value & 0x7F));
                value >>= 7;
            }
            while (value is not 0);

            var result = bytes.ToArray();
            result[^1] |= 0x80;
            output.AddRange(result);
        }
    }

    private static byte[] CreatePalmDatabase(IReadOnlyList<byte[]> records)
    {
        using var stream = new MemoryStream();
        var name = new byte[32];
        Encoding.ASCII.GetBytes("Test_book").CopyTo(name, 0);
        stream.Write(name);
        stream.Write(new byte[28]); // Attributes, version, dates, modification number, application info, sort info
        stream.Write("BOOKMOBI"u8);
        stream.Write(new byte[8]); // Unique id seed, next record list
        stream.Write([(byte)(records.Count >> 8), (byte)records.Count]);
        var recordOffset = 78 + (records.Count * 8) + 2;
        for (var i = 0; i < records.Count; i++)
        {
            stream.Write(ToBigEndian((uint)recordOffset));
            stream.Write(ToBigEndian((uint)(i * 2))); // Attributes and unique id
            recordOffset += records[i].Length;
        }

        stream.Write([0, 0]);
        foreach (var record in records)
        {
            stream.Write(record);
        }

        return stream.ToArray();
    }

    private static byte[] ToBigEndian(uint value)
    {
        var result = new byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(result, value);
        return result;
    }

    private static byte[] CreateEndOfFileRecord() => [0xE9, 0x8E, 0x0D, 0x0A];

    /// <summary>Compresses text with the PalmDOC compression: back-references of 3 to 10 bytes, spaces followed by a character, and literal bytes.</summary>
    public static byte[] CompressPalmDoc(ReadOnlySpan<byte> input)
    {
        var output = new List<byte>();
        var position = 0;
        while (position < input.Length)
        {
            var bestLength = 0;
            var bestDistance = 0;
            for (var distance = 1; distance <= Math.Min(position, 2047); distance++)
            {
                var length = 0;
                while (length < 10 && position + length < input.Length && input[position + length - distance] == input[position + length])
                {
                    length++;
                }

                if (length > bestLength)
                {
                    bestLength = length;
                    bestDistance = distance;
                }
            }

            if (bestLength >= 3)
            {
                var pair = 0x8000 | (bestDistance << 3) | (bestLength - 3);
                output.Add((byte)(pair >> 8));
                output.Add((byte)pair);
                position += bestLength;
                continue;
            }

            var value = input[position];
            if (value is (byte)' ' && position + 1 < input.Length && input[position + 1] is >= 0x40 and <= 0x7F)
            {
                output.Add((byte)(input[position + 1] ^ 0x80));
                position += 2;
                continue;
            }

            if (value is 0 or (>= 9 and <= 0x7F))
            {
                output.Add(value);
                position++;
                continue;
            }

            // A run of 1 to 8 bytes copied as they are
            var count = 0;
            while (count < 8 && position + count < input.Length && input[position + count] is (>= 1 and <= 8) or >= 0x80)
            {
                count++;
            }

            output.Add((byte)count);
            output.AddRange(input.Slice(position, count));
            position += count;
        }

        return [.. output];
    }

    /// <summary>
    /// Compresses text with a HUFF/CDIC code of 8-bit codes: the code <c>255 - b</c> is the phrase of the byte <c>b</c>, except
    /// the code 0, which is the phrase "the ", itself compressed. The text must not contain the byte 255.
    /// </summary>
    public static byte[] CompressHuffCdic(ReadOnlySpan<byte> input)
    {
        var output = new List<byte>();
        for (var position = 0; position < input.Length; position++)
        {
            if (input[position..].StartsWith("the "u8))
            {
                output.Add(0);
                position += 3;
                continue;
            }

            if (input[position] is 255)
                throw new ArgumentException("The text contains the byte 255", nameof(input));

            output.Add((byte)(255 - input[position]));
        }

        return [.. output];
    }

    private static List<byte[]> CreateHuffCdicRecords()
    {
        // HUFF: 256 terminal codes of 8 bits, whose largest code is 255
        var huff = new byte[24 + (256 * 4) + (64 * 4)];
        "HUFF\0\0\0\x18"u8.CopyTo(huff);
        BinaryPrimitives.WriteUInt32BigEndian(huff.AsSpan(8), 24);
        BinaryPrimitives.WriteUInt32BigEndian(huff.AsSpan(12), 24 + (256 * 4));
        for (var i = 0; i < 256; i++)
        {
            BinaryPrimitives.WriteUInt32BigEndian(huff.AsSpan(24 + (i * 4)), (255u << 8) | 0x80 | 8);
        }

        // CDIC: the phrase of index i is the byte i, except the phrase 255, which is "the " compressed
        using var phrases = new MemoryStream();
        var offsets = new byte[256 * 2];
        for (var i = 0; i < 256; i++)
        {
            BinaryPrimitives.WriteUInt16BigEndian(offsets.AsSpan(i * 2), (ushort)(offsets.Length + phrases.Length));
            byte[] phrase = i is 255 ? CompressHuffCdic("the"u8).Append((byte)(255 - ' ')).ToArray() : [(byte)i];
            phrases.WriteByte((byte)((phrase.Length >> 8) | (i is 255 ? 0 : 0x80)));
            phrases.WriteByte((byte)phrase.Length);
            phrases.Write(phrase);
        }

        var cdic = new byte[16];
        "CDIC\0\0\0\x10"u8.CopyTo(cdic);
        BinaryPrimitives.WriteUInt32BigEndian(cdic.AsSpan(8), 256);
        BinaryPrimitives.WriteUInt32BigEndian(cdic.AsSpan(12), 8);
        return [huff, [.. cdic, .. offsets, .. phrases.ToArray()]];
    }

    /// <summary>The fields of the first record of a version of a MOBI book.</summary>
    private sealed class MobiRecord0
    {
        public MobiCompression Compression { get; init; }

        public int TextLength { get; init; }

        public int TextRecordCount { get; init; }

        public bool Utf8 { get; init; }

        public uint FileVersion { get; init; }

        public ushort Encryption { get; init; }

        public bool TrailingEntries { get; init; }

        public uint FirstResourceIndex { get; init; } = uint.MaxValue;

        public uint HuffmanRecordIndex { get; init; }

        public uint HuffmanRecordCount { get; init; }

        public uint FdstIndex { get; init; } = uint.MaxValue;

        public uint SkeletonIndex { get; init; } = uint.MaxValue;

        public uint FragmentIndex { get; init; } = uint.MaxValue;

        public required string Title { get; init; }

        public required string Author { get; init; }

        public int? CoverIndex { get; init; }

        public uint? Kf8RecordIndex { get; init; }
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
