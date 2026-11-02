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
    public void WrongOffsetsAreRecovered()
    {
        var file = HandWrittenPdf.CreateSimple();
        file.OffsetError = 3;

        var document = PdfDocument.Open(file.ToArray());

        Assert.Equal("Hello", document.Pages[0].ExtractText());
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
    }

    [Fact]
    public void MissingTrailerFindsTheCatalog()
    {
        var file = HandWrittenPdf.CreateSimple();
        file.WriteXref = false;

        var document = PdfDocument.Open(file.ToArray());

        Assert.True(document.IsRepaired);
        Assert.Single(document.Pages);
    }

    [Fact]
    public void BrokenStartXrefIsRebuilt()
    {
        var text = Encoding.Latin1.GetString(HandWrittenPdf.CreateSimple().ToArray());
        text = text.Replace("startxref\n", "startxref\n9", StringComparison.Ordinal);

        var document = PdfDocument.Open(Encoding.Latin1.GetBytes(text));

        Assert.True(document.IsRepaired);
        Assert.Equal("Hello", document.Pages[0].ExtractText());
    }

    [Fact]
    public void GarbageBeforeTheHeaderShiftsTheOffsets()
    {
        var file = HandWrittenPdf.CreateSimple().ToArray();
        var shifted = Encoding.ASCII.GetBytes("garbage\n").Concat(file).ToArray();

        var document = PdfDocument.Open(shifted);

        Assert.Equal("Hello", document.Pages[0].ExtractText());
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
    }

    [Fact]
    public void WrongDeclaredLengthStillEndsAtEndstream()
    {
        var file = HandWrittenPdf.CreateSimple();
        file.Add(6, "<< /Length 1 >>\nstream\nabc\nendstream");

        var document = PdfDocument.Open(file.ToArray());

        Assert.True(document.Objects.TryGetReference(6, out var reference));
        Assert.Equal("abc", Encoding.Latin1.GetString(Assert.IsType<PdfStream>(reference.Resolve()).GetDecodedData()));
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
    }
}
