using System.Text;
using Meziantou.Framework.Pdf.Objects;
using Meziantou.Framework.Pdf.Parsing;

namespace Meziantou.Framework.Pdf.Tests;

public sealed class PdfReaderTests
{
    [Fact]
    public void ReadsCrossReferenceTable()
    {
        var document = PdfDocument.Open(HandWrittenPdf.CreateSimple().ToArray());

        Assert.False(document.IsRepaired);
        Assert.Equal(PdfVersion.Pdf17, document.Version);
        Assert.Single(document.Pages);
        Assert.Equal(new PdfRectangle(0, 0, 612, 792), document.Pages[0].MediaBox);
        Assert.Equal("Hello", document.Pages[0].ExtractText());
    }

    [Fact]
    public void ReadsCrossReferenceStream()
    {
        var file = HandWrittenPdf.CreateSimple();
        file.UseXrefStream = true;

        var document = PdfDocument.Open(file.ToArray());

        Assert.False(document.IsRepaired);
        Assert.Equal("Hello", document.Pages[0].ExtractText());
    }

    [Fact]
    public void ReadsObjectStreams()
    {
        var file = HandWrittenPdf.CreateSimple().Compress(1, 2, 3, 4);
        file.UseXrefStream = true;

        var document = PdfDocument.Open(file.ToArray());

        Assert.False(document.IsRepaired);
        Assert.Equal("Hello", document.Pages[0].ExtractText());
        Assert.True(document.Catalog.IsType(PdfName.Create("Catalog")));
    }

    [Fact]
    public void ReadsIncrementalUpdates()
    {
        var original = HandWrittenPdf.CreateSimple("Old").ToArray();
        var updated = HandWrittenPdf.AppendUpdate(original, [(5, HandWrittenPdf.Stream("BT /F1 12 Tf 72 720 Td (New) Tj ET")), (6, "<< /Title (Updated) >>")], "<< /Size 7 /Root 1 0 R /Info 6 0 R /Prev {prev} >>");

        var document = PdfDocument.Open(updated);

        Assert.False(document.IsRepaired);
        Assert.Equal("New", document.Pages[0].ExtractText());
        Assert.Equal("Updated", document.Information.Title);
    }

    [Fact]
    public void UpdateWithoutRootInheritsIt()
    {
        var original = HandWrittenPdf.CreateSimple().ToArray();
        var updated = HandWrittenPdf.AppendUpdate(original, [(6, "<< /Title (T) >>")], "<< /Size 7 /Info 6 0 R /Prev {prev} >>");

        var document = PdfDocument.Open(updated);

        Assert.Single(document.Pages);
        Assert.Equal("T", document.Information.Title);
    }

    [Fact]
    public void PreviousSectionWithDamagedTrailerKeepsItsEntries()
    {
        var file = HandWrittenPdf.CreateSimple();
        file.Trailer = "[ /Size {size} /Root 1 0 R";
        var updated = HandWrittenPdf.AppendUpdate(file.ToArray(), [(6, "<< /Title (T) >>")], "<< /Size 7 /Root 1 0 R /Info 6 0 R /Prev {prev} >>");

        var document = PdfDocument.Open(updated);

        Assert.False(document.IsRepaired);
        Assert.Equal("Hello", Assert.Single(document.Pages).ExtractText());
        Assert.Equal("T", document.Information.Title);
    }

    [Fact]
    public void WrongOffsetsAreRecovered()
    {
        var file = HandWrittenPdf.CreateSimple();
        file.OffsetError = 3;

        var document = PdfDocument.Open(file.ToArray());

        Assert.Equal("Hello", document.Pages[0].ExtractText());
        Assert.Contains(PdfDiagnosticKind.WrongObjectOffset, document.Diagnostics.Select(diagnostic => diagnostic.Kind));
        Assert.All(document.Diagnostics, diagnostic => Assert.NotNull(diagnostic.ObjectNumber));
    }

    [Fact]
    public void MissingCrossReferenceIsRebuilt()
    {
        var file = HandWrittenPdf.CreateSimple();
        file.WriteXref = false;
        var bytes = Encoding.Latin1.GetString(file.ToArray()) + "trailer\n<< /Root 1 0 R >>\n";

        var document = PdfDocument.Open(Encoding.Latin1.GetBytes(bytes));

        Assert.True(document.IsRepaired);
        Assert.Equal("Hello", document.Pages[0].ExtractText());
        Assert.Contains(PdfDiagnosticKind.DamagedCrossReferenceSection, document.Diagnostics.Select(diagnostic => diagnostic.Kind));
        Assert.Contains(PdfDiagnosticKind.CrossReferenceRebuilt, document.Diagnostics.Select(diagnostic => diagnostic.Kind));
    }

    [Fact]
    public void MissingTrailerFindsTheCatalog()
    {
        var file = HandWrittenPdf.CreateSimple();
        file.WriteXref = false;

        var document = PdfDocument.Open(file.ToArray());

        Assert.True(document.IsRepaired);
        Assert.Single(document.Pages);
        Assert.Contains(PdfDiagnosticKind.CatalogFoundByScan, document.Diagnostics.Select(diagnostic => diagnostic.Kind));
    }

    [Fact]
    public void BrokenStartXrefIsRebuilt()
    {
        var text = Encoding.Latin1.GetString(HandWrittenPdf.CreateSimple().ToArray());
        text = text.Replace("startxref\n", "startxref\n9", StringComparison.Ordinal);

        var document = PdfDocument.Open(Encoding.Latin1.GetBytes(text));

        Assert.True(document.IsRepaired);
        Assert.Equal("Hello", document.Pages[0].ExtractText());
        Assert.Contains(PdfDiagnosticKind.InvalidStartXref, document.Diagnostics.Select(diagnostic => diagnostic.Kind));
    }

    [Fact]
    public void GarbageBeforeTheHeaderShiftsTheOffsets()
    {
        var file = HandWrittenPdf.CreateSimple().ToArray();
        var shifted = Encoding.ASCII.GetBytes("garbage\n").Concat(file).ToArray();

        var document = PdfDocument.Open(shifted);

        Assert.Equal("Hello", document.Pages[0].ExtractText());
        var header = Assert.Single(document.Diagnostics, diagnostic => diagnostic.Kind == PdfDiagnosticKind.HeaderNotAtStart);
        Assert.Equal(8, header.Offset);
        Assert.Equal(PdfDiagnosticSeverity.Information, header.Severity);
        Assert.Contains(PdfDiagnosticKind.WrongObjectOffset, document.Diagnostics.Select(diagnostic => diagnostic.Kind));
    }

    [Fact]
    public void LaterDefinitionOfAnObjectWinsInRecovery()
    {
        var text = Encoding.Latin1.GetString(HandWrittenPdf.CreateSimple("First").ToArray());
        text = text[..text.IndexOf("xref", StringComparison.Ordinal)] + "5 0 obj\n" + HandWrittenPdf.Stream("BT /F1 12 Tf 72 720 Td (Second) Tj ET") + "\nendobj\n";

        var document = PdfDocument.Open(Encoding.Latin1.GetBytes(text));

        Assert.True(document.IsRepaired);
        Assert.Equal("Second", document.Pages[0].ExtractText());
    }

    [Fact]
    public void TableThatStartsAtOneIsShifted()
    {
        var text = Encoding.Latin1.GetString(HandWrittenPdf.CreateSimple().ToArray());
        text = text.Replace("xref\n0 6\n", "xref\n1 6\n", StringComparison.Ordinal);

        var document = PdfDocument.Open(Encoding.Latin1.GetBytes(text));

        Assert.False(document.IsRepaired);
        Assert.Equal("Hello", document.Pages[0].ExtractText());
        Assert.Equal([PdfDiagnosticKind.ShiftedCrossReferenceTable], document.Diagnostics.Select(diagnostic => diagnostic.Kind));
    }

    [Fact]
    public void HeaderVersion()
    {
        var file = HandWrittenPdf.CreateSimple();
        file.Header = "%PDF-1.4\n%\u00E2\u00E3\u00CF\u00D3\n";

        Assert.Equal(PdfVersion.Pdf14, PdfDocument.Open(file.ToArray()).Version);
    }

    [Fact]
    public void CatalogVersionOverridesTheHeader()
    {
        var file = new HandWrittenPdf()
            .Add(1, "<< /Type /Catalog /Pages 2 0 R /Version /2.0 >>")
            .Add(2, "<< /Type /Pages /Kids [] /Count 0 >>");
        file.Header = "%PDF-1.4\n";

        Assert.Equal(PdfVersion.Pdf20, PdfDocument.Open(file.ToArray()).Version);
    }

    [Fact]
    public void NotAPdfThrows()
    {
        Assert.Throws<InvalidPdfContentException>(() => PdfDocument.Open("not a pdf"u8.ToArray()));
        Assert.Throws<InvalidPdfContentException>(() => PdfDocument.Open("%PDF-1.7\nnothing"u8.ToArray()));
    }

    [Theory]
    [InlineData("<< /Filter /Adobe.PubSec /V 4 /R 4 >>", typeof(UnsupportedPdfFeatureException))]
    [InlineData("<< /Filter /Adobe.PubSec /SubFilter /adbe.pkcs7.s5 /V 3 >>", typeof(UnsupportedPdfFeatureException))]
    [InlineData("<< /Filter /Adobe.PubSec /SubFilter /adbe.pkcs7.s5 /V 5 /CF << /DefaultCryptFilter << /CFM /AESV3 >> >> /StmF /DefaultCryptFilter /StrF /DefaultCryptFilter >>", typeof(InvalidPdfContentException))]
    [InlineData("<< /Filter /Adobe.PubSec /SubFilter /adbe.pkcs7.s5 /V 5 /CF << /DefaultCryptFilter << /CFM /AESV3 /Recipients [<0102>] >> >> /StmF /DefaultCryptFilter /StrF /DefaultCryptFilter >>", typeof(PdfPasswordException))]
    [InlineData("<< /Filter /Unknown >>", typeof(UnsupportedPdfFeatureException))]
    [InlineData("<< /Filter /Standard /V 3 /R 3 >>", typeof(UnsupportedPdfFeatureException))]
    [InlineData("<< /Filter /Standard /V 1 /R 2 >>", typeof(InvalidPdfContentException))]
    [InlineData("<< /Filter /Standard /V 1 /R 2 /O <00> /U <00> /P -4 >>", typeof(InvalidPdfContentException))]
    [InlineData("<< /Filter /Standard /V 4 /R 4 /StmF /Unknown /O <00> /U <00> /P -4 >>", typeof(InvalidPdfContentException))]
    public void UnsupportedOrInvalidEncryptionIsReported(string encrypt, Type exception)
    {
        var file = HandWrittenPdf.CreateSimple();
        file.Trailer = "<< /Size {size} /Root 1 0 R /Encrypt " + encrypt + " >>";

        Assert.Equal(exception, Assert.ThrowsAny<PdfException>(() => PdfDocument.Open(file.ToArray())).GetType());
    }

    [Fact]
    public void ReferencesToMissingObjectsAreNull()
    {
        var document = PdfDocument.Open(HandWrittenPdf.CreateSimple().ToArray());
        Assert.True(document.Objects.TryGetReference(1, out var catalog));

        Assert.Same(PdfNull.Instance, document.Objects.Resolve(document.Objects.GetOrCreateReference(99, 0)));
        Assert.IsType<PdfDictionary>(catalog.Resolve());
    }

    [Fact]
    public void PageTreeCyclesAndInheritance()
    {
        var file = new HandWrittenPdf()
            .Add(1, "<< /Type /Catalog /Pages 2 0 R >>")
            .Add(2, "<< /Type /Pages /Kids [3 0 R 4 0 R 2 0 R] /Count 2 /MediaBox [0 0 200 300] /Rotate 90 >>")
            .Add(3, "<< /Type /Page /Parent 2 0 R >>")
            .Add(4, "<< /Parent 2 0 R /MediaBox [0 0 100 100] /Rotate -90 >>");

        var document = PdfDocument.Open(file.ToArray());

        Assert.Equal(2, document.Pages.Count);
        Assert.Equal(new PdfRectangle(0, 0, 200, 300), document.Pages[0].MediaBox);
        Assert.Equal(90, document.Pages[0].Rotation);
        Assert.Equal(new PdfRectangle(0, 0, 100, 100), document.Pages[1].MediaBox);
        Assert.Equal(270, document.Pages[1].Rotation);
        Assert.Equal(document.Pages[0].MediaBox, document.Pages[0].CropBox);
        var cycle = Assert.Single(document.Diagnostics);
        Assert.Equal(PdfDiagnosticKind.InvalidPageTreeNode, cycle.Kind);
        Assert.Equal(2, cycle.ObjectNumber);
    }

    [Fact]
    public void StreamLengthThatIsAnIndirectObject()
    {
        var file = new HandWrittenPdf()
            .Add(1, "<< /Type /Catalog /Pages 2 0 R >>")
            .Add(2, "<< /Type /Pages /Kids [3 0 R] /Count 1 >>")
            .Add(3, "<< /Type /Page /Parent 2 0 R /Contents 4 0 R /Resources << /Font << /F1 6 0 R >> >> >>")
            .Add(4, "<< /Length 5 0 R >>\nstream\nBT /F1 1 Tf (A) Tj ET\nendstream")
            .Add(5, "21")
            .Add(6, "<< /Type /Font /Subtype /Type1 /BaseFont /Courier >>");

        var document = PdfDocument.Open(file.ToArray());

        Assert.Equal("BT /F1 1 Tf (A) Tj ET", Encoding.ASCII.GetString(document.Pages[0].GetContentData()));
        Assert.Equal("A", document.Pages[0].ExtractText());
    }

    [Fact]
    public void SelfReferencingLengthIsNotACycle()
    {
        var file = new HandWrittenPdf()
            .Add(1, "<< /Type /Catalog /Pages 2 0 R >>")
            .Add(2, "<< /Type /Pages /Kids [3 0 R] /Count 1 >>")
            .Add(3, "<< /Type /Page /Parent 2 0 R /Contents 4 0 R >>")
            .Add(4, "<< /Length 4 0 R >>\nstream\nq Q\nendstream");

        var document = PdfDocument.Open(file.ToArray());

        Assert.Equal("q Q", Encoding.ASCII.GetString(document.Pages[0].GetContentData()));
    }

    [Fact]
    public void ChainOfLengthReferencesDoesNotRecurseWithoutBound()
    {
        // Each stream has its length in the next stream object: reading one would read them all, recursively
        var file = new HandWrittenPdf()
            .Add(1, "<< /Type /Catalog /Pages 2 0 R >>")
            .Add(2, "<< /Type /Pages /Kids [3 0 R] /Count 1 >>")
            .Add(3, "<< /Type /Page /Parent 2 0 R /Contents 4 0 R >>");
        const int Count = 5000;
        for (var i = 0; i < Count; i++)
        {
            file.Add(4 + i, "<< /Length " + (5 + i).ToString(System.Globalization.CultureInfo.InvariantCulture) + " 0 R >>\nstream\nq Q\nendstream");
        }

        var document = PdfDocument.Open(file.ToArray());

        Assert.Equal("q Q", Encoding.ASCII.GetString(document.Pages[0].GetContentData()));
    }

    [Fact]
    public void NullEntryOfANewerTrailerInheritsTheOlderEntry()
    {
        var original = HandWrittenPdf.CreateSimple().ToArray();
        original = HandWrittenPdf.AppendUpdate(original, [(6, "<< /Title (Old) >>")], "<< /Size 7 /Root 1 0 R /Info 6 0 R /Prev {prev} >>");
        var updated = HandWrittenPdf.AppendUpdate(original, [(4, "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica /Encoding /WinAnsiEncoding >>")], "<< /Size 7 /Root 1 0 R /Info null /Prev {prev} >>");

        var document = PdfDocument.Open(updated);

        Assert.False(document.IsRepaired);
        Assert.Equal("Old", document.Information.Title);
        Assert.IsType<PdfReference>(document.Trailer[PdfNames.Info]);
    }

    [Fact]
    public void NullEntryOfTheOnlyTrailerIsAbsent()
    {
        var file = HandWrittenPdf.CreateSimple();
        file.Trailer = "<< /Size {size} /Root 1 0 R /Info null >>";

        var document = PdfDocument.Open(file.ToArray());

        Assert.False(document.Trailer.ContainsKey(PdfNames.Info));
    }

    [Fact]
    public void WrongIndexesInObjectStreamsAreFound()
    {
        const int Count = 2000;
        var file = new HandWrittenPdf()
            .Add(1, "<< /Type /Catalog /Pages 2 0 R >>")
            .Add(2, "<< /Type /Pages /Kids [] /Count 0 >>");
        for (var i = 0; i < Count; i++)
        {
            file.Add(10 + i, (i * 3).ToString(System.Globalization.CultureInfo.InvariantCulture));
        }

        file.Compress([.. Enumerable.Range(10, Count)]);
        file.UseXrefStream = true;
        file.ContainerIndexError = 1;

        var document = PdfDocument.Open(file.ToArray());

        for (var i = 0; i < Count; i++)
        {
            Assert.True(document.Objects.TryGetReference(10 + i, out var reference));
            Assert.Equal(i * 3, Assert.IsType<PdfInteger>(reference.Resolve()).Value);
        }

        Assert.Contains(PdfDiagnosticKind.WrongObjectStreamIndex, document.Diagnostics.Select(diagnostic => diagnostic.Kind));
    }

    [Fact]
    public void ObjectStreamCacheIsBoundedByCountAndBytes()
    {
        var cache = new ObjectStreamCache(maxCount: 3, maxBytes: 100);
        for (var i = 0; i < 10; i++)
        {
            cache.Add(i, CreateObjectStream(10));
            Assert.True(cache.TryGetValue(i, out _));
            Assert.InRange(cache.Count, 1, 3);
        }

        // A large stream evicts streams one at a time until it fits, and is kept even when it is alone over the budget
        cache.Add(20, CreateObjectStream(85));
        Assert.True(cache.TryGetValue(20, out _));
        Assert.InRange(cache.Bytes, 85, 100);
        cache.Add(21, CreateObjectStream(200));
        Assert.Equal(1, cache.Count);
        Assert.Equal(200, cache.Bytes);
        Assert.True(cache.TryGetValue(21, out _));

        static ObjectStreamData CreateObjectStream(int length) => new(new byte[length], 0, []);
    }

    [Fact]
    public void ObjectStreamCacheKeepsMostStreamsWhenAccessesCycleOverMoreThanTheCapacity()
    {
        // Clearing the cache, or evicting the least recently used stream, misses every access of a cycle over one more
        // stream than the capacity: one stream at a time is evicted, at random, so that most accesses hit
        var cache = new ObjectStreamCache(maxCount: 64, maxBytes: long.MaxValue);
        var misses = 0;
        for (var cycle = 0; cycle < 10; cycle++)
        {
            for (var i = 0; i < 65; i++)
            {
                if (!cache.TryGetValue(i, out _))
                {
                    misses++;
                    cache.Add(i, new ObjectStreamData([], 0, []));
                }
            }
        }

        Assert.InRange(misses, 65, 65 * 5);
    }

    [Fact]
    public void ObjectsOfMoreObjectStreamsThanTheCacheHoldsAreRead()
    {
        // 65 object streams, one more than the cache holds
        var file = new HandWrittenPdf()
            .Add(1, "<< /Type /Catalog /Pages 2 0 R >>")
            .Add(2, "<< /Type /Pages /Kids [] /Count 0 >>");
        var text = new StringBuilder(Encoding.Latin1.GetString(file.ToArray()));
        text.Length = text.ToString().IndexOf("xref", StringComparison.Ordinal);
        for (var i = 0; i < 65; i++)
        {
            var container = 100 + i;
            var contained = 1000 + i;
            var content = string.Create(System.Globalization.CultureInfo.InvariantCulture, $"{contained} 0 {i}");
            text.Append(System.Globalization.CultureInfo.InvariantCulture, $"{container} 0 obj\n{HandWrittenPdf.Stream(content, " /Type /ObjStm /N 1 /First " + (content.Length - i.ToString(System.Globalization.CultureInfo.InvariantCulture).Length))}\nendobj\n");
        }

        var document = PdfDocument.Open(Encoding.Latin1.GetBytes(text.ToString()));

        Assert.True(document.IsRepaired);
        for (var cycle = 0; cycle < 3; cycle++)
        {
            for (var i = 0; i < 65; i++)
            {
                Assert.True(document.Objects.TryGetReference(1000 + i, out var reference));
                Assert.Equal(i, Assert.IsType<PdfInteger>(reference.Resolve()).Value);
            }
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void EncryptedFileWithBrokenCrossReferenceIsDecrypted(bool objectStreams)
    {
        var document = new PdfDocument();
        document.Pages.Add(new PdfSize(200, 200));
        document.Information.Title = "Secret title";
        document.Encryption = new PdfEncryption("user", "owner") { Algorithm = PdfEncryptionAlgorithm.AesWith256BitKey };
        var text = Encoding.Latin1.GetString(document.ToByteArray(new PdfSaveOptions { UseObjectStreams = objectStreams }));
        var damaged = Encoding.Latin1.GetBytes(text.Replace("startxref", "startxreX", StringComparison.Ordinal));

        var read = PdfDocument.Open(damaged, new PdfReadOptions { Password = "user" });

        Assert.True(read.IsRepaired);
        Assert.True(read.IsEncrypted);
        Assert.Equal("Secret title", read.Information.Title);
        Assert.Single(read.Pages);
        Assert.Equal("Secret title", PdfDocument.Open(read.ToByteArray(), new PdfReadOptions { Password = "user" }).Information.Title);
    }

    [Fact]
    public void ReferenceWithAnotherGenerationResolvesInARepairedFileThatDefinesOneGeneration()
    {
        var text = Encoding.Latin1.GetString(HandWrittenPdf.CreateSimple().ToArray());
        text = text[..text.IndexOf("xref", StringComparison.Ordinal)] + "trailer\n<< /Root 1 1 R >>\n";

        var document = PdfDocument.Open(Encoding.Latin1.GetBytes(text));

        Assert.True(document.IsRepaired);
        Assert.Equal("Hello", document.Pages[0].ExtractText());
        Assert.Contains(PdfDiagnosticKind.GenerationMismatch, document.Diagnostics.Select(diagnostic => diagnostic.Kind));
    }

    [Fact]
    public void ReferenceWithAnotherGenerationIsNullWhenTheRepairedFileDefinesSeveralGenerations()
    {
        var text = Encoding.Latin1.GetString(HandWrittenPdf.CreateSimple().ToArray());
        text = text[..text.IndexOf("xref", StringComparison.Ordinal)] + "6 0 obj\n(Zero)\nendobj\n6 1 obj\n(One)\nendobj\ntrailer\n<< /Root 1 0 R >>\n";

        var document = PdfDocument.Open(Encoding.Latin1.GetBytes(text));

        Assert.True(document.IsRepaired);
        Assert.Equal("One", Assert.IsType<PdfString>(document.Objects.GetOrCreateReference(6, 1).Resolve()).ToText());
        Assert.Same(PdfNull.Instance, document.Objects.GetOrCreateReference(6, 2).Resolve());
    }

    [Fact]
    public void ReferenceWithAnotherGenerationIsNullInAFileThatIsNotRepaired()
    {
        var document = PdfDocument.Open(HandWrittenPdf.CreateSimple().ToArray());

        Assert.False(document.IsRepaired);
        Assert.Same(PdfNull.Instance, document.Objects.GetOrCreateReference(4, 1).Resolve());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ObjectsInsideTheDataOfAStreamAreNotRecoveredObjects(bool indirectLength)
    {
        // An uncompressed embedded file whose bytes look like objects and a trailer, after the objects of the document: a
        // scan that keeps the last definition would take its catalog and its content for the document's
        var embedded = "%PDF-1.7\n1 0 obj\n<< /Type /Catalog /Pages 9 0 R >>\nendobj\n5 0 obj\n<< /Length 3 >>\nstream\nq Q\nendstream\nendobj\ntrailer\n<< /Root 1 0 R >>\n%%EOF\n";
        var text = Encoding.Latin1.GetString(HandWrittenPdf.CreateSimple().ToArray());
        text = text[..text.IndexOf("xref", StringComparison.Ordinal)];
        text += indirectLength
            ? "7 0 obj\n" + embedded.Length.ToString(System.Globalization.CultureInfo.InvariantCulture) + "\nendobj\n6 0 obj\n<< /Type /EmbeddedFile /Length 7 0 R >>\nstream\n" + embedded + "\nendstream\nendobj\n"
            : "6 0 obj\n" + HandWrittenPdf.Stream(embedded, " /Type /EmbeddedFile") + "\nendobj\n";

        var document = PdfDocument.Open(Encoding.Latin1.GetBytes(text));

        Assert.True(document.IsRepaired);
        Assert.Equal("Hello", document.Pages[0].ExtractText());
        Assert.True(document.Objects.TryGetReference(6, out var reference));
        Assert.Equal(embedded, Encoding.Latin1.GetString(Assert.IsType<PdfStream>(reference.Resolve()).GetDecodedData()));
    }

    [Theory]
    [InlineData("abc\nendobj\n")]
    [InlineData("abc\n")]
    public void MissingEndstreamDoesNotConsumeTheNextObjects(string afterData)
    {
        var text = Encoding.Latin1.GetString(HandWrittenPdf.CreateSimple().ToArray());
        text = text[..text.IndexOf("xref", StringComparison.Ordinal)]
            + "6 0 obj\n<< /Length 3 >>\nstream\n" + afterData
            + "7 0 obj\n<< /Length 3 >>\nstream\nxyz\nendstream\nendobj\ntrailer\n<< /Root 1 0 R >>\n";

        var document = PdfDocument.Open(Encoding.Latin1.GetBytes(text));

        Assert.True(document.Objects.TryGetReference(6, out var first));
        Assert.True(document.Objects.TryGetReference(7, out var second));
        Assert.Equal("abc", Encoding.Latin1.GetString(Assert.IsType<PdfStream>(first.Resolve()).GetDecodedData()));
        Assert.Equal("xyz", Encoding.Latin1.GetString(Assert.IsType<PdfStream>(second.Resolve()).GetDecodedData()));
        Assert.Contains(document.Diagnostics, diagnostic => diagnostic.ObjectNumber == 6 && diagnostic.Kind is PdfDiagnosticKind.MissingEndStream or PdfDiagnosticKind.InvalidStreamLength);
    }

    [Fact]
    public void MissingEndstreamIsNotCompletedByAnIncrementalUpdate()
    {
        // The object 6 and the cross-reference stream have a wrong /Length and no endstream keyword: their data ends at
        // their endobj keyword, not at the endstream keyword of the stream an update appends
        var file = HandWrittenPdf.CreateSimple();
        file.UseXrefStream = true;
        file.Add(6, "<< /Length 2 >>\nstream\nabc");
        var text = Encoding.Latin1.GetString(file.ToArray());
        var xref = text.LastIndexOf("/Type /XRef", StringComparison.Ordinal);
        var length = text.IndexOf("/Length ", xref, StringComparison.Ordinal) + "/Length ".Length;
        var lengthEnd = text.IndexOf(">>", length, StringComparison.Ordinal);
        var declaredLength = int.Parse(text[length..lengthEnd], System.Globalization.CultureInfo.InvariantCulture) + 3;
        var endstream = text.IndexOf("\nendstream", lengthEnd, StringComparison.Ordinal);
        text = text[..length] + declaredLength.ToString(System.Globalization.CultureInfo.InvariantCulture) + text[lengthEnd..endstream] + text[(endstream + "\nendstream".Length)..];
        var original = Encoding.Latin1.GetBytes(text);
        var updated = HandWrittenPdf.AppendUpdate(original, [(8, HandWrittenPdf.Stream("xyz"))], "<< /Size 9 /Root 1 0 R /Prev {prev} >>");

        foreach (var data in (byte[][])[original, updated])
        {
            var document = PdfDocument.Open(data);

            Assert.False(document.IsRepaired);
            Assert.Equal("Hello", document.Pages[0].ExtractText());
            Assert.True(document.Objects.TryGetReference(6, out var reference));
            Assert.Equal("abc", Encoding.Latin1.GetString(Assert.IsType<PdfStream>(reference.Resolve()).GetDecodedData()));
        }

        Assert.Equal([original.Length, updated.Length], PdfDocument.Open(updated).Revisions.Select(revision => revision.Length));
    }

    [Fact]
    public void WrongDeclaredLengthStillEndsAtEndstream()
    {
        var file = HandWrittenPdf.CreateSimple();
        file.Add(6, "<< /Length 1 >>\nstream\nabc\nendstream");

        var document = PdfDocument.Open(file.ToArray());

        Assert.True(document.Objects.TryGetReference(6, out var reference));
        Assert.Equal("abc", Encoding.Latin1.GetString(Assert.IsType<PdfStream>(reference.Resolve()).GetDecodedData()));
        var length = Assert.Single(document.Diagnostics);
        Assert.Equal(PdfDiagnosticKind.InvalidStreamLength, length.Kind);
        Assert.Equal(6, length.ObjectNumber);
        Assert.Equal(PdfDiagnosticSeverity.Warning, length.Severity);
        Assert.StartsWith("Warning InvalidStreamLength (object 6, offset ", length.ToString());
    }

    [Fact]
    public void RecoveryOfUnterminatedStringsIsBoundedByTheNextObject()
    {
        const int Count = 20_000;
        var text = new StringBuilder(Encoding.Latin1.GetString(HandWrittenPdf.CreateSimple().ToArray()));
        text.Length = text.ToString().IndexOf("xref", StringComparison.Ordinal);
        for (var i = 0; i < Count; i++)
        {
            text.Append(System.Globalization.CultureInfo.InvariantCulture, $"{100 + i} 0 obj (\n");
        }

        for (var i = 0; i < Count; i++)
        {
            text.Append("trailer (\n");
        }

        text.Append("trailer\n<< /Root 1 0 R >>\n");

        var document = PdfDocument.Open(Encoding.Latin1.GetBytes(text.ToString()));

        Assert.True(document.IsRepaired);
        Assert.Equal("Hello", document.Pages[0].ExtractText());

        // Each damaged string ends where the next object starts, instead of at the end of the file
        for (var i = 0; i < Count; i++)
        {
            Assert.True(document.Objects.TryGetReference(100 + i, out var reference));
            Assert.Equal("\n", Assert.IsType<PdfString>(reference.Resolve()).ToText());
        }
    }

    [Fact]
    public void RecoveryOfStreamsWithoutTerminatorsIsBoundedByTheNextObject()
    {
        const int Count = 20_000;
        var text = new StringBuilder(Encoding.Latin1.GetString(HandWrittenPdf.CreateSimple().ToArray()));
        text.Length = text.ToString().IndexOf("xref", StringComparison.Ordinal);
        text.Append("trailer\n<< /Root 1 0 R >>\n");
        for (var i = 0; i < Count; i++)
        {
            text.Append(System.Globalization.CultureInfo.InvariantCulture, $"{100 + i} 0 obj<<>>stream\nxx\n");
        }

        var document = PdfDocument.Open(Encoding.Latin1.GetBytes(text.ToString()));

        Assert.True(document.IsRepaired);
        Assert.Equal("Hello", document.Pages[0].ExtractText());
        for (var i = 0; i < Count; i++)
        {
            Assert.True(document.Objects.TryGetReference(100 + i, out var reference));
            Assert.Equal("xx", Encoding.Latin1.GetString(Assert.IsType<PdfStream>(reference.Resolve()).GetDecodedData()));
        }
    }

    [Fact]
    public void RepairDoesNotLoadTheObjects()
    {
        const int Count = 1000;
        var file = HandWrittenPdf.CreateSimple();
        for (var i = 0; i < Count; i++)
        {
            file.Add(100 + i, "<< /Type /Annot /Subtype /Text /Contents (Note) >>");
        }

        file.WriteXref = false;

        var document = PdfDocument.Open(file.ToArray());

        Assert.True(document.IsRepaired);
        Assert.Equal(Count + 5, document.Objects.Count);
        Assert.InRange(document.Objects.Entries.Count(e => e.State == IndirectObjectState.Loaded), 1, 2);
        Assert.Equal("Hello", document.Pages[0].ExtractText());
    }

    [Fact]
    public void RepairFindsTheCatalogOfAnObjectStream()
    {
        var file = HandWrittenPdf.CreateSimple().Compress(1, 2, 3, 4);
        file.UseXrefStream = true;
        var text = Encoding.Latin1.GetString(file.ToArray()).Replace("startxref", "startxreX", StringComparison.Ordinal);

        var document = PdfDocument.Open(Encoding.Latin1.GetBytes(text));

        Assert.True(document.IsRepaired);
        Assert.Equal("Hello", document.Pages[0].ExtractText());
    }

    [Fact]
    public void UnterminatedDictionaryEndsAtTheNextObjectInRecovery()
    {
        var text = "%PDF-1.4\n1 0 obj\n<< /Type /Catalog /Pages 2 0 R\n2 0 obj\n<< /Type /Pages /Kids [3 0 R] /Count 1\n3 0 obj\n<< /Type /Page /Parent 2 0 R /MediaBox [0 0 100 100\ntrailer\n<< /Root 1 0 R\n";

        var document = PdfDocument.Open(Encoding.Latin1.GetBytes(text));

        Assert.True(document.IsRepaired);
        Assert.Single(document.Pages);
        Assert.Equal(new PdfRectangle(0, 0, 100, 100), document.Pages[0].MediaBox);
        Assert.Contains(PdfDiagnosticKind.UnclosedContainer, document.Diagnostics.Select(diagnostic => diagnostic.Kind));
    }

    [Fact]
    public void AnIntactFileHasNoDiagnostics()
    {
        var document = PdfDocument.Open(HandWrittenPdf.CreateSimple().ToArray(), new PdfReadOptions { LoadAllObjects = true });

        Assert.Empty(document.Diagnostics);
        Assert.Empty(new PdfDocument().Diagnostics);
        Assert.Empty(new PdfDocument().ValidateStructure());
    }

    [Fact]
    public void DiagnosticsGrowAsTheObjectsAreRead()
    {
        var file = HandWrittenPdf.CreateSimple();
        file.Add(6, "<< /Length 1 >>\nstream\nabc\nendstream");

        var document = PdfDocument.Open(file.ToArray());

        Assert.Empty(document.Diagnostics);
        Assert.Equal(PdfDiagnosticKind.InvalidStreamLength, Assert.Single(document.ValidateStructure()).Kind);
        Assert.Equal(PdfDiagnosticKind.InvalidStreamLength, Assert.Single(PdfDocument.Open(file.ToArray(), new PdfReadOptions { LoadAllObjects = true }).Diagnostics).Kind);
    }

    [Fact]
    public void ValidateStructureReportsTheStreamsThatCannotBeDecoded()
    {
        // Flate data whose last block has a reserved type (RFC 1951, 3.2.3): the bytes of the blocks before it are decoded
        using var compressed = new MemoryStream();
        var deflate = new System.IO.Compression.DeflateStream(compressed, System.IO.Compression.CompressionLevel.SmallestSize, leaveOpen: true);
        deflate.Write(Encoding.ASCII.GetBytes(new string('a', 100_000)));
        deflate.Flush();
        var truncated = Encoding.Latin1.GetString([.. compressed.ToArray(), 0x07]);
        deflate.Dispose();
        var file = HandWrittenPdf.CreateSimple();
        file.Add(6, "<< /Length 3 /Filter /Unknown >>\nstream\nabc\nendstream");
        file.Add(7, HandWrittenPdf.Stream(truncated, " /Filter /FlateDecode"));
        file.Add(8, "<< /A missing 5 /B 1 >>");

        var diagnostics = PdfDocument.Open(file.ToArray()).ValidateStructure();

        Assert.Contains(diagnostics, diagnostic => diagnostic is { Kind: PdfDiagnosticKind.UndecodableStream, ObjectNumber: 6, Severity: PdfDiagnosticSeverity.Error });
        Assert.Contains(diagnostics, diagnostic => diagnostic.Kind == PdfDiagnosticKind.DamagedStreamData);
        Assert.Contains(diagnostics, diagnostic => diagnostic is { Kind: PdfDiagnosticKind.UnknownKeyword, ObjectNumber: 8 });
        Assert.Contains(diagnostics, diagnostic => diagnostic is { Kind: PdfDiagnosticKind.InvalidDictionaryKey, ObjectNumber: 8 });
    }

    [Fact]
    public void ABrokenPreviousSectionIsReported()
    {
        var original = HandWrittenPdf.CreateSimple().ToArray();
        var updated = HandWrittenPdf.AppendUpdate(original, [(6, "<< /Title (T) >>")], "<< /Size 7 /Root 1 0 R /Info 6 0 R /Prev 3 >>");

        var document = PdfDocument.Open(updated);

        Assert.Equal("T", document.Information.Title);
        var broken = Assert.Single(document.Diagnostics, diagnostic => diagnostic.Kind == PdfDiagnosticKind.BrokenCrossReferenceChain);
        Assert.Equal(3, broken.Offset);
        Assert.Null(PdfDocument.Open(HandWrittenPdf.AppendUpdate(original, [(6, "<< /Title (T) >>")], "<< /Size 7 /Root 1 0 R /Info 6 0 R /Prev 99999 >>")).Diagnostics.Single(diagnostic => diagnostic.Kind == PdfDiagnosticKind.BrokenCrossReferenceChain).Offset);
    }

    [Fact]
    public void AMissingObjectIsReported()
    {
        var text = Encoding.Latin1.GetString(HandWrittenPdf.CreateSimple().ToArray());
        var file = Encoding.Latin1.GetBytes(text.Replace("4 0 obj", "4 0 xxx", StringComparison.Ordinal));

        var document = PdfDocument.Open(file);
        _ = document.Pages[0].ExtractText();

        Assert.Contains(document.Diagnostics, diagnostic => diagnostic is { Kind: PdfDiagnosticKind.MissingObject, ObjectNumber: 4 });
    }

    [Fact]
    public void AnObjectDoesNotDependOnTheObjectsLoadedBeforeIt()
    {
        // The font has a string that is not terminated: it is recovered up to the next object. Object 6 is not where its
        // cross-reference entry says, so that loading it first scans the file. The offsets of the objects do not change
        var text = Encoding.Latin1.GetString(HandWrittenPdf.CreateSimple().Add(6, "<< /A 1 >>").ToArray());
        var file = Encoding.Latin1.GetBytes(text.Replace("/Encoding /WinAnsiEncoding", "(Encoding /WinAnsiEncoding", StringComparison.Ordinal).Replace("6 0 obj", "6 0 xxx", StringComparison.Ordinal));

        string ReadFont(params int[] objectNumbers)
        {
            var document = PdfDocument.Open(file);
            foreach (var number in objectNumbers)
            {
                Assert.True(document.Objects.TryGetReference(number, out var reference));
                _ = document.Objects.Resolve(reference);
            }

            Assert.True(document.Objects.TryGetReference(4, out var font));
            return document.Objects.Resolve(font).ToString();
        }

        var first = ReadFont();
        Assert.StartsWith("<</Type /Font", first, StringComparison.Ordinal);
        Assert.Equal(first, ReadFont(6));
    }

    [Fact]
    public void Example_Diagnostics()
    {
        var text = Encoding.Latin1.GetString(HandWrittenPdf.CreateSimple().ToArray());
        var data = Encoding.Latin1.GetBytes(text.Replace("startxref\n", "startxref\n9", StringComparison.Ordinal));

        #region Pdf_Diagnostics
        // Read the whole file when it is opened, so that every problem is listed
        var document = PdfDocument.Open(data, new PdfReadOptions { LoadAllObjects = true });
        var problems = document.Diagnostics
            .Where(diagnostic => diagnostic.Severity >= PdfDiagnosticSeverity.Warning)
            .Select(diagnostic => diagnostic.ToString())
            .ToList();
        #endregion

        Assert.True(document.IsRepaired);
        Assert.Contains("Warning CrossReferenceRebuilt: The cross-reference information was rebuilt by scanning the file: 5 objects found", problems);
    }

    [Fact]
    public void ValidateStructureReportsTheObjectStreamsBeyondTheLimits()
    {
        var file = HandWrittenPdf.CreateSimple();
        for (var i = 0; i < 20; i++)
        {
            file.Add(10 + i, "(" + new string('x', 100) + ")");
        }

        file.Compress([.. Enumerable.Range(10, 20)]);
        file.UseXrefStream = true;
        file.CompressObjectStreams = true;
        var document = PdfDocument.Open(file.ToArray(), new PdfReadOptions { MaxDecodedStreamLength = 1000 });

        var diagnostics = document.ValidateStructure();

        Assert.Single(diagnostics, diagnostic => diagnostic.Kind == PdfDiagnosticKind.UndecodableObjectStream);
    }

    [Fact]
    public void ValidateStructureThrowsForAnObjectOfAnObjectStreamNestedTooDeeply()
    {
        var file = HandWrittenPdf.CreateSimple();
        file.Add(10, new string('[', 50) + new string(']', 50));
        file.Add(11, "(eleven)");
        file.Compress(10, 11);
        file.UseXrefStream = true;
        var document = PdfDocument.Open(file.ToArray(), new PdfReadOptions { MaxNestingDepth = 10 });

        Assert.Throws<PdfResourceLimitException>(() => document.ValidateStructure());
    }

    [Fact]
    public void TheRepairsOfTheObjectsOfObjectStreamsAreReported()
    {
        var file = HandWrittenPdf.CreateSimple();
        file.Add(10, "<< /A missing >>");
        file.Compress(10);
        file.UseXrefStream = true;

        var diagnostics = PdfDocument.Open(file.ToArray(), new PdfReadOptions { LoadAllObjects = true }).Diagnostics;

        Assert.Contains(diagnostics, diagnostic => diagnostic is { Kind: PdfDiagnosticKind.UnknownKeyword, ObjectNumber: 10, Offset: null });
    }

    [Fact]
    public void ValidateStructureReportsTheReferencesWithAnotherGeneration()
    {
        var text = Encoding.Latin1.GetString(HandWrittenPdf.CreateSimple().ToArray());
        text = text[..text.IndexOf("xref", StringComparison.Ordinal)] + "6 0 obj\n(six)\nendobj\n7 0 obj\n<< /Ref 6 1 R >>\nendobj\ntrailer\n<< /Root 1 0 R >>\n";

        var diagnostics = PdfDocument.Open(Encoding.Latin1.GetBytes(text)).ValidateStructure();

        Assert.Contains(diagnostics, diagnostic => diagnostic is { Kind: PdfDiagnosticKind.GenerationMismatch, ObjectNumber: 6 });
    }

    [Fact]
    public void AFlateStreamWhoseChecksumEndsWithASpaceIsIntact()
    {
        // The Adler-32 checksum of the byte 0x1F is 0x00200020: its last bytes look like padding
        using var compressed = new MemoryStream();
        using (var zlib = new System.IO.Compression.ZLibStream(compressed, System.IO.Compression.CompressionLevel.Optimal, leaveOpen: true))
        {
            zlib.Write([0x1F]);
        }

        var data = compressed.ToArray();
        Assert.Equal<byte>([0x00, 0x20, 0x00, 0x20], data[^4..]);
        var file = HandWrittenPdf.CreateSimple();
        file.Add(6, HandWrittenPdf.Stream(Encoding.Latin1.GetString(data), " /Filter /FlateDecode"));

        Assert.Empty(PdfDocument.Open(file.ToArray()).ValidateStructure());
    }

    [Fact]
    public void AFlateStreamWithAWrongChecksumIsReported()
    {
        using var compressed = new MemoryStream();
        using (var zlib = new System.IO.Compression.ZLibStream(compressed, System.IO.Compression.CompressionLevel.Optimal, leaveOpen: true))
        {
            zlib.Write("Hello, world"u8);
        }

        var data = compressed.ToArray();
        data[^1] ^= 0xFF;
        var file = HandWrittenPdf.CreateSimple();
        file.Add(6, HandWrittenPdf.Stream(Encoding.Latin1.GetString(data), " /Filter /FlateDecode"));

        var diagnostics = PdfDocument.Open(file.ToArray()).ValidateStructure();

        Assert.Equal(PdfDiagnosticKind.DamagedStreamData, Assert.Single(diagnostics).Kind);

        // The streams the library writes have the right checksums
        var written = new PdfDocument();
        written.Pages.Add(PdfSize.A4).CreateContentBuilder().DrawText("Text", PdfFont.GetStandardFont(PdfStandardFont.Helvetica), 12, 10, 10);
        Assert.Empty(PdfDocument.Open(written.ToByteArray(), new PdfReadOptions { LoadAllObjects = true }).Diagnostics);
    }

    [Fact]
    public void TheRepairsOfAnObjectAreBounded()
    {
        var file = HandWrittenPdf.CreateSimple();
        file.Add(6, "<< " + string.Concat(Enumerable.Repeat("1 ", 5000)) + ">>");

        var diagnostics = PdfDocument.Open(file.ToArray(), new PdfReadOptions { LoadAllObjects = true }).Diagnostics;

        Assert.True(diagnostics.Count <= 1000, diagnostics.Count.ToString(System.Globalization.CultureInfo.InvariantCulture));
        Assert.Contains(diagnostics, diagnostic => diagnostic.Kind == PdfDiagnosticKind.InvalidDictionaryKey);
    }

    [Fact]
    public async Task PrefetchedPagesAreReadWithoutRequests()
    {
        var file = CreateLargeDocument(pageCount: 20, linearize: true);
        using var handler = new RangeHandler(file);
        using var httpClient = new HttpClient(handler, disposeHandler: false);

        var document = await PdfDocument.OpenAsync(httpClient, new Uri("https://example.com/report.pdf"));
        await document.PrefetchPageAsync(10);
        var text = document.Pages[10].ExtractText();

        Assert.Equal("Page 11", text.Trim());
        Assert.Equal(0, handler.SynchronousRequests);
        Assert.True(handler.ServedBytes < file.Length / 2, $"{handler.ServedBytes} of {file.Length} bytes were downloaded");
    }

    [Fact]
    public async Task TheFirstPageOfALinearizedFileIsDownloadedWithTheStartOfTheFile()
    {
        var file = CreateLargeDocument(pageCount: 20, linearize: true);
        using var handler = new RangeHandler(file);
        using var client = new HttpClient(handler, disposeHandler: false);

        var document = await PdfDocument.OpenAsync(client, new Uri("https://example.com/a.pdf"));

        // The start of the file, its end, then the rest of the first page section (/E) in one request
        Assert.Equal(3, handler.Requests);
        var start = Encoding.Latin1.GetString(file.AsSpan(0, 300));
        var endOfFirstPage = long.Parse(start[(start.IndexOf("/E ", StringComparison.Ordinal) + 3)..].Split(' ', StringSplitOptions.RemoveEmptyEntries)[0], System.Globalization.CultureInfo.InvariantCulture);
        Assert.Contains(handler.Ranges, range => range.From == 16 * 1024 && range.From + range.Length >= endOfFirstPage);
        await document.PrefetchPageAsync(0);
        Assert.Equal("Page 1", document.Pages[0].ExtractText().Trim());
        Assert.Equal(20, document.Pages.Count);
        Assert.Equal(0, handler.SynchronousRequests);
    }

    [Fact]
    public async Task AFileThatIsNotLinearizedIsReadOverHttp()
    {
        var file = CreateLargeDocument(pageCount: 10, linearize: false);
        using var handler = new RangeHandler(file);
        using var client = new HttpClient(handler, disposeHandler: false);

        var document = await PdfDocument.OpenAsync(client, new Uri("https://example.com/a.pdf"));

        // Without a prefetch, the objects are downloaded synchronously when they are read
        Assert.Equal("Page 10", document.Pages[9].ExtractText().Trim());
        Assert.True(handler.SynchronousRequests > 0);
        Assert.True(handler.ServedBytes < file.Length);
        Assert.Equal(PdfDocument.Open(file).Pages.Select(page => page.ExtractText()), document.Pages.Select(page => page.ExtractText()));
    }

    [Fact]
    public async Task AServerThatIgnoresTheRangesSendsTheWholeFile()
    {
        var file = CreateLargeDocument(pageCount: 3, linearize: false);
        using var handler = new RangeHandler(file) { IgnoreRanges = true };
        using var client = new HttpClient(handler, disposeHandler: false);

        var document = await PdfDocument.OpenAsync(client, new Uri("https://example.com/a.pdf"));

        Assert.Equal(1, handler.Requests);
        Assert.Equal("Page 3", document.Pages[2].ExtractText().Trim());
        await document.PrefetchPageAsync(1);
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => document.PrefetchPageAsync(3));
    }

    [Fact]
    public async Task HttpErrorsAndCancellationAreReported()
    {
        var file = CreateLargeDocument(pageCount: 3, linearize: false);
        using var failingHandler = new RangeHandler(file) { StatusCode = System.Net.HttpStatusCode.InternalServerError };
        using var failing = new HttpClient(failingHandler, disposeHandler: false);
        await Assert.ThrowsAsync<HttpRequestException>(() => PdfDocument.OpenAsync(failing, new Uri("https://example.com/a.pdf")));

        using var handler = new RangeHandler(file);
        using var client = new HttpClient(handler, disposeHandler: false);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => PdfDocument.OpenAsync(client, new Uri("https://example.com/a.pdf"), cancellationToken: new CancellationToken(canceled: true)));
    }

    [Fact]
    public async Task ARepairNeedsTheWholeFileOverHttp()
    {
        var text = Encoding.Latin1.GetString(CreateLargeDocument(pageCount: 3, linearize: false));
        var damaged = Encoding.Latin1.GetBytes(text[..text.LastIndexOf("startxref", StringComparison.Ordinal)]);
        using var handler = new RangeHandler(damaged);
        using var client = new HttpClient(handler, disposeHandler: false);

        await Assert.ThrowsAsync<UnsupportedPdfFeatureException>(() => PdfDocument.OpenAsync(client, new Uri("https://example.com/a.pdf"), new PdfReadOptions { AllowFullDownload = false }));

        // An empty file: its first range is not satisfiable
        using var emptyHandler = new RangeHandler([]);
        using var emptyClient = new HttpClient(emptyHandler, disposeHandler: false);
        await Assert.ThrowsAsync<InvalidPdfContentException>(() => PdfDocument.OpenAsync(emptyClient, new Uri("https://example.com/empty.pdf")));
        var repaired = await PdfDocument.OpenAsync(client, new Uri("https://example.com/a.pdf"));
        Assert.True(repaired.IsRepaired);
        Assert.Equal("Page 3", repaired.Pages[2].ExtractText().Trim());
    }

    [Theory]
    [InlineData(100)]
    [InlineData(-100)]
    public async Task RangesOfAnotherLengthAreRefused(int lengthError)
    {
        var file = CreateLargeDocument(pageCount: 10, linearize: false);
        using var handler = new RangeHandler(file) { LengthError = lengthError };
        using var client = new HttpClient(handler, disposeHandler: false);

        // The ranges after the first ones: those of the objects of the pages
        await Assert.ThrowsAsync<HttpRequestException>(async () =>
        {
            var document = await PdfDocument.OpenAsync(client, new Uri("https://example.com/a.pdf"));
            await document.PrefetchPageAsync(9);
        });
    }

    [Fact]
    public async Task APrefetchedPageWithAFormIsRenderedWithoutRequests()
    {
        var document = new PdfDocument();
        for (var i = 0; i < 6; i++)
        {
            var page = document.Pages.Add(PdfSize.A4);
            page.CreateContentBuilder().AppendRawContent(System.Text.Encoding.ASCII.GetBytes(string.Concat(Enumerable.Repeat("% filler line of the content stream\n", 3000))));
            document.Form.AddTextField(page, "field" + i.ToString(System.Globalization.CultureInfo.InvariantCulture), new PdfRectangle(72, 700, 300, 720)).Value = "Value";
        }

        var file = document.ToByteArray(new PdfSaveOptions { CompressStreams = false });
        using var handler = new RangeHandler(file);
        using var client = new HttpClient(handler, disposeHandler: false);
        var remote = await PdfDocument.OpenAsync(client, new Uri("https://example.com/a.pdf"));

        await remote.PrefetchPageAsync(4);
        using var image = Rendering.PdfRenderer.RenderPage(remote.Pages[4], new Rendering.PdfRenderOptions { UseSystemFonts = false, Dpi = 18 });

        Assert.Equal(0, handler.SynchronousRequests);
    }

    [Fact]
    public async Task ADeepFirstObjectIsReadWithTheLimitsOfTheOptions()
    {
        var file = new HandWrittenPdf()
            .Add(1, "<< /Type /Catalog /Pages 2 0 R /Deep " + new string('[', 20) + new string(']', 20) + " >>")
            .Add(2, "<< /Type /Pages /Kids [3 0 R] /Count 1 >>")
            .Add(3, "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 100 100] >>")
            .ToArray();
        using var handler = new RangeHandler(file);
        using var client = new HttpClient(handler, disposeHandler: false);

        var document = await PdfDocument.OpenAsync(client, new Uri("https://example.com/a.pdf"));

        Assert.Single(document.Pages);
    }

    [Fact]
    public async Task AHeaderBeyondTheFirstKilobyteNeedsTheWholeFile()
    {
        var file = new byte[2000].Concat(HandWrittenPdf.CreateSimple().ToArray()).ToArray();
        file.AsSpan(0, 2000).Fill((byte)' ');
        using var handler = new RangeHandler(file);
        using var client = new HttpClient(handler, disposeHandler: false);

        await Assert.ThrowsAsync<UnsupportedPdfFeatureException>(() => PdfDocument.OpenAsync(client, new Uri("https://example.com/a.pdf"), new PdfReadOptions { AllowFullDownload = false }));
    }

    private static byte[] CreateLargeDocument(int pageCount, bool linearize)
    {
        var document = new PdfDocument();
        var font = PdfFont.GetStandardFont(PdfStandardFont.Helvetica);
        var filler = Encoding.ASCII.GetBytes(string.Concat(Enumerable.Repeat("% filler line of the content stream\n", 3000)));
        for (var i = 0; i < pageCount; i++)
        {
            document.Pages.Add(PdfSize.A4).CreateContentBuilder()
                .DrawText(string.Create(System.Globalization.CultureInfo.InvariantCulture, $"Page {i + 1}"), font, 12, 72, 720)
                .AppendRawContent(filler);
        }

        return document.ToByteArray(new PdfSaveOptions { Linearize = linearize, CompressStreams = false });
    }

    /// <summary>Serves a file with range requests (RFC 9110, 14), synchronously or not, and counts what it serves.</summary>
    private sealed class RangeHandler(byte[] file) : HttpMessageHandler
    {
        public bool IgnoreRanges { get; init; }

        public System.Net.HttpStatusCode? StatusCode { get; init; }

        public int Requests { get; private set; }

        public int SynchronousRequests { get; private set; }

        public long ServedBytes { get; private set; }

        public List<(long From, long Length)> Ranges { get; } = [];

        /// <summary>Gets the bytes the server adds to (positive) or removes from (negative) the ranges it returns after the first one.</summary>
        public int LengthError { get; init; }

        protected override HttpResponseMessage Send(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            SynchronousRequests++;
            return Serve(request);
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(Serve(request));
        }

        private HttpResponseMessage Serve(HttpRequestMessage request)
        {
            Requests++;
            if (StatusCode is { } status)
                return new HttpResponseMessage(status);

            if (IgnoreRanges || request.Headers.Range is not { } range)
            {
                ServedBytes += file.Length;
                return new HttpResponseMessage(System.Net.HttpStatusCode.OK) { Content = new ByteArrayContent(file) };
            }

            if (file.Length == 0)
            {
                var empty = new ByteArrayContent([]);
                empty.Headers.ContentRange = new System.Net.Http.Headers.ContentRangeHeaderValue(0);
                return new HttpResponseMessage(System.Net.HttpStatusCode.RequestedRangeNotSatisfiable) { Content = empty };
            }

            var item = Assert.Single(range.Ranges);
            var from = item.From!.Value;
            var to = Math.Min(item.To!.Value, file.Length - 1);
            var length = (int)(to - from + 1);
            if (Requests > 1)
            {
                length = Math.Clamp(length + LengthError, 1, file.Length - (int)from);
            }

            ServedBytes += length;
            Ranges.Add((from, length));
            var content = new ByteArrayContent(file, (int)from, length);
            content.Headers.ContentRange = new System.Net.Http.Headers.ContentRangeHeaderValue(from, to, file.Length);
            return new HttpResponseMessage(System.Net.HttpStatusCode.PartialContent) { Content = content };
        }
    }
}
