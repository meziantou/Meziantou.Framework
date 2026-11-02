using System.Security.Cryptography;
using System.Text;
using Meziantou.Framework.Pdf.Objects;
using Meziantou.Framework.SnapshotTesting;

namespace Meziantou.Framework.Pdf.Tests;

public sealed class PdfWriterTests
{
    private static readonly PdfSaveOptions Uncompressed = new() { CompressStreams = false, UseObjectStreams = false };

    [Fact]
    public void GoldenMinimalDocument()
    {
        var document = new PdfDocument();
        var page = document.Pages.Add(new PdfSize(200, 100));
        page.CreateContentBuilder()
            .SetFillRgb(1, 0, 0)
            .Rectangle(10, 10, 50, 30)
            .Fill()
            .DrawText("Hello", PdfFont.GetStandardFont(PdfStandardFont.Helvetica), 12, 20, 60);

        Snapshot.Validate(Encoding.Latin1.GetString(document.ToByteArray(Uncompressed)));
    }

    [Fact]
    public void GoldenObjectStreams()
    {
        var document = new PdfDocument();
        document.Pages.Add(new PdfSize(200, 100)).CreateContentBuilder().MoveTo(0, 0).LineTo(10, 10).Stroke();

        var bytes = document.ToByteArray(new PdfSaveOptions { CompressStreams = false, UseObjectStreams = true });

        Snapshot.Validate(bytes, SnapshotType.Create("pdf"));
    }

    [Fact]
    public void SavingIsDeterministic()
    {
        static byte[] Create()
        {
            var document = new PdfDocument();
            document.Information.Title = "Same";
            document.Pages.Add(PdfSize.A4).CreateContentBuilder().DrawText("Same", PdfFont.GetStandardFont(PdfStandardFont.TimesRoman), 10, 10, 10);
            return document.ToByteArray();
        }

        Assert.Equal(Create(), Create());
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void RoundTrip(bool compress, bool objectStreams)
    {
        var document = CreateRichDocument();

        var saved = document.ToByteArray(new PdfSaveOptions { CompressStreams = compress, UseObjectStreams = objectStreams });
        var read = PdfDocument.Open(saved);

        Assert.False(read.IsRepaired);
        Assert.Equal(PdfVersion.Pdf17, read.Version);
        Assert.Equal(3, read.Pages.Count);
        Assert.Equal("Title \u00E9\u4E2D", read.Information.Title);
        Assert.Equal("Author", read.Information.Author);
        Assert.Equal(new DateTimeOffset(2024, 1, 2, 3, 4, 5, TimeSpan.FromHours(-5)), read.Information.CreationDate);
        Assert.Equal("Page 1\nSecond line", read.Pages[0].ExtractText());
        Assert.Equal("Page 2", read.Pages[1].ExtractText());
        Assert.Equal(PdfSize.Letter.Width, read.Pages[2].MediaBox.Width);
        Assert.Equal(90, read.Pages[2].Rotation);

        Assert.HasCount(2, read.Outlines);
        Assert.Equal("Chapter 1", read.Outlines[0].Title);
        Assert.Same(read.Pages[0], read.Outlines[0].Destination!.Page);
        Assert.Single(read.Outlines[0].Children);
        Assert.Equal("Section 1.1", read.Outlines[0].Children[0].Title);
        Assert.Equal(PdfDestinationKind.Xyz, read.Outlines[0].Children[0].Destination!.Kind);
        Assert.Equal(700, read.Outlines[0].Children[0].Destination!.Top);
        Assert.True(read.Outlines[0].IsOpen);
        Assert.True(read.Outlines[1].IsBold);
        Assert.Equal(PdfColor.FromRgb(1, 0, 0), read.Outlines[1].Color);

        var links = read.Pages[0].Annotations.OfType<PdfLinkAnnotation>().ToList();
        Assert.HasCount(2, links);
        Assert.Equal(new Uri("https://www.meziantou.net/"), links[0].Uri);
        Assert.Same(read.Pages[1], links[1].Destination!.Page);
        Assert.Equal(new PdfRectangle(10, 20, 110, 40), links[0].Rectangle);
    }

    [Fact]
    public void RoundTripKeepsTheObjectGraph()
    {
        var document = CreateRichDocument();
        var first = document.ToByteArray(Uncompressed);

        var second = PdfDocument.Open(first).ToByteArray(Uncompressed);

        // A document written by the library and read back is written with the same objects; only the /ID differs
        Assert.Equal(RemoveId(first), RemoveId(second));
    }

    [Fact]
    public void UnreachableObjectsAreNotWritten()
    {
        var document = new PdfDocument();
        document.Objects.Add(new PdfDictionary { [PdfName.Create("Orphan")] = PdfBoolean.True });
        document.Pages.Add(PdfSize.A5);

        var text = Encoding.Latin1.GetString(document.ToByteArray(Uncompressed));

        Assert.DoesNotContain("Orphan", text, StringComparison.Ordinal);
    }

    [Fact]
    public void RemovedPagesAreNotWritten()
    {
        var document = new PdfDocument();
        document.Pages.Add(PdfSize.A5).CreateContentBuilder().DrawText("Kept", PdfFont.GetStandardFont(PdfStandardFont.Courier), 10, 10, 10);
        document.Pages.Add(PdfSize.A5).CreateContentBuilder().DrawText("Removed", PdfFont.GetStandardFont(PdfStandardFont.Courier), 10, 10, 10);
        document.Pages.RemoveAt(1);

        var read = PdfDocument.Open(document.ToByteArray(Uncompressed));

        Assert.Single(read.Pages);
        Assert.Equal("Kept", read.Pages[0].ExtractText());
        Assert.DoesNotContain("Removed", Encoding.Latin1.GetString(document.ToByteArray(Uncompressed)), StringComparison.Ordinal);
    }

    [Fact]
    public void IncrementalSaveAppendsTheChanges()
    {
        var original = HandWrittenPdf.CreateSimple().ToArray();
        var document = PdfDocument.Open(original);
        document.Information.Title = "Changed";
        document.Pages[0].CreateContentBuilder().DrawText("World", PdfFont.GetStandardFont(PdfStandardFont.Helvetica), 12, 72, 700);

        using var output = new MemoryStream();
        document.SaveIncremental(output, Uncompressed);
        var updated = output.ToArray();

        Assert.Equal(original, updated.AsSpan(0, original.Length).ToArray());
        var appended = Encoding.Latin1.GetString(updated.AsSpan(original.Length));
        Assert.Contains("/Prev ", appended, StringComparison.Ordinal);
        Assert.DoesNotContain("\n4 0 obj", appended, StringComparison.Ordinal);
        Assert.StartsWith("3 0 obj", appended, StringComparison.Ordinal);

        var read = PdfDocument.Open(updated);
        Assert.False(read.IsRepaired);
        Assert.Equal("Changed", read.Information.Title);
        Assert.Equal("Hello\nWorld", read.Pages[0].ExtractText());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void IncrementalSaveOfAnUnchangedDocumentWritesTheOriginalFile(bool xrefStream)
    {
        var file = HandWrittenPdf.CreateSimple();
        file.UseXrefStream = xrefStream;
        var original = file.ToArray();
        var document = PdfDocument.Open(original);
        _ = document.Pages[0].ExtractText();

        using var output = new MemoryStream();
        document.SaveIncremental(output);

        Assert.Equal(original, output.ToArray());
    }

    [Fact]
    public void IncrementalSaveOfAChangedTrailerWritesAnUpdate()
    {
        var original = HandWrittenPdf.CreateSimple().ToArray();
        var document = PdfDocument.Open(original);
        document.Trailer[PdfName.Create("Private")] = PdfString.FromText("value");

        using var output = new MemoryStream();
        document.SaveIncremental(output);
        var appended = Encoding.Latin1.GetString(output.ToArray().AsSpan(original.Length));

        // A cross-reference section has at least one subsection (7.5.4): the update without objects is not written as an empty one
        Assert.DoesNotContain("xref\ntrailer", appended, StringComparison.Ordinal);
        Assert.StartsWith("1 0 obj", appended, StringComparison.Ordinal);
        Assert.Equal("value", PdfDocument.Open(output.ToArray()).Trailer.Get<PdfString>(PdfName.Create("Private"))!.ToText());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SaveMethodsWriteTheSameBytes(bool incremental)
    {
        var document = incremental ? PdfDocument.Open(CreateRichDocument().ToByteArray()) : CreateRichDocument();
        document.Information.Subject = "Changed";
        byte[] Save(Action<Stream> save)
        {
            using var stream = new MemoryStream();
            save(stream);
            return stream.ToArray();
        }

        var expected = incremental ? Save(s => document.SaveIncremental(s)) : document.ToByteArray();

        Assert.Equal(expected, incremental ? Save(s => document.SaveIncrementalAsync(s, cancellationToken: XunitCancellationToken).GetAwaiter().GetResult()) : Save(s => document.Save(s)));
        Assert.Equal(expected, incremental ? Save(s => document.SaveIncremental(s)) : Save(s => document.SaveAsync(s, cancellationToken: XunitCancellationToken).GetAwaiter().GetResult()));
        if (!incremental)
        {
            var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".pdf");
            try
            {
                document.Save(path);
                Assert.Equal(expected, File.ReadAllBytes(path));
            }
            finally
            {
                File.Delete(path);
            }
        }
    }

    [Fact]
    public void LargeDocumentsAreWrittenThroughABoundedBuffer()
    {
        var document = new PdfDocument();
        var image = new byte[300_000];
        RandomNumberGenerator.Fill(image);
        for (var i = 0; i < 20; i++)
        {
            var stream = new PdfStream();
            stream.SetEncodedData(image);
            document.Pages.Add(PdfSize.A4).Dictionary[PdfName.Create("Private")] = document.Objects.Add(stream);
        }

        using var output = new WriteCountingStream();
        document.Save(output);

        // The file is written in blocks as it is serialized, not in one write of the whole file
        Assert.True(output.Length > 20 * image.Length);
        Assert.True(output.LargestWrite < 2 * image.Length, $"Largest write: {output.LargestWrite}");
        Assert.Equal(document.ToByteArray(), output.ToArray());
    }

    [Fact]
    public async Task SaveAsyncObservesCancellation()
    {
        var document = CreateRichDocument();
        using var canceled = new CancellationTokenSource();
        await canceled.CancelAsync();

        using var output = new MemoryStream();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => document.SaveAsync(output, cancellationToken: canceled.Token));
        Assert.Equal(0, output.Length);

        var incremental = PdfDocument.Open(document.ToByteArray());
        incremental.Information.Title = "Changed";
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => incremental.SaveIncrementalAsync(output, cancellationToken: canceled.Token));
        Assert.Equal(0, output.Length);
    }

    [Fact]
    public async Task SaveIncrementalAsyncObservesCancellationBetweenBlocks()
    {
        var original = new PdfDocument();
        var data = new byte[1_000_000];
        RandomNumberGenerator.Fill(data);
        var stream = new PdfStream();
        stream.SetEncodedData(data);
        original.Pages.Add(PdfSize.A4).Dictionary[PdfName.Create("Private")] = original.Objects.Add(stream);
        var document = PdfDocument.Open(original.ToByteArray(new PdfSaveOptions { CompressStreams = false }));
        document.Information.Title = "Changed";

        // The original file is written first; the token is canceled while it is written, before the update
        using var cancellation = new CancellationTokenSource();
        using var output = new WriteCountingStream { OnWrite = cancellation.Cancel };
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => document.SaveIncrementalAsync(output, cancellationToken: cancellation.Token));
        Assert.Equal(1, output.WriteCount);
    }

    [Fact]
    public void IncrementalSaveOfACrossReferenceStream()
    {
        var file = HandWrittenPdf.CreateSimple().Compress(1, 2, 4);
        file.UseXrefStream = true;
        var original = file.ToArray();
        var document = PdfDocument.Open(original);
        document.Information.Author = "Me";

        using var output = new MemoryStream();
        document.SaveIncremental(output, Uncompressed);

        var appended = Encoding.Latin1.GetString(output.ToArray().AsSpan(original.Length));
        Assert.Contains("/Type /XRef", appended, StringComparison.Ordinal);
        var read = PdfDocument.Open(output.ToArray());
        Assert.False(read.IsRepaired);
        Assert.Equal("Me", read.Information.Author);
        Assert.Equal("Hello", read.Pages[0].ExtractText());
    }

    [Fact]
    public void IncrementalSaveNeedsAFile()
    {
        using var output = new MemoryStream();

        Assert.Throws<InvalidOperationException>(() => new PdfDocument().SaveIncremental(output));
    }

    [Fact]
    public void IncrementalSaveOfARepairedFileThrows()
    {
        var file = HandWrittenPdf.CreateSimple();
        file.WriteXref = false;
        var document = PdfDocument.Open(file.ToArray());

        using var output = new MemoryStream();
        Assert.Throws<InvalidOperationException>(() => document.SaveIncremental(output));
    }

    [Fact]
    public void RepairedFileCanBeSavedInFull()
    {
        var file = HandWrittenPdf.CreateSimple();
        file.OffsetError = 7;
        file.WriteXref = false;
        var document = PdfDocument.Open(file.ToArray());

        var read = PdfDocument.Open(document.ToByteArray());

        Assert.False(read.IsRepaired);
        Assert.Equal("Hello", read.Pages[0].ExtractText());
    }

    [Fact]
    public void ObjectStreamsNeedPdf15()
    {
        var document = new PdfDocument { Version = PdfVersion.Pdf14 };

        Assert.StartsWith("%PDF-1.5", Encoding.ASCII.GetString(document.ToByteArray(new PdfSaveOptions { UseObjectStreams = true })), StringComparison.Ordinal);
        Assert.StartsWith("%PDF-1.4", Encoding.ASCII.GetString(document.ToByteArray(new PdfSaveOptions { UseObjectStreams = false })), StringComparison.Ordinal);
        Assert.Throws<InvalidOperationException>(() => document.ToByteArray(new PdfSaveOptions { UseObjectStreams = true, Version = PdfVersion.Pdf14 }));
    }

    [Fact]
    public void FileIdentifierKeepsItsFirstPart()
    {
        var file = HandWrittenPdf.CreateSimple();
        file.Trailer = "<< /Size {size} /Root 1 0 R /ID [<0102> <0304>] >>";

        var saved = PdfDocument.Open(file.ToArray()).ToByteArray();
        var id = PdfDocument.Open(saved).Trailer.Get<PdfArray>(PdfName.Create("ID"))!;

        Assert.Equal(new byte[] { 1, 2 }, id.GetResolved(0) is PdfString first ? first.Bytes.ToArray() : null);
        Assert.Equal(16, ((PdfString)id.GetResolved(1)).Bytes.Length);
    }

    [Fact]
    public void IncrementalSaveOfAReplacedStreamWritesIt()
    {
        var file = HandWrittenPdf.CreateSimple();
        file.Add(6, HandWrittenPdf.Stream("AAAA"));
        file.Add(7, HandWrittenPdf.Stream("BBBB"));
        file.Trailer = "<< /Size {size} /Root 1 0 R /A 6 0 R /B 7 0 R >>";
        var original = file.ToArray();
        var document = PdfDocument.Open(original);
        Assert.True(document.Objects.TryGetReference(6, out var first));
        Assert.True(document.Objects.TryGetReference(7, out var second));
        var replacement = (PdfStream)document.Objects.Resolve(second);
        _ = document.Objects.Resolve(first);

        // Both streams have the same dictionary and the same version of their data: only their bytes differ
        document.Objects.Set(first, replacement);
        using var output = new MemoryStream();
        document.SaveIncremental(output, Uncompressed);

        var read = PdfDocument.Open(output.ToArray());
        Assert.True(read.Objects.TryGetReference(6, out var updated));
        Assert.Equal("BBBB"u8.ToArray(), ((PdfStream)read.Objects.Resolve(updated)).GetDecodedData().ToArray());
    }

    [Fact]
    public void IncrementalSaveOfAStreamChangedInPlaceWritesIt()
    {
        var original = HandWrittenPdf.CreateSimple().ToArray();
        var document = PdfDocument.Open(original);
        Assert.True(document.Objects.TryGetReference(5, out var content));
        ((PdfStream)document.Objects.Resolve(content)).SetData("BT /F1 12 Tf 72 720 Td (World) Tj ET"u8);

        using var output = new MemoryStream();
        document.SaveIncremental(output, Uncompressed);

        Assert.Equal("World", PdfDocument.Open(output.ToArray()).Pages[0].ExtractText());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void TrailerEntriesArePreserved(bool xrefStream)
    {
        var file = HandWrittenPdf.CreateSimple();
        file.UseXrefStream = xrefStream;
        file.Trailer = "<< /Size {size} /Root 1 0 R /Private (secret) /PrivateRef 4 0 R >>";
        var document = PdfDocument.Open(file.ToArray());
        document.Information.Title = "Changed";

        using var output = new MemoryStream();
        document.SaveIncremental(output, Uncompressed);
        var updated = PdfDocument.Open(output.ToArray());
        var full = PdfDocument.Open(document.ToByteArray());

        foreach (var read in new[] { updated, full })
        {
            Assert.Equal("Changed", read.Information.Title);
            Assert.Equal("secret", read.Trailer.Get<PdfString>(PdfName.Create("Private"))!.ToText());
            Assert.Equal("Helvetica", read.Trailer.Get<PdfDictionary>(PdfName.Create("PrivateRef"))!.GetName(PdfName.Create("BaseFont"))!.Value);
            Assert.False(read.Trailer.ContainsKey(PdfName.Create("W")));
            Assert.False(read.Trailer.ContainsKey(PdfName.Create("Prev")));
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void IncrementalSaveWritesFreeEntriesForRemovedObjects(bool xrefStream)
    {
        var file = HandWrittenPdf.CreateSimple();
        file.Add(6, "<< /Removed true >>");
        file.UseXrefStream = xrefStream;
        file.Trailer = "<< /Size {size} /Root 1 0 R /Private 6 0 R >>";
        var original = file.ToArray();
        var document = PdfDocument.Open(original);
        Assert.True(document.Objects.TryGetReference(6, out var removed));

        Assert.True(document.Objects.Remove(removed));
        Assert.False(document.Objects.Remove(removed));
        Assert.Same(PdfNull.Instance, document.Objects.Resolve(removed));
        Assert.False(document.Objects.TryGetReference(6, out _));
        var added = document.Objects.Add(new PdfDictionary { [PdfName.Create("Added")] = PdfBoolean.True });
        Assert.True(added.ObjectNumber > 6);
        document.Catalog[PdfName.Create("Added")] = added;

        using var output = new MemoryStream();
        document.SaveIncremental(output, Uncompressed);
        var updated = output.ToArray();

        Assert.Equal(original, updated.AsSpan(0, original.Length).ToArray());
        if (!xrefStream)
        {
            Assert.Contains("\n0000000000 00001 f \n", Encoding.Latin1.GetString(updated.AsSpan(original.Length)), StringComparison.Ordinal);
        }

        var read = PdfDocument.Open(updated);
        Assert.False(read.IsRepaired);
        Assert.False(read.Objects.TryGetReference(6, out _));
        Assert.Null(read.Trailer.GetResolved(PdfName.Create("Private")));
        Assert.Equal(PdfBoolean.True, read.Catalog.Get<PdfDictionary>(PdfName.Create("Added"))!.GetResolved(PdfName.Create("Added")));
        Assert.Equal("Hello", read.Pages[0].ExtractText());

        // The earlier revision keeps the object
        Assert.True(PdfDocument.Open(original).Objects.TryGetReference(6, out _));

        // A new object of the updated file does not take the free number
        Assert.True(read.Objects.Add(PdfBoolean.True).ObjectNumber > added.ObjectNumber);
    }

    [Fact]
    public void RemovedObjectsAreWrittenAsNullInAFullSave()
    {
        var document = new PdfDocument();
        var removed = document.Objects.Add(new PdfDictionary { [PdfName.Create("Removed")] = PdfBoolean.True });
        document.Catalog[PdfName.Create("Private")] = removed;

        Assert.True(document.Objects.Remove(removed));
        var text = Encoding.Latin1.GetString(document.ToByteArray(Uncompressed));

        Assert.Contains("/Private null", text, StringComparison.Ordinal);
        Assert.DoesNotContain("/Removed", text, StringComparison.Ordinal);
    }

    [Fact]
    public void ReferencesToAnotherDocumentAreRejected()
    {
        var other = new PdfDocument();
        var foreign = other.Objects.Add(new PdfDictionary { [PdfName.Create("Foreign")] = PdfBoolean.True });
        var document = PdfDocument.Open(HandWrittenPdf.CreateSimple().ToArray());
        document.Catalog[PdfName.Create("Private")] = foreign;

        using var output = new MemoryStream();
        var incremental = Assert.Throws<InvalidOperationException>(() => document.SaveIncremental(output));
        var full = Assert.Throws<InvalidOperationException>(() => document.ToByteArray());

        Assert.Contains("another document", incremental.Message, StringComparison.Ordinal);
        Assert.Contains("another document", full.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void NamesWithANullCharacterAreRejected()
    {
        Assert.Throws<ArgumentException>(() => PdfName.Create("A\0B"));
        Assert.Throws<ArgumentException>(() => new PdfName("A\0B"));

        var file = HandWrittenPdf.CreateSimple();
        file.Trailer = "<< /Size {size} /Root 1 0 R /Private /A#00B /Escaped /A#20B >>";
        var document = PdfDocument.Open(file.ToArray());
        Assert.Equal("A\0B", document.Trailer.GetName(PdfName.Create("Private"))!.Value);

        var exception = Assert.Throws<InvalidOperationException>(() => document.ToByteArray(Uncompressed));
        Assert.Contains("/A#00B", exception.Message, StringComparison.Ordinal);

        document.Trailer.Remove(PdfName.Create("Private"));
        var text = Encoding.Latin1.GetString(document.ToByteArray(Uncompressed));
        Assert.Contains("/Escaped /A#20B", text, StringComparison.Ordinal);
    }

    [Fact]
    public void ObjectsCanBeAddedToAFileWithAnOversizedSize()
    {
        var file = HandWrittenPdf.CreateSimple();
        file.Trailer = "<< /Size 2147483647 /Root 1 0 R >>";
        var document = PdfDocument.Open(file.ToArray());

        document.Pages.Add(PdfSize.A4).CreateContentBuilder().DrawText("Added", PdfFont.GetStandardFont(PdfStandardFont.Helvetica), 12, 72, 700);
        document.Objects.Add(PdfBoolean.True);

        using var output = new MemoryStream();
        document.SaveIncremental(output);
        var updated = PdfDocument.Open(output.ToArray());
        Assert.False(updated.IsRepaired);
        Assert.Equal(2, updated.Pages.Count);
        Assert.Equal("Added", updated.Pages[1].ExtractText());
        Assert.Equal(2, PdfDocument.Open(document.ToByteArray()).Pages.Count);
    }

    [Fact]
    public void AddingAnObjectWithoutNumberLeftThrows()
    {
        var original = HandWrittenPdf.CreateSimple().ToArray();
        var file = HandWrittenPdf.AppendUpdate(original, [(2147483645, "true")], "<< /Size 2147483646 /Root 1 0 R /Prev {prev} >>");
        var document = PdfDocument.Open(file);
        Assert.False(document.IsRepaired);

        var exception = Assert.Throws<InvalidOperationException>(() => document.Objects.Add(PdfBoolean.True));

        Assert.Contains("No object number is left", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void IncrementalSaveOfACrossReferenceStreamUsesObjectStreams()
    {
        var file = HandWrittenPdf.CreateSimple();
        file.UseXrefStream = true;
        var original = file.ToArray();
        var document = PdfDocument.Open(original);
        document.Information.Title = "Changed";
        document.Pages[0].CreateContentBuilder().DrawText("World", PdfFont.GetStandardFont(PdfStandardFont.Helvetica), 12, 72, 700);

        using var output = new MemoryStream();
        document.SaveIncremental(output, new PdfSaveOptions { CompressStreams = false });
        var appended = Encoding.Latin1.GetString(output.ToArray().AsSpan(original.Length));
        using var withoutObjectStreams = new MemoryStream();
        document.SaveIncremental(withoutObjectStreams, Uncompressed);

        Assert.Contains("/Type /ObjStm", appended, StringComparison.Ordinal);
        Assert.DoesNotContain("/Type /ObjStm", Encoding.Latin1.GetString(withoutObjectStreams.ToArray().AsSpan(original.Length)), StringComparison.Ordinal);
        foreach (var bytes in new[] { output.ToArray(), withoutObjectStreams.ToArray() })
        {
            var read = PdfDocument.Open(bytes);
            Assert.False(read.IsRepaired);
            Assert.Equal("Changed", read.Information.Title);
            Assert.Equal("Hello\nWorld", read.Pages[0].ExtractText());
        }

        // A table-based file keeps writing the objects one by one
        var table = HandWrittenPdf.CreateSimple().ToArray();
        var tableDocument = PdfDocument.Open(table);
        tableDocument.Information.Title = "Changed";
        using var tableOutput = new MemoryStream();
        tableDocument.SaveIncremental(tableOutput);
        Assert.DoesNotContain("/ObjStm", Encoding.Latin1.GetString(tableOutput.ToArray().AsSpan(table.Length)), StringComparison.Ordinal);
    }

    [Fact]
    public void IncrementalObjectStreamsOfAnEncryptedDocument()
    {
        var source = new PdfDocument { Encryption = new PdfEncryption("user", "owner") { Algorithm = PdfEncryptionAlgorithm.AesWith128BitKey } };
        source.Pages.Add(PdfSize.A4).CreateContentBuilder().DrawText("Hello", PdfFont.GetStandardFont(PdfStandardFont.Helvetica), 12, 72, 700);
        var original = source.ToByteArray();
        var document = PdfDocument.Open(original, new PdfReadOptions { Password = "user" });
        document.Information.Title = "Secret title";

        using var output = new MemoryStream();
        document.SaveIncremental(output);

        Assert.DoesNotContain("Secret title", Encoding.Latin1.GetString(output.ToArray()), StringComparison.Ordinal);
        var read = PdfDocument.Open(output.ToArray(), new PdfReadOptions { Password = "user" });
        Assert.Equal("Secret title", read.Information.Title);
        Assert.Equal("Hello", read.Pages[0].ExtractText());
    }

    internal static PdfDocument CreateRichDocument()
    {
        var document = new PdfDocument();
        document.Information.Title = "Title \u00E9\u4E2D";
        document.Information.Author = "Author";
        document.Information.CreationDate = new DateTimeOffset(2024, 1, 2, 3, 4, 5, TimeSpan.FromHours(-5));
        var helvetica = PdfFont.GetStandardFont(PdfStandardFont.Helvetica);
        var page1 = document.Pages.Add(PdfSize.A4);
        page1.CreateContentBuilder()
            .BeginText()
            .SetFont(helvetica, 12)
            .SetTextLeading(14)
            .MoveTextPosition(72, 750)
            .ShowText("Page 1")
            .ShowTextOnNextLine("Second line")
            .EndText();
        var page2 = document.Pages.Add(PdfSize.A4);
        page2.CreateContentBuilder().DrawText("Page 2", PdfFont.GetStandardFont(PdfStandardFont.TimesBold), 20, 100, 700);
        var page3 = document.Pages.Add(PdfSize.Letter);
        page3.Rotation = 90;

        var chapter = document.Outlines.Add("Chapter 1", PdfDestination.Fit(page1));
        chapter.IsOpen = true;
        chapter.Children.Add("Section 1.1", PdfDestination.Xyz(page1, 0, 700));
        var chapter2 = document.Outlines.Add("Chapter 2", PdfDestination.FitHorizontally(page2, 500));
        chapter2.IsBold = true;
        chapter2.Color = PdfColor.FromRgb(1, 0, 0);

        page1.Annotations.AddLink(new PdfRectangle(10, 20, 110, 40), new Uri("https://www.meziantou.net/"));
        page1.Annotations.AddLink(new PdfRectangle(10, 50, 110, 70), PdfDestination.Fit(page2));
        return document;
    }

    private sealed class WriteCountingStream : MemoryStream
    {
        public int WriteCount { get; private set; }

        public int LargestWrite { get; private set; }

        public Action? OnWrite { get; init; }

        public override void Write(byte[] buffer, int offset, int count)
        {
            WriteCount++;
            LargestWrite = Math.Max(LargestWrite, count);
            base.Write(buffer, offset, count);
            OnWrite?.Invoke();
        }

        public override void Write(ReadOnlySpan<byte> buffer) => Write(buffer.ToArray(), 0, buffer.Length);

        public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) => WriteAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

        public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Write(buffer.Span);
            return ValueTask.CompletedTask;
        }
    }

    private static string RemoveId(byte[] file)
    {
        var text = Encoding.Latin1.GetString(file);
        var index = text.IndexOf("/ID [", StringComparison.Ordinal);
        return text[..index] + text[text.IndexOf(']', index, StringComparison.Ordinal)..];
    }
}
