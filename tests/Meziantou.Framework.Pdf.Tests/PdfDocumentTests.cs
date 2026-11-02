using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Text;
using Meziantou.Framework.Imaging;
using Meziantou.Framework.Pdf.Objects;

namespace Meziantou.Framework.Pdf.Tests;

public sealed class PdfDocumentTests
{
    [Fact]
    public void Example_CreateDocument()
    {
        #region Pdf_CreateDocument
        var document = new PdfDocument();
        document.Information.Title = "Invoice";
        var font = PdfFont.GetStandardFont(PdfStandardFont.Helvetica);

        var page = document.Pages.Add(PdfSize.A4);
        page.CreateContentBuilder()
            .SetFillRgb(0.2, 0.4, 0.8)
            .Rectangle(50, 750, 200, 40)
            .Fill()
            .SetFillGray(0)
            .DrawText("Hello, PDF!", font, 24, 60, 700);

        using var stream = new MemoryStream();
        document.Save(stream);
        #endregion

        var read = PdfDocument.Open(stream.ToArray());
        Assert.Equal("Invoice", read.Information.Title);
        Assert.Equal("Hello, PDF!", read.Pages[0].ExtractText());
    }

    [Fact]
    public void Example_EmbeddedFont()
    {
        var fontData = File.ReadAllBytes(TestHarness.RepositoryPaths.Fonts / "noto-sans" / "NotoSans-Regular.subset.ttf");

        #region Pdf_EmbeddedFont
        // A .ttf, .otf, .ttc, .woff or .woff2 file
        var font = PdfFont.Load(fontData);

        var document = new PdfDocument();
        document.Pages.Add(PdfSize.A4).CreateContentBuilder()
            .DrawText("Office \u2014 AVATAR", font, 24, 60, 700);

        // The document embeds the glyphs it shows, with their text
        var pdf = document.ToByteArray();
        #endregion

        Assert.Equal("Office \u2014 AVATAR", PdfDocument.Open(pdf).Pages[0].ExtractText());
    }

    [Fact]
    public void Example_AdvancedGraphics()
    {
        #region Pdf_AdvancedGraphics
        var document = new PdfDocument();
        var builder = document.Pages.Add(PdfSize.A4).CreateContentBuilder();

        // A gradient from red to blue, painted in a rectangle
        var gradient = PdfShading.Axial(PdfDeviceColorSpace.Rgb, 50, 0, 250, 0,
            PdfFunction.Gradient((0, PdfColor.FromRgb(1, 0, 0)), (1, PdfColor.FromRgb(0, 0, 1))));
        builder.SaveGraphicsState()
            .Rectangle(50, 600, 200, 100).Clip().EndPath()
            .PaintShading(gradient)
            .RestoreGraphicsState();

        // A form painted twice, the second time at half opacity with the Multiply blend mode
        var logo = document.CreateForm(new PdfRectangle(0, 0, 100, 100));
        logo.CreateContentBuilder().SetFillRgb(1, 0.8, 0).Circle(50, 50, 50).Fill();
        builder.DrawForm(logo)
            .SaveGraphicsState()
            .SetGraphicsState(new PdfExtGState { FillAlpha = 0.5, BlendMode = PdfBlendMode.Multiply })
            .Translate(50, 0)
            .DrawForm(logo)
            .RestoreGraphicsState();

        // A checkerboard: a tiling pattern used as the fill color
        var checkerboard = document.CreateTilingPattern(new PdfRectangle(0, 0, 20, 20), 20, 20);
        checkerboard.CreateContentBuilder().SetFillGray(0).Rectangle(0, 0, 10, 10).Rectangle(10, 10, 10, 10).Fill();
        builder.SetFillPattern(checkerboard).Rectangle(300, 600, 200, 100).Fill();
        #endregion

        var page = PdfDocument.Open(document.ToByteArray()).Pages[0];
        Assert.Equal(["Sh1"], page.Resources!.Get<PdfDictionary>(PdfNames.Shading)!.Keys.Select(key => key.Value));
        Assert.Equal(["Fm1"], page.Resources.Get<PdfDictionary>(PdfNames.XObject)!.Keys.Select(key => key.Value));
        Assert.Equal(["P1"], page.Resources.Get<PdfDictionary>(PdfNames.Pattern)!.Keys.Select(key => key.Value));
    }

    [Fact]
    public void Example_ReadDocument()
    {
        var file = HandWrittenPdf.CreateSimple("Some text").ToArray();

        #region Pdf_ReadDocument
        var document = PdfDocument.Open(file);
        var pageCount = document.Pages.Count;
        var text = document.Pages[0].ExtractText();
        #endregion

        Assert.Equal(1, pageCount);
        Assert.Equal("Some text", text);
    }

    [Fact]
    public void Example_IncrementalUpdate()
    {
        var file = HandWrittenPdf.CreateSimple().ToArray();

        #region Pdf_IncrementalUpdate
        var document = PdfDocument.Open(file);
        document.Information.Author = "Reviewer";

        using var output = new MemoryStream();
        document.SaveIncremental(output);
        #endregion

        Assert.Equal("Reviewer", PdfDocument.Open(output.ToArray()).Information.Author);
    }

    [Fact]
    public void Example_Encryption()
    {
        #region Pdf_Encryption
        var document = new PdfDocument();
        document.Pages.Add(PdfSize.A4).CreateContentBuilder()
            .DrawText("Confidential", PdfFont.GetStandardFont(PdfStandardFont.Helvetica), 24, 60, 700);

        // AES-256 by default: the user password opens the document with the permissions, the owner password with all
        document.Encryption = new PdfEncryption("user password", "owner password")
        {
            Permissions = PdfPermissions.Print | PdfPermissions.PrintHighQuality,
        };
        var file = document.ToByteArray();

        var read = PdfDocument.Open(file, new PdfReadOptions { Password = "user password" });
        var text = read.Pages[0].ExtractText();
        var canCopy = read.Permissions.HasFlag(PdfPermissions.CopyContent);
        #endregion

        Assert.True(read.IsEncrypted);
        Assert.Equal("Confidential", text);
        Assert.False(canCopy);
        Assert.Throws<PdfPasswordException>(() => PdfDocument.Open(file));
    }

    [Theory]
    [InlineData(PdfEncryptionAlgorithm.Rc4With40BitKey, false)]
    [InlineData(PdfEncryptionAlgorithm.Rc4With128BitKey, false)]
    [InlineData(PdfEncryptionAlgorithm.AesWith128BitKey, false)]
    [InlineData(PdfEncryptionAlgorithm.AesWith128BitKey, true)]
    [InlineData(PdfEncryptionAlgorithm.AesWith256BitKey, false)]
    [InlineData(PdfEncryptionAlgorithm.AesWith256BitKey, true)]
    public void EncryptedDocumentsAreReadWithTheirPasswords(PdfEncryptionAlgorithm algorithm, bool objectStreams)
    {
        var document = new PdfDocument();
        document.Information.Title = "Secret title";
        document.Pages.Add(PdfSize.A4).CreateContentBuilder().DrawText("Secret text", PdfFont.GetStandardFont(PdfStandardFont.Helvetica), 24, 60, 700);
        document.Encryption = new PdfEncryption("user", "owner") { Algorithm = algorithm, Permissions = PdfPermissions.Print | PdfPermissions.Annotate };
        var file = document.ToByteArray(new PdfSaveOptions { UseObjectStreams = objectStreams, CompressStreams = false });

        // Neither the strings nor the streams are in clear
        var text = Encoding.Latin1.GetString(file);
        Assert.DoesNotContain("Secret", text, StringComparison.Ordinal);
        Assert.Contains("/Encrypt", text, StringComparison.Ordinal);

        var user = PdfDocument.Open(file, new PdfReadOptions { Password = "user" });
        Assert.True(user.IsEncrypted);
        Assert.Equal("Secret title", user.Information.Title);
        Assert.Equal("Secret text", user.Pages[0].ExtractText());
        Assert.Equal(PdfPermissions.Print | PdfPermissions.Annotate, user.Permissions);
        Assert.Equal(algorithm, user.Encryption!.Algorithm);
        Assert.Null(user.Encryption.UserPassword);

        var owner = PdfDocument.Open(file, new PdfReadOptions { Password = "owner" });
        Assert.Equal("Secret text", owner.Pages[0].ExtractText());
        Assert.Equal(PdfPermissions.All, owner.Permissions);

        Assert.Throws<PdfPasswordException>(() => PdfDocument.Open(file));
        Assert.Throws<PdfPasswordException>(() => PdfDocument.Open(file, new PdfReadOptions { Password = "wrong" }));
    }

    [Fact]
    public void TheEncryptionOfADocumentReadIsKeptReplacedOrRemoved()
    {
        var document = new PdfDocument();
        document.Pages.Add(PdfSize.A4).CreateContentBuilder().DrawText("Text", PdfFont.GetStandardFont(PdfStandardFont.Helvetica), 24, 60, 700);
        document.Encryption = new PdfEncryption("", "owner") { Algorithm = PdfEncryptionAlgorithm.AesWith128BitKey };
        var file = document.ToByteArray();

        // A full save keeps the keys of the file; an incremental update encrypts its objects with them
        var read = PdfDocument.Open(file);
        read.Information.Title = "Updated";
        var kept = PdfDocument.Open(read.ToByteArray(), new PdfReadOptions { Password = "owner" });
        Assert.True(kept.IsEncrypted);
        Assert.Equal("Updated", kept.Information.Title);
        Assert.Equal(PdfEncryptionAlgorithm.AesWith128BitKey, kept.Encryption!.Algorithm);

        using var update = new MemoryStream();
        read.SaveIncremental(update);
        var updated = PdfDocument.Open(update.ToArray());
        Assert.True(updated.IsEncrypted);
        Assert.Equal("Updated", updated.Information.Title);
        Assert.Equal("Text", updated.Pages[0].ExtractText());

        // The encryption can be replaced or removed by a full save only
        read.Encryption = new PdfEncryption("new", "");
        Assert.Throws<InvalidOperationException>(() => read.SaveIncremental(new MemoryStream()));
        var replaced = PdfDocument.Open(read.ToByteArray(), new PdfReadOptions { Password = "new" });
        Assert.Equal(PdfEncryptionAlgorithm.AesWith256BitKey, replaced.Encryption!.Algorithm);
        Assert.Equal(PdfVersion.Pdf20, replaced.Version);

        read.Encryption = null;
        var removed = PdfDocument.Open(read.ToByteArray());
        Assert.False(removed.IsEncrypted);
        Assert.Null(removed.Encryption);
        Assert.Equal("Text", removed.Pages[0].ExtractText());
    }

    [Fact]
    public void ExplicitCryptFiltersOfStreamsAreHonored()
    {
        // An AES-128 file with an empty user password, and an update written by hand with streams that name their crypt
        // filter (7.4.10, 7.6.6): the Identity filter, explicit or by default, and RC4, a filter of the encryption
        // dictionary that is not the default. The RC4 stream is encrypted here with the algorithms 1 and 2 of 7.6.3 and
        // 7.6.4.3, independently of the security handler
        var document = new PdfDocument();
        document.Pages.Add(PdfSize.A4);
        document.Encryption = new PdfEncryption("", "owner") { Algorithm = PdfEncryptionAlgorithm.AesWith128BitKey };
        var original = document.ToByteArray(new PdfSaveOptions { UseObjectStreams = false });
        var read = PdfDocument.Open(original);
        var encryption = (PdfDictionary)read.Objects.Resolve((PdfReference)read.Trailer[PdfNames.Encrypt]!);
        var owner = encryption.Get<PdfString>(PdfName.Create("O"))!.Bytes.ToArray();
        var user = encryption.Get<PdfString>(PdfName.Create("U"))!.Bytes.ToArray();
        var permissions = (int)encryption.GetInteger(PdfName.Create("P"));
        var id = ((PdfString)read.Trailer.Get<PdfArray>(PdfNames.ID)!.GetResolved(0)).Bytes.ToArray();
        var fileKey = ComputeRevision4FileKey(owner, permissions, id);

        var first = read.Objects.MaxObjectNumber + 1;
        var rc4Data = Encoding.Latin1.GetString(Rc4(ComputeObjectKey(fileKey, first + 2), "RC4 filter"u8.ToArray()));
        var encrypt = "<< /Filter /Standard /V 4 /R 4 /Length 128 /CF << /StdCF << /AuthEvent /DocOpen /CFM /AESV2 /Length 16 >> /Second << /CFM /V2 /Length 16 >> >> /StmF /StdCF /StrF /StdCF"
            + " /O <" + Convert.ToHexString(owner) + "> /U <" + Convert.ToHexString(user) + "> /P " + permissions.ToString(CultureInfo.InvariantCulture) + " >>";
        var updated = HandWrittenPdf.AppendUpdate(original,
            [
                (first, HandWrittenPdf.Stream("Identity", " /Filter /Crypt /DecodeParms << /Type /CryptFilterDecodeParms /Name /Identity >>")),
                (first + 1, HandWrittenPdf.Stream("Default name", " /Filter /Crypt")),
                (first + 2, HandWrittenPdf.Stream(rc4Data, " /Filter /Crypt /DecodeParms << /Name /Second >>")),
                (first + 3, HandWrittenPdf.Stream("48656C6C6F>", " /Filter [/Crypt /ASCIIHexDecode] /DecodeParms [<< /Name /Identity >> null]")),
                (first + 4, encrypt),
            ],
            string.Create(CultureInfo.InvariantCulture, $"<< /Size {first + 5} /Root {((PdfReference)read.Trailer[PdfNames.Root]!).ObjectNumber} 0 R /Encrypt {first + 4} 0 R /ID [<{Convert.ToHexString(id)}> <{Convert.ToHexString(id)}>] /Prev {{prev}} >>"));

        var result = PdfDocument.Open(updated);
        Assert.True(result.IsEncrypted);
        var streams = new PdfArray();
        for (var number = first; number < first + 4; number++)
        {
            Assert.True(result.Objects.TryGetReference(number, out var reference));
            streams.Add(reference);
        }

        Assert.Equal(["Identity", "Default name", "RC4 filter", "Hello"], GetTexts(streams));

        // A full save with the keys of the file keeps the crypt filters of the streams
        result.Catalog[PdfName.Create("Streams")] = streams;
        var saved = PdfDocument.Open(result.ToByteArray());
        Assert.Equal(["Identity", "Default name", "RC4 filter", "Hello"], GetTexts(saved.Catalog.Get<PdfArray>(PdfName.Create("Streams"))!));

        static string[] GetTexts(PdfArray streams) => [.. streams.Select(item => Encoding.Latin1.GetString(((PdfStream)PdfReference.ResolveIfReference(item)).GetDecodedData()))];
    }

    /// <summary>Algorithm 2 (7.6.4.3.2) for an empty user password and a 128-bit key of revision 4.</summary>
    private static byte[] ComputeRevision4FileKey(byte[] owner, int permissions, byte[] id)
    {
        byte[] padding = [0x28, 0xBF, 0x4E, 0x5E, 0x4E, 0x75, 0x8A, 0x41, 0x64, 0x00, 0x4E, 0x56, 0xFF, 0xFA, 0x01, 0x08, 0x2E, 0x2E, 0x00, 0xB6, 0xD0, 0x68, 0x3E, 0x80, 0x2F, 0x0C, 0xA9, 0xFE, 0x64, 0x53, 0x69, 0x7A];
        var input = padding.Concat(owner.Take(32)).Concat(BitConverter.GetBytes(permissions)).Concat(id).ToArray();
#pragma warning disable CA5351 // The algorithm of the specification
        var hash = System.Security.Cryptography.MD5.HashData(input);
        for (var i = 0; i < 50; i++)
        {
            hash = System.Security.Cryptography.MD5.HashData(hash.AsSpan(0, 16));
        }
#pragma warning restore CA5351

        return hash[..16];
    }

    /// <summary>Algorithm 1 (7.6.3.3): the RC4 key of an object of generation 0.</summary>
    private static byte[] ComputeObjectKey(byte[] fileKey, int number)
    {
#pragma warning disable CA5351 // The algorithm of the specification
        return System.Security.Cryptography.MD5.HashData([.. fileKey, (byte)number, (byte)(number >> 8), (byte)(number >> 16), 0, 0]);
#pragma warning restore CA5351
    }

    private static byte[] Rc4(byte[] key, byte[] data)
    {
        var state = Enumerable.Range(0, 256).Select(i => (byte)i).ToArray();
        for (int i = 0, j = 0; i < 256; i++)
        {
            j = (j + state[i] + key[i % key.Length]) & 0xFF;
            (state[i], state[j]) = (state[j], state[i]);
        }

        var result = new byte[data.Length];
        for (int k = 0, i = 0, j = 0; k < data.Length; k++)
        {
            i = (i + 1) & 0xFF;
            j = (j + state[i]) & 0xFF;
            (state[i], state[j]) = (state[j], state[i]);
            result[k] = (byte)(data[k] ^ state[(state[i] + state[j]) & 0xFF]);
        }

        return result;
    }

    [Fact]
    public void AnEncryptedDocumentRepairedForItsCatalogIsWrittenWithItsKeys()
    {
        var document = new PdfDocument();
        document.Pages.Add(PdfSize.A4).CreateContentBuilder().DrawText("Text", PdfFont.GetStandardFont(PdfStandardFont.Helvetica), 24, 60, 700);
        document.Encryption = new PdfEncryption("", "owner") { Algorithm = PdfEncryptionAlgorithm.AesWith128BitKey };
        var file = Encoding.Latin1.GetString(document.ToByteArray(new PdfSaveOptions { UseObjectStreams = false }));

        // The trailer has the encryption and the identifier, but its /Root is missing: the repair finds the catalog
        var root = file.LastIndexOf("/Root ", StringComparison.Ordinal);
        var damaged = Encoding.Latin1.GetBytes(file[..root] + "/Root 99" + file[file.IndexOf(" ", root + 6, StringComparison.Ordinal)..]);
        var read = PdfDocument.Open(damaged);
        Assert.True(read.IsRepaired);
        Assert.True(read.IsEncrypted);
        Assert.Equal("Text", read.Pages[0].ExtractText());

        var written = PdfDocument.Open(read.ToByteArray());
        Assert.True(written.IsEncrypted);
        Assert.Equal("Text", written.Pages[0].ExtractText());
    }

    [Fact]
    public void AStreamInTheEncryptionDictionaryIsNotWrittenInIt()
    {
        var document = new PdfDocument();
        document.Pages.Add(PdfSize.A4).CreateContentBuilder().DrawText("Text", PdfFont.GetStandardFont(PdfStandardFont.Helvetica), 24, 60, 700);
        document.Encryption = new PdfEncryption("", "owner") { Algorithm = PdfEncryptionAlgorithm.AesWith256BitKey };
        var file = Encoding.Latin1.GetString(document.ToByteArray(new PdfSaveOptions { UseObjectStreams = false }));

        // An entry of the encryption dictionary refers to the content stream, which cannot be a direct object
        var contents = file.IndexOf("/Contents ", StringComparison.Ordinal) + "/Contents ".Length;
        var reference = file[contents..file.IndexOf(" R", contents, StringComparison.Ordinal)] + " R";
        var filter = file.IndexOf("/Filter /Standard", StringComparison.Ordinal);
        var read = PdfDocument.Open(Encoding.Latin1.GetBytes(file[..filter] + "/Extra " + reference + " " + file[filter..]));

        var bytes = read.ToByteArray(new PdfSaveOptions { UseObjectStreams = false });
        Assert.Contains("/Extra null", Encoding.Latin1.GetString(bytes), StringComparison.Ordinal);
        var written = PdfDocument.Open(bytes);
        Assert.True(written.IsEncrypted);
        Assert.Single(written.Pages);
    }

    [Fact]
    public void TamperedPermissionsOfRevision6AreNotTrusted()
    {
        // /P is outside the encryption: revision 6 repeats it in /Perms, encrypted with the file key
        var document = new PdfDocument();
        document.Pages.Add(PdfSize.A4);
        document.Encryption = new PdfEncryption("", "owner") { Permissions = PdfPermissions.Print };
        var file = Encoding.Latin1.GetString(document.ToByteArray(new PdfSaveOptions { UseObjectStreams = false }));
        Assert.Contains("/P -3900", file, StringComparison.Ordinal);

        var read = PdfDocument.Open(Encoding.Latin1.GetBytes(file.Replace("/P -3900", "/P -0001", StringComparison.Ordinal)));

        Assert.Equal(PdfPermissions.None, read.Permissions);
        Assert.Equal(PdfPermissions.Print, PdfDocument.Open(Encoding.Latin1.GetBytes(file)).Permissions);
    }

    [Fact]
    public void ThePasswordsOfTheLegacyAlgorithmsArePdfDocEncoded()
    {
        var document = new PdfDocument();
        document.Pages.Add(PdfSize.A4);
        document.Encryption = new PdfEncryption("中文", "owner") { Algorithm = PdfEncryptionAlgorithm.AesWith128BitKey };

        Assert.Throws<ArgumentException>(() => document.ToByteArray());
    }

    [Fact]
    public void AMissingKeyLengthIsRecovered()
    {
        // Revision 3 without /Length is a 40-bit key; some writers omit it for a 128-bit key. The entry is renamed in
        // place, so that the offsets of the file do not change.
        var document = new PdfDocument();
        document.Pages.Add(PdfSize.A4).CreateContentBuilder().DrawText("Text", PdfFont.GetStandardFont(PdfStandardFont.Helvetica), 24, 60, 700);
        document.Encryption = new PdfEncryption("", "owner") { Algorithm = PdfEncryptionAlgorithm.Rc4With128BitKey };
        var file = Encoding.Latin1.GetString(document.ToByteArray(new PdfSaveOptions { UseObjectStreams = false }));
        Assert.Contains("/Length 128", file, StringComparison.Ordinal);

        var read = PdfDocument.Open(Encoding.Latin1.GetBytes(file.Replace("/Length 128", "/Wength 128", StringComparison.Ordinal)));

        Assert.Equal("Text", read.Pages[0].ExtractText());
    }

    [Fact]
    public void TheMetadataCanBeLeftInClear()
    {
        var document = new PdfDocument();
        document.Information.Title = "Visible";
        document.UpdateXmpMetadata();
        document.Encryption = new PdfEncryption("user", "owner") { EncryptMetadata = false };

        var file = document.ToByteArray();

        Assert.Contains("<x:xmpmeta", Encoding.Latin1.GetString(file), StringComparison.Ordinal);
        var read = PdfDocument.Open(file, new PdfReadOptions { Password = "user" });
        Assert.False(read.Encryption!.EncryptMetadata);
        Assert.Contains("Visible", Encoding.UTF8.GetString(read.GetXmpMetadata()!), StringComparison.Ordinal);
        Assert.Equal("Visible", read.Information.Title);
    }

    [Fact]
    public void PasswordsOfRevision6AreNormalized()
    {
        // SASLprep: the ligature U+FB01 is "fi", and the soft hyphen is removed
        var document = new PdfDocument();
        document.Pages.Add(PdfSize.A4);
        document.Encryption = new PdfEncryption("\uFB01le\u00AD", "");

        var file = document.ToByteArray();

        Assert.True(PdfDocument.Open(file, new PdfReadOptions { Password = "file" }).IsEncrypted);
    }

    [Fact]
    public void PasswordsOfRevision6AreTruncatedTo127Bytes()
    {
        // The 127th byte is the first byte of "é" (C3 A9): it is kept, so "è" (C3 A8) gives the same key and the
        // password without the character does not
        var prefix = new string('a', 126);
        var document = new PdfDocument();
        document.Pages.Add(PdfSize.A4);
        document.Encryption = new PdfEncryption(prefix + "é", "owner");

        var file = document.ToByteArray();

        Assert.True(PdfDocument.Open(file, new PdfReadOptions { Password = prefix + "è" }).IsEncrypted);
        Assert.Throws<PdfPasswordException>(() => PdfDocument.Open(file, new PdfReadOptions { Password = prefix }));
    }

    [Fact]
    public void TheVersionOfTheEncryptionCannotBeLowered()
    {
        var document = new PdfDocument();
        document.Pages.Add(PdfSize.A4);
        document.Encryption = new PdfEncryption("", "owner");

        Assert.Throws<InvalidOperationException>(() => document.ToByteArray(new PdfSaveOptions { Version = PdfVersion.Pdf17 }));
    }

    [Fact]
    public void Example_TaggedDocument()
    {
        var fontData = File.ReadAllBytes(TestHarness.RepositoryPaths.Fonts / "noto-sans" / "NotoSans-Regular.subset.ttf");

        #region Pdf_TaggedDocument
        var font = PdfFont.Load(fontData);
        var document = new PdfDocument
        {
            PdfAConformance = PdfAConformance.PdfA2A,
            PdfUAConformance = PdfUAConformance.PdfUA1,
            Language = "en-US",
        };
        document.Information.Title = "Annual report";

        // The logical structure: a heading, a paragraph and a figure in a Document element
        var root = document.StructureTree.AddElement("Document");
        var heading = root.AddElement("H1");
        var paragraph = root.AddElement("P");
        var figure = root.AddElement("Figure");
        figure.AlternateDescription = "A blue square";

        // The content of each element, and the page number as an artifact
        document.Pages.Add(PdfSize.A4).CreateContentBuilder()
            .BeginStructureElement(heading).DrawText("AVATAR", font, 24, 60, 760).EndMarkedContent()
            .BeginStructureElement(paragraph).DrawText("Office", font, 12, 60, 720).EndMarkedContent()
            .BeginStructureElement(figure).SetFillRgb(0, 0, 1).Rectangle(60, 600, 100, 100).Fill().EndMarkedContent()
            .BeginArtifact(PdfArtifactType.Pagination).DrawText("1", font, 10, 300, 40).EndMarkedContent();

        var pdf = document.ToByteArray();
        #endregion

        var read = PdfDocument.Open(pdf);
        var element = Assert.Single(read.StructureTree.Elements);
        Assert.Equal("Document", element.Type);
        Assert.Equal(["H1", "P", "Figure"], element.Children.Select(child => child.Type));
        Assert.Equal("A blue square", element.Children[2].AlternateDescription);
        Assert.Equal("en-US", read.Language);
        var xmp = Encoding.UTF8.GetString(read.GetXmpMetadata()!);
        Assert.Contains("<pdfaid:part>2</pdfaid:part>", xmp, StringComparison.Ordinal);
        Assert.Contains("<pdfaid:conformance>A</pdfaid:conformance>", xmp, StringComparison.Ordinal);
        Assert.Contains("<pdfuaid:part>1</pdfuaid:part>", xmp, StringComparison.Ordinal);
        Assert.Contains("<pdfaSchema:prefix>pdfuaid</pdfaSchema:prefix>", xmp, StringComparison.Ordinal);
    }

    [Fact]
    public void Example_PdfA()
    {
        var fontData = File.ReadAllBytes(TestHarness.RepositoryPaths.Fonts / "noto-sans" / "NotoSans-Regular.subset.ttf");

        #region Pdf_PdfA
        // PDF/A embeds the fonts: the standard 14 fonts cannot be used
        var font = PdfFont.Load(fontData);
        var document = new PdfDocument { PdfAConformance = PdfAConformance.PdfA2B };
        document.Information.Title = "Archived";
        document.Pages.Add(PdfSize.A4).CreateContentBuilder().DrawText("Office", font, 24, 60, 700);

        // The save adds the sRGB output intent and the PDF/A identification of the XMP metadata
        var pdf = document.ToByteArray();
        #endregion

        var read = PdfDocument.Open(pdf);
        var intent = Assert.IsType<PdfDictionary>(read.Catalog.Get<PdfArray>(PdfName.Create("OutputIntents"))!.GetResolved(0));
        Assert.Equal("GTS_PDFA1", intent.GetName(PdfNames.S)!.Value);
        Assert.Contains("<pdfaid:conformance>B</pdfaid:conformance>", Encoding.UTF8.GetString(read.GetXmpMetadata()!), StringComparison.Ordinal);
        Assert.Equal(PdfVersion.Pdf17, read.Version);
    }

    [Fact]
    public void TheParentTreeMapsTheMarkedContentAndTheAnnotations()
    {
        var document = new PdfDocument();
        var root = document.StructureTree.AddElement("Document");
        document.StructureTree.MapRole("Heading", "H1");
        var heading = root.AddElement("Heading");
        var link = root.AddElement("Link");
        var page = document.Pages.Add(PdfSize.A4);
        var annotation = page.Annotations.AddLink(new PdfRectangle(0, 0, 10, 10), new Uri("https://example.com/"));
        link.AddAnnotation(annotation);
        page.CreateContentBuilder()
            .BeginStructureElement(heading).EndMarkedContent()
            .BeginStructureElement(link).EndMarkedContent()
            .BeginArtifact().EndMarkedContent()
            .BeginArtifact(PdfArtifactType.Background).EndMarkedContent();

        var read = PdfDocument.Open(document.ToByteArray(new PdfSaveOptions { CompressStreams = false }));

        var readPage = read.Pages[0];
        Assert.Equal("/Heading <</MCID 0>> BDC\nEMC\n/Link <</MCID 1>> BDC\nEMC\n/Artifact BMC\nEMC\n/Artifact <</Type /Background>> BDC\nEMC\n", Encoding.Latin1.GetString(readPage.GetContentData()));
        // The annotation was tagged first: it has the first key
        Assert.Equal(1, readPage.Dictionary.GetInteger(PdfName.Create("StructParents")));
        Assert.Equal("S", readPage.Dictionary.GetName(PdfName.Create("Tabs"))!.Value);
        Assert.Equal(0, readPage.Annotations[0].Dictionary.GetInteger(PdfName.Create("StructParent")));
        Assert.True(read.Catalog.Get<PdfDictionary>(PdfName.Create("MarkInfo"))!.GetBoolean(PdfName.Create("Marked")));
        Assert.Equal("H1", read.StructureTree.RoleMap["Heading"]);

        var treeRoot = read.Catalog.Get<PdfDictionary>(PdfName.Create("StructTreeRoot"))!;
        var nums = treeRoot.Get<PdfDictionary>(PdfName.Create("ParentTree"))!.Get<PdfArray>(PdfName.Create("Nums"))!;
        Assert.HasCount(4, nums);
        Assert.Same(read.StructureTree.Elements[0].Children[1].Dictionary, nums.GetResolved(1));
        var pageElements = Assert.IsType<PdfArray>(nums.GetResolved(3));
        Assert.Same(read.StructureTree.Elements[0].Children[0].Dictionary, pageElements.GetResolved(0));
        Assert.Same(read.StructureTree.Elements[0].Children[1].Dictionary, pageElements.GetResolved(1));
        Assert.Equal(2, treeRoot.GetInteger(PdfName.Create("ParentTreeNextKey")));
    }

    [Fact]
    public void TheStructureOfADocumentReadIsExtended()
    {
        var document = new PdfDocument();
        var element = document.StructureTree.AddElement("P");
        document.Pages.Add(PdfSize.A4).CreateContentBuilder().BeginStructureElement(element).EndMarkedContent();
        var read = PdfDocument.Open(document.ToByteArray());

        // The new content of the page follows the content it has: its marked-content identifier is the next one
        var added = read.StructureTree.AddElement("P");
        read.Pages[0].CreateContentBuilder().BeginStructureElement(added).EndMarkedContent();
        read.Pages.Add(PdfSize.A4).CreateContentBuilder().BeginStructureElement(added).EndMarkedContent();
        var saved = PdfDocument.Open(read.ToByteArray());

        Assert.HasCount(2, saved.StructureTree.Elements);
        var nums = saved.Catalog.Get<PdfDictionary>(PdfName.Create("StructTreeRoot"))!.Get<PdfDictionary>(PdfName.Create("ParentTree"))!.Get<PdfArray>(PdfName.Create("Nums"))!;
        Assert.HasCount(4, nums);
        Assert.HasCount(2, Assert.IsType<PdfArray>(nums.GetResolved(1)));
        Assert.Equal(1, saved.Pages[1].Dictionary.GetInteger(PdfName.Create("StructParents")));
        Assert.Contains("<</MCID 1>>", Encoding.Latin1.GetString(saved.Pages[0].GetContentData()), StringComparison.Ordinal);
    }

    [Fact]
    public void TaggingNeedsThePageOfTheDocument()
    {
        var document = new PdfDocument();
        var other = new PdfDocument();
        var element = document.StructureTree.AddElement("P");

        Assert.Throws<InvalidOperationException>(() => document.CreateForm(new PdfRectangle(0, 0, 10, 10)).CreateContentBuilder().BeginStructureElement(element));
        Assert.Throws<ArgumentException>(() => other.Pages.Add(PdfSize.A4).CreateContentBuilder().BeginStructureElement(element));
    }

    [Theory]
    [InlineData(PdfAConformance.PdfA2B, PdfUAConformance.None, "is not embedded")]
    [InlineData(PdfAConformance.PdfA2A, PdfUAConformance.None, "needs a tagged document")]
    [InlineData(PdfAConformance.None, PdfUAConformance.PdfUA1, "needs the title")]
    [InlineData(PdfAConformance.None, PdfUAConformance.PdfUA1, "needs the language")]
    [InlineData(PdfAConformance.PdfA1B, PdfUAConformance.None, "is not embedded")]
    [InlineData(PdfAConformance.PdfA1A, PdfUAConformance.None, "needs a tagged document")]
    [InlineData(PdfAConformance.PdfA4F, PdfUAConformance.None, "PDF/A-4f needs at least one embedded file")]
    [InlineData(PdfAConformance.None, PdfUAConformance.PdfUA2, "needs the title")]
    [InlineData(PdfAConformance.PdfA2B, PdfUAConformance.PdfUA2, "cannot be combined with PDF/A-2")]
    public void TheRequirementsOfTheConformanceLevelsAreChecked(PdfAConformance pdfA, PdfUAConformance pdfUA, string message)
    {
        var document = new PdfDocument { PdfAConformance = pdfA, PdfUAConformance = pdfUA };
        document.Pages.Add(PdfSize.A4).CreateContentBuilder().DrawText("Text", PdfFont.GetStandardFont(PdfStandardFont.Helvetica), 12, 10, 10);

        var exception = Assert.Throws<InvalidOperationException>(() => document.ToByteArray());

        Assert.Contains(message, exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void PdfANeedsADocumentWithoutEncryptionInPdf17()
    {
        var document = new PdfDocument { PdfAConformance = PdfAConformance.PdfA3B };
        document.Pages.Add(PdfSize.A4);
        document.Encryption = new PdfEncryption("", "owner");
        Assert.Contains("encryption", Assert.Throws<InvalidOperationException>(() => document.ToByteArray()).Message, StringComparison.Ordinal);

        document.Encryption = null;
        document.Version = PdfVersion.Pdf20;
        Assert.Contains("based on PDF 1.7", Assert.Throws<InvalidOperationException>(() => document.ToByteArray()).Message, StringComparison.Ordinal);
    }

    [Fact]
    public void PdfA1IsAPdf14FileWithoutTransparency()
    {
        var font = PdfFont.Load(TestHarness.RepositoryPaths.Fonts / "noto-sans" / "NotoSans-Regular.subset.ttf");
        var document = new PdfDocument { PdfAConformance = PdfAConformance.PdfA1B };
        document.Pages.Add(PdfSize.A4).CreateContentBuilder().DrawText("AVATAR", font, 12, 10, 10);

        var pdf = document.ToByteArray();

        Assert.StartsWith("%PDF-1.4\n", Encoding.Latin1.GetString(pdf), StringComparison.Ordinal);
        var text = Encoding.Latin1.GetString(pdf);
        Assert.Contains("\nxref\n", text, StringComparison.Ordinal);
        Assert.DoesNotContain("/ObjStm", text, StringComparison.Ordinal);
        var read = PdfDocument.Open(pdf);
        Assert.Contains("<pdfaid:part>1</pdfaid:part>", Encoding.UTF8.GetString(read.GetXmpMetadata()!), StringComparison.Ordinal);

        // The CIDSet of the subset: the glyph 0 and the four glyphs of "AVATAR" (the high-order bit is the CID 0)
        var cidFont = read.Pages[0].Resources!.Get<PdfDictionary>(PdfNames.Font)!.Select(entry => (PdfDictionary)PdfReference.ResolveIfReference(entry.Value)).Single().Get<PdfArray>(PdfNames.DescendantFonts)!.GetResolved(0);
        var cidSet = ((PdfDictionary)cidFont).Get<PdfDictionary>(PdfNames.FontDescriptor)!.Get<PdfStream>(PdfName.Create("CIDSet"))!.GetDecodedData();
        Assert.Equal(0x80, cidSet[0] & 0x80);
        var glyphs = Enumerable.Range(0, cidSet.Length * 8).Count(cid => (cidSet[cid / 8] & (0x80 >> (cid % 8))) != 0);
        Assert.Equal(5, glyphs);

        // The output intent has an ICC profile of version 2, which converts as the sRGB profile of version 4
        var intent = (PdfDictionary)read.Catalog.Get<PdfArray>(PdfName.Create("OutputIntents"))!.GetResolved(0);
        var profile = new Imaging.Metadata.IccProfile(new Imaging.Metadata.MetadataBlob(intent.Get<PdfStream>(PdfName.Create("DestOutputProfile"))!.GetDecodedData()));
        Assert.Equal(2, profile.Version.Major);
        byte[] colors = [0, 0, 0, 255, 255, 255, 255, 0, 0, 0, 255, 0, 0, 0, 255, 128, 64, 200, 10, 240, 120];
        var converted = new byte[colors.Length];
        IccColorTransform.Create(profile, Imaging.Metadata.IccProfile.Srgb).Convert(colors, converted);
        for (var i = 0; i < colors.Length; i++)
        {
            Assert.True(Math.Abs(colors[i] - converted[i]) <= 1, $"Component {i}: {colors[i]} became {converted[i]}");
        }

        // A save that asks for another version is refused
        Assert.Throws<InvalidOperationException>(() => document.ToByteArray(new PdfSaveOptions { Version = PdfVersion.Pdf17 }));
    }

    [Fact]
    public void PdfA1RefusesTransparencyAndEmbeddedFiles()
    {
        var font = PdfFont.Load(TestHarness.RepositoryPaths.Fonts / "noto-sans" / "NotoSans-Regular.subset.ttf");
        using var image = Image.ImportPixelData<Rgba32>([new Rgba32(255, 0, 0, 128)], 1, 1);
        var document = new PdfDocument { PdfAConformance = PdfAConformance.PdfA1B };
        var page = document.Pages.Add(PdfSize.A4);
        page.CreateContentBuilder()
            .DrawText("Office", font, 12, 10, 10)
            .SetGraphicsState(new PdfExtGState { FillAlpha = 0.5, BlendMode = PdfBlendMode.Multiply })
            .DrawImage(PdfImage.FromImage(image), 0, 0, 10, 10);
        page.Annotations.AddFileAttachment(PdfRectangle.FromSize(0, 0, 10, 10), "a.txt", "A"u8);
        document.EmbeddedFiles.Add("b.txt", "B"u8);

        var message = Assert.Throws<InvalidOperationException>(() => document.ToByteArray()).Message;

        Assert.Contains("a constant alpha other than 1 (ca)", message, StringComparison.Ordinal);
        Assert.Contains("the blend mode Multiply", message, StringComparison.Ordinal);
        Assert.Contains("an image has a soft mask", message, StringComparison.Ordinal);
        Assert.Contains("does not allow embedded files", message, StringComparison.Ordinal);
        Assert.Contains("does not allow the FileAttachment annotations", message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(PdfAConformance.PdfA4E, "E")]
    [InlineData(PdfAConformance.PdfA4F, "F")]
    public void PdfA4EAndPdfA4FAreIdentified(PdfAConformance conformance, string level)
    {
        var document = new PdfDocument { PdfAConformance = conformance };
        document.Pages.Add(PdfSize.A4);
        if (conformance == PdfAConformance.PdfA4F)
        {
            document.EmbeddedFiles.Add("data.csv", "a,b"u8);
        }

        var read = PdfDocument.Open(document.ToByteArray());

        var xmp = Encoding.UTF8.GetString(read.GetXmpMetadata()!);
        Assert.Contains("<pdfaid:part>4</pdfaid:part>", xmp, StringComparison.Ordinal);
        Assert.Contains($"<pdfaid:conformance>{level}</pdfaid:conformance>", xmp, StringComparison.Ordinal);
        Assert.Equal(PdfVersion.Pdf20, read.Version);
        Assert.Null(read.Trailer[PdfNames.Info]);

        // PDF/A-4e, as PDF/A-4, embeds PDF/A documents only
        if (conformance == PdfAConformance.PdfA4E)
        {
            document.EmbeddedFiles.Add("data.csv", "a,b"u8);
            Assert.Contains("'data.csv' is not a PDF/A-1, PDF/A-2, PDF/A-4 document", Assert.Throws<InvalidOperationException>(() => document.ToByteArray()).Message, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void Example_PdfUA2()
    {
        var fontData = File.ReadAllBytes(TestHarness.RepositoryPaths.Fonts / "noto-sans" / "NotoSans-Regular.subset.ttf");

        #region Pdf_PdfUA2
        var font = PdfFont.Load(fontData);
        var document = new PdfDocument
        {
            PdfAConformance = PdfAConformance.PdfA4,
            PdfUAConformance = PdfUAConformance.PdfUA2,
            Language = "en-US",
        };
        document.Information.Title = "Annual report";

        // A single Document element, and headings H1 to H6
        var root = document.StructureTree.AddElement("Document");
        var heading = root.AddElement("H1");
        var page = document.Pages.Add(PdfSize.A4);
        page.CreateContentBuilder().BeginStructureElement(heading).DrawText("Results", font, 24, 60, 760).EndMarkedContent();

        // The destinations in the document go to structure elements
        document.Outlines.Add("Results", PdfDestination.Fit(page).WithStructureElement(heading));

        var pdf = document.ToByteArray();
        #endregion

        var read = PdfDocument.Open(pdf);
        Assert.Equal(PdfVersion.Pdf20, read.Version);
        var xmp = Encoding.UTF8.GetString(read.GetXmpMetadata()!);
        Assert.Contains("<pdfuaid:part>2</pdfuaid:part>", xmp, StringComparison.Ordinal);
        Assert.Contains("<pdfuaid:rev>2024</pdfuaid:rev>", xmp, StringComparison.Ordinal);
        Assert.DoesNotContain("pdfaExtension", xmp, StringComparison.Ordinal);

        // The Document and H1 elements are in the PDF 2.0 namespace
        var structureRoot = read.Catalog.Get<PdfDictionary>(PdfName.Create("StructTreeRoot"))!;
        var ns = Assert.IsType<PdfDictionary>(structureRoot.Get<PdfArray>(PdfName.Create("Namespaces"))!.GetResolved(0));
        Assert.Equal("http://iso.org/pdf2/ssn", ns.GetText(PdfName.Create("NS")));
        var document2 = read.StructureTree.Elements[0];
        Assert.Same(ns, document2.Dictionary.Get<PdfDictionary>(PdfName.Create("NS")));
        Assert.Same(ns, document2.Children[0].Dictionary.Get<PdfDictionary>(PdfName.Create("NS")));

        // The outline item has a GoTo action with the page and the structure element
        var item = read.Outlines[0];
        Assert.Same(read.Pages[0].Dictionary, item.Destination!.Page.Dictionary);
        Assert.Same(document2.Children[0].Dictionary, item.Destination.StructureElement!.Dictionary);
    }

    [Fact]
    public void PdfUA2ChecksTheStructureAndTheDestinations()
    {
        var font = PdfFont.Load(TestHarness.RepositoryPaths.Fonts / "noto-sans" / "NotoSans-Regular.subset.ttf");
        var document = new PdfDocument { PdfUAConformance = PdfUAConformance.PdfUA2, Language = "en" };
        document.Information.Title = "Title";
        document.StructureTree.MapRole("Heading", "H");
        var first = document.StructureTree.AddElement("Document");
        var heading = first.AddElement("Heading");
        first.AddElement("Note");
        document.StructureTree.AddElement("Part");
        var page = document.Pages.Add(PdfSize.A4);
        page.CreateContentBuilder().BeginStructureElement(heading).DrawText("Text", font, 12, 10, 10).EndMarkedContent();
        document.Outlines.Add("Page", PdfDestination.Fit(page));
        page.Annotations.AddLink(new PdfRectangle(0, 0, 10, 10), PdfDestination.Fit(page));
        document.EmbeddedFiles.Add("data.csv", "a,b"u8);

        var message = Assert.Throws<InvalidOperationException>(() => document.ToByteArray()).Message;

        Assert.Contains("needs a single Document element", message, StringComparison.Ordinal);
        Assert.Contains("does not allow the H structure type", message, StringComparison.Ordinal);
        Assert.Contains("does not allow the Note structure type", message, StringComparison.Ordinal);
        Assert.Contains("the outline item 'Page' goes to a page", message, StringComparison.Ordinal);
        Assert.Contains("a link goes to a page", message, StringComparison.Ordinal);
        Assert.Contains("the description of the embedded file 'data.csv'", message, StringComparison.Ordinal);
        Assert.Throws<ArgumentException>(() => PdfDestination.Fit(page).WithStructureElement(new PdfDocument().StructureTree.AddElement("P")));
    }

    [Fact]
    public void ALinkToAStructureElementIsAGoToAction()
    {
        var document = new PdfDocument();
        var element = document.StructureTree.AddElement("H1");
        var page = document.Pages.Add(PdfSize.A4);
        page.Annotations.AddLink(new PdfRectangle(0, 0, 10, 10), PdfDestination.Xyz(page, 0, 500).WithStructureElement(element));

        var read = PdfDocument.Open(document.ToByteArray());

        var link = Assert.IsType<PdfLinkAnnotation>(read.Pages[0].Annotations[0]);
        Assert.Null(link.Dictionary[PdfNames.Dest]);
        var action = link.Dictionary.Get<PdfDictionary>(PdfNames.A)!;
        Assert.Equal("GoTo", action.GetName(PdfNames.S)!.Value);
        Assert.Same(read.StructureTree.Elements[0].Dictionary, action.Get<PdfArray>(PdfName.Create("SD"))!.GetResolved(0));
        Assert.Equal(PdfDestinationKind.Xyz, link.Destination!.Kind);
        Assert.Equal(500, link.Destination.Top);
        Assert.Same(read.StructureTree.Elements[0].Dictionary, link.Destination.StructureElement!.Dictionary);
    }

    [Fact]
    public void PdfA4KeepsTheInformationInTheMetadataOnly()
    {
        var document = new PdfDocument { PdfAConformance = PdfAConformance.PdfA4 };
        document.Information.Title = "Title";
        document.Pages.Add(PdfSize.A4).Annotations.AddLink(new PdfRectangle(0, 0, 10, 10), new Uri("https://example.com/"));

        var read = PdfDocument.Open(document.ToByteArray());

        Assert.Equal(PdfVersion.Pdf20, read.Version);
        Assert.Null(read.Trailer[PdfNames.Info]);
        Assert.Contains("<pdfaid:rev>2020</pdfaid:rev>", Encoding.UTF8.GetString(read.GetXmpMetadata()!), StringComparison.Ordinal);
        Assert.Equal(4, read.Pages[0].Annotations[0].Dictionary.GetInteger(PdfNames.F));
    }

    [Fact]
    public void Example_EmbeddedFiles()
    {
        #region Pdf_EmbeddedFiles
        var document = new PdfDocument();
        var page = document.Pages.Add(PdfSize.A4);

        // A file of the document: the name tree of the catalog, associated with the document
        var csv = "Product,Price\nBook,12\n"u8.ToArray();
        document.EmbeddedFiles.Add("prices.csv", csv, new PdfEmbeddedFileOptions
        {
            Description = "The prices of the table",
            MimeType = "text/csv",
            Relationship = PdfAssociatedFileRelationship.Data,
            ModificationDate = new DateTimeOffset(2026, 10, 24, 9, 30, 0, TimeSpan.Zero),
        });

        // A file of a page: a file attachment annotation, drawn as an icon
        page.Annotations.AddFileAttachment(PdfRectangle.FromSize(60, 700, 16, 20), "notes.txt", "Notes"u8, icon: PdfFileAttachmentIcon.Paperclip);

        var read = PdfDocument.Open(document.ToByteArray());
        var prices = read.EmbeddedFiles[0];
        var notes = ((PdfFileAttachmentAnnotation)read.Pages[0].Annotations[0]).File!;
        #endregion

        Assert.Equal("prices.csv", prices.FileName);
        Assert.Equal("The prices of the table", prices.Description);
        Assert.Equal("text/csv", prices.MimeType);
        Assert.Equal(PdfAssociatedFileRelationship.Data, prices.Relationship);
        Assert.Equal(new DateTimeOffset(2026, 10, 24, 9, 30, 0, TimeSpan.Zero), prices.ModificationDate);
        Assert.Null(prices.CreationDate);
        Assert.Equal(csv.Length, prices.Size);
        Assert.Equal(csv, prices.GetData());
        Assert.Same(prices.Dictionary, read.Catalog.Get<PdfArray>(PdfName.Create("AF"))!.GetResolved(0));

        Assert.Equal("notes.txt", notes.FileName);
        Assert.Equal("application/octet-stream", notes.MimeType);
        Assert.Equal(PdfAssociatedFileRelationship.Unspecified, notes.Relationship);
        Assert.Equal("Notes"u8.ToArray(), notes.GetData());
        var annotation = Assert.IsType<PdfFileAttachmentAnnotation>(read.Pages[0].Annotations[0]);
        Assert.Equal(PdfFileAttachmentIcon.Paperclip, annotation.Icon);
        Assert.Equal("notes.txt", annotation.Contents);
        Assert.NotNull(annotation.Dictionary.Get<PdfDictionary>(PdfName.Create("AP"))!.Get<PdfStream>(PdfNames.N));
        Assert.Same(notes.Dictionary, annotation.Dictionary.Get<PdfArray>(PdfName.Create("AF"))!.GetResolved(0));
        Assert.HasCount(1, read.EmbeddedFiles);
    }

    [Fact]
    public void TheNameTreeOfTheEmbeddedFilesIsSortedAndFilesAreRemoved()
    {
        var document = new PdfDocument();
        var b = document.EmbeddedFiles.Add("b.txt", "B"u8);
        document.EmbeddedFiles.Add("a.txt", "A"u8);
        document.Pages.Add(PdfSize.A4).Annotations.AddFileAttachment(PdfRectangle.FromSize(0, 0, 10, 10), b);

        Assert.Throws<ArgumentException>(() => document.EmbeddedFiles.Add("a.txt", "Other"u8));
        Assert.Throws<ArgumentException>(() => document.EmbeddedFiles.Add("", "Empty"u8));
        Assert.Throws<ArgumentException>(() => document.EmbeddedFiles.Add("c.txt", "C"u8, new PdfEmbeddedFileOptions { MimeType = "text plain" }));
        Assert.Throws<ArgumentException>(() => new PdfDocument().Pages.Add(PdfSize.A4).Annotations.AddFileAttachment(PdfRectangle.FromSize(0, 0, 10, 10), b));

        var read = PdfDocument.Open(document.ToByteArray());
        var tree = read.Catalog.Get<PdfDictionary>(PdfNames.Names)!.Get<PdfDictionary>(PdfName.Create("EmbeddedFiles"))!;
        var names = tree.Get<PdfArray>(PdfNames.Names)!;
        Assert.Equal(["a.txt", "b.txt"], [((PdfString)names.GetResolved(0)).ToText(), ((PdfString)names.GetResolved(2)).ToText()]);
        Assert.Equal(["a.txt", "b.txt"], read.EmbeddedFiles.Select(file => file.FileName));

        // The page keeps its file: the file of the name tree is removed from the tree and from the associated files
        var removed = read.EmbeddedFiles[1];
        Assert.True(read.EmbeddedFiles.Remove(removed));
        Assert.False(read.EmbeddedFiles.Remove(removed));
        Assert.True(read.EmbeddedFiles.Remove(read.EmbeddedFiles[0]));
        var saved = PdfDocument.Open(read.ToByteArray());
        Assert.Empty(saved.EmbeddedFiles);
        Assert.Null(saved.Catalog[PdfNames.Names]);
        Assert.Null(saved.Catalog[PdfName.Create("AF")]);
        Assert.Equal("B"u8.ToArray(), ((PdfFileAttachmentAnnotation)saved.Pages[0].Annotations[0]).File!.GetData());
    }

    [Fact]
    public void TheEmbeddedFilesOfAFileAreRead()
    {
        // A name tree of two levels with limits, a file with /F only and a file without embedded file stream
        var file = HandWrittenPdf.CreateSimple()
            .Add(1, "<< /Type /Catalog /Pages 2 0 R /Names << /EmbeddedFiles 6 0 R >> >>")
            .Add(6, "<< /Kids [7 0 R 8 0 R] >>")
            .Add(7, "<< /Limits [(a.txt) (a.txt)] /Names [(a.txt) 9 0 R] >>")
            .Add(8, "<< /Limits [(b.txt) (c.txt)] /Names [(b.txt) << /Type /Filespec /F (external.txt) >> (c.txt) 10 0 R] >>")
            .Add(9, "<< /Type /Filespec /F (a.txt) /Desc (First) /AFRelationship /Source /EF << /F 11 0 R >> >>")
            .Add(10, "<< /Type /Filespec /UF (\\376\\377\\000\\351\\000.\\000t) /EF << /F 12 0 R >> >>")
            .Add(11, HandWrittenPdf.Stream("Hello", " /Type /EmbeddedFile /Subtype /text#2Fplain /Params << /Size 5 /CreationDate (D:20260102030405Z) >>"))
            .Add(12, HandWrittenPdf.Stream("Bonjour"));

        var document = PdfDocument.Open(file.ToArray());

        Assert.Equal(["a.txt", "external.txt", "\u00E9.t"], document.EmbeddedFiles.Select(f => f.FileName));
        var first = document.EmbeddedFiles[0];
        Assert.Equal("First", first.Description);
        Assert.Equal("text/plain", first.MimeType);
        Assert.Equal(PdfAssociatedFileRelationship.Source, first.Relationship);
        Assert.Equal(new DateTimeOffset(2026, 1, 2, 3, 4, 5, TimeSpan.Zero), first.CreationDate);
        Assert.Equal(5, first.Size);
        Assert.Equal("Hello"u8.ToArray(), first.GetData());
        Assert.Throws<InvalidPdfContentException>(() => document.EmbeddedFiles[1].GetData());
        Assert.Null(document.EmbeddedFiles[2].MimeType);
        Assert.Null(document.EmbeddedFiles[2].Size);
        Assert.Equal("Bonjour"u8.ToArray(), document.EmbeddedFiles[2].GetData());
    }

    [Fact]
    public void PdfA2EmbedsPdfADocumentsOnly()
    {
        var font = PdfFont.Load(TestHarness.RepositoryPaths.Fonts / "noto-sans" / "NotoSans-Regular.subset.ttf");
        var archived = new PdfDocument { PdfAConformance = PdfAConformance.PdfA2B };
        archived.Pages.Add(PdfSize.A4).CreateContentBuilder().DrawText("Archived", font, 12, 10, 10);
        var document = new PdfDocument { PdfAConformance = PdfAConformance.PdfA2B };
        document.Pages.Add(PdfSize.A4).CreateContentBuilder().DrawText("Text", font, 12, 10, 10);
        document.EmbeddedFiles.Add("archived.pdf", archived.ToByteArray(), new PdfEmbeddedFileOptions { MimeType = "application/pdf" });
        Assert.NotEmpty(document.ToByteArray());

        document.EmbeddedFiles.Add("plain.pdf", new PdfDocument().ToByteArray());
        document.EmbeddedFiles.Add("data.csv", "a,b"u8);
        var message = Assert.Throws<InvalidOperationException>(() => document.ToByteArray()).Message;
        Assert.Contains("'plain.pdf' is not a PDF/A-1, PDF/A-2 document", message, StringComparison.Ordinal);
        Assert.Contains("'data.csv' is not a PDF/A-1, PDF/A-2 document", message, StringComparison.Ordinal);
        Assert.DoesNotContain("archived.pdf", message, StringComparison.Ordinal);

        // PDF/A-3 embeds any file
        document.PdfAConformance = PdfAConformance.PdfA3B;
        Assert.NotEmpty(document.ToByteArray());
    }

    [Fact]
    public void Example_OutlineAndLinks()
    {
        #region Pdf_OutlineAndLinks
        var document = new PdfDocument();
        var cover = document.Pages.Add(PdfSize.A4);
        var content = document.Pages.Add(PdfSize.A4);

        var chapter = document.Outlines.Add("Chapter 1", PdfDestination.Fit(content));
        chapter.Children.Add("Section 1.1", PdfDestination.Xyz(content, left: 0, top: 500));

        cover.Annotations.AddLink(new PdfRectangle(50, 50, 250, 80), PdfDestination.Fit(content));
        cover.Annotations.AddLink(new PdfRectangle(50, 100, 250, 130), new Uri("https://www.meziantou.net"));
        #endregion

        var read = PdfDocument.Open(document.ToByteArray());
        Assert.Equal("Chapter 1", read.Outlines[0].Title);
        Assert.Equal(2, read.Pages[0].Annotations.Count);
    }

    [Fact]
    public void Example_LowLevelObjects()
    {
        #region Pdf_LowLevelObjects
        var document = new PdfDocument();
        document.Pages.Add(PdfSize.Letter);

        // The catalog is a dictionary of the object model: entries the API has no member for can be set directly
        document.Catalog[PdfName.Create("PageLayout")] = PdfName.Create("TwoColumnLeft");
        var viewerPreferences = new PdfDictionary
        {
            [PdfName.Create("DisplayDocTitle")] = PdfBoolean.True,
        };
        document.Catalog[PdfName.Create("ViewerPreferences")] = document.Objects.Add(viewerPreferences);
        #endregion

        var read = PdfDocument.Open(document.ToByteArray());
        Assert.Equal("TwoColumnLeft", read.Catalog.GetName(PdfName.Create("PageLayout"))?.Value);
        Assert.True(read.Catalog.Get<PdfDictionary>(PdfName.Create("ViewerPreferences"))!.GetBoolean(PdfName.Create("DisplayDocTitle")));
    }

    [Fact]
    public void NewDocumentHasACatalogAndAnEmptyPageTree()
    {
        var document = new PdfDocument();

        Assert.Empty(document.Pages);
        Assert.True(document.Catalog.IsType(PdfName.Create("Catalog")));
        Assert.Equal(PdfVersion.Pdf17, document.Version);
        Assert.Empty(PdfDocument.Open(document.ToByteArray()).Pages);
    }

    [Fact]
    public void InsertAndRemovePages()
    {
        var document = new PdfDocument();
        var a = document.Pages.Add(new PdfSize(100, 100));
        var c = document.Pages.Add(new PdfSize(300, 300));
        var b = document.Pages.Insert(1, new PdfSize(200, 200));

        Assert.Equal([a, b, c], document.Pages);
        document.Pages.RemoveAt(0);

        var read = PdfDocument.Open(document.ToByteArray());
        Assert.Equal([200.0, 300.0], read.Pages.Select(p => p.MediaBox.Width));
        Assert.Equal(2, read.Catalog.Get<PdfDictionary>(PdfName.Create("Pages"))!.GetInteger(PdfName.Create("Count")));
    }

    [Fact]
    public void AddPageToADocumentRead()
    {
        var document = PdfDocument.Open(HandWrittenPdf.CreateSimple().ToArray());
        document.Pages.Add(PdfSize.A5).CreateContentBuilder().DrawText("Added", PdfFont.GetStandardFont(PdfStandardFont.Courier), 10, 10, 10);

        var read = PdfDocument.Open(document.ToByteArray());

        Assert.Equal(2, read.Pages.Count);
        Assert.Equal("Hello", read.Pages[0].ExtractText());
        Assert.Equal("Added", read.Pages[1].ExtractText());
    }

    [Fact]
    public void ContentAddedToAPageReadIsIsolated()
    {
        var file = new HandWrittenPdf()
            .Add(1, "<< /Type /Catalog /Pages 2 0 R >>")
            .Add(2, "<< /Type /Pages /Kids [3 0 R] /Count 1 /Resources << /Font << /F1 5 0 R >> >> >>")
            .Add(3, "<< /Type /Page /Parent 2 0 R /Contents 4 0 R >>")
            .Add(4, HandWrittenPdf.Stream("2 0 0 2 0 0 cm BT /F1 10 Tf (Old) Tj ET"))
            .Add(5, "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>");
        var document = PdfDocument.Open(file.ToArray());

        document.Pages[0].CreateContentBuilder().DrawText("New", PdfFont.GetStandardFont(PdfStandardFont.Helvetica), 10, 0, 100);

        var content = Encoding.ASCII.GetString(document.Pages[0].GetContentData());
        Assert.StartsWith("q\n\n2 0 0 2 0 0 cm", content, StringComparison.Ordinal);
        Assert.Contains("Q\nBT\n", content, StringComparison.Ordinal);

        // The inherited resources were copied to the page before the new font was added
        var resources = document.Pages[0].Dictionary.Get<PdfDictionary>(PdfName.Create("Resources"))!;
        Assert.Equal(2, resources.Get<PdfDictionary>(PdfName.Create("Font"))!.Count);
        Assert.Equal(1, document.Catalog.Get<PdfDictionary>(PdfName.Create("Pages"))!.Get<PdfDictionary>(PdfName.Create("Resources"))!.Get<PdfDictionary>(PdfName.Create("Font"))!.Count);
        Assert.Equal("Old\nNew", document.Pages[0].ExtractText());
    }

    [Fact]
    public void InformationDictionary()
    {
        var document = new PdfDocument();
        Assert.Null(document.Information.Dictionary);

        document.Information.Title = "T";
        document.Information.Subject = "S";
        document.Information.Keywords = "K";
        document.Information.Creator = "C";
        document.Information.Producer = "P";
        document.Information.ModificationDate = new DateTimeOffset(2020, 2, 29, 23, 59, 58, TimeSpan.Zero);
        document.Information.SetText("Custom", "Value");
        document.Information.Subject = null;

        var read = PdfDocument.Open(document.ToByteArray()).Information;
        Assert.Equal("T", read.Title);
        Assert.Null(read.Subject);
        Assert.Equal("K", read.Keywords);
        Assert.Equal("C", read.Creator);
        Assert.Equal("P", read.Producer);
        Assert.Equal(new DateTimeOffset(2020, 2, 29, 23, 59, 58, TimeSpan.Zero), read.ModificationDate);
        Assert.Equal("Value", read.GetText("Custom"));
        Assert.Equal("D:20200229235958Z", read.Dictionary!.GetText(PdfName.Create("ModDate")));
    }

    [Fact]
    public void XmpMetadataFromTheInformation()
    {
        var document = new PdfDocument();
        document.Information.Title = "A & B";
        document.Information.Author = "Me";
        document.Information.CreationDate = new DateTimeOffset(2021, 3, 4, 5, 6, 7, TimeSpan.FromHours(2));
        document.UpdateXmpMetadata();

        var read = PdfDocument.Open(document.ToByteArray());
        var xmp = Encoding.UTF8.GetString(read.GetXmpMetadata()!);

        Assert.StartsWith("<?xpacket begin=\"\uFEFF\"", xmp, StringComparison.Ordinal);
        Assert.Contains("<rdf:li xml:lang=\"x-default\">A &amp; B</rdf:li>", xmp, StringComparison.Ordinal);
        Assert.Contains("<rdf:li>Me</rdf:li>", xmp, StringComparison.Ordinal);
        Assert.Contains("<xmp:CreateDate>2021-03-04T05:06:07+02:00</xmp:CreateDate>", xmp, StringComparison.Ordinal);
        Assert.EndsWith("<?xpacket end=\"w\"?>", xmp, StringComparison.Ordinal);

        // The metadata stream is not compressed, so that tools that do not read PDF can find it
        Assert.Contains("<x:xmpmeta", Encoding.Latin1.GetString(document.ToByteArray()), StringComparison.Ordinal);
        read.RemoveXmpMetadata();
        Assert.Null(read.GetXmpMetadata());
    }

    [Fact]
    public void XmpMetadataReplacesTheCharactersXmlDoesNotAllow()
    {
        var document = new PdfDocument();
        document.Information.Title = "A\u0001B";
        document.Information.Author = "C\uD800D";
        document.Information.Subject = "\U0001F600";
        document.UpdateXmpMetadata();

        var xmp = Encoding.UTF8.GetString(document.GetXmpMetadata()!);
        Assert.Contains("<rdf:li xml:lang=\"x-default\">A�B</rdf:li>", xmp, StringComparison.Ordinal);
        Assert.Contains("<rdf:li>C�D</rdf:li>", xmp, StringComparison.Ordinal);
        Assert.Contains("\U0001F600", xmp, StringComparison.Ordinal);

        // PDF/A-4 has no information dictionary: the packet has the replacement character
        var pdfA4 = new PdfDocument { PdfAConformance = PdfAConformance.PdfA4 };
        pdfA4.Information.Title = "A\u0001B";
        pdfA4.Pages.Add(PdfSize.A4);
        Assert.Contains("A�B", Encoding.UTF8.GetString(PdfDocument.Open(pdfA4.ToByteArray()).GetXmpMetadata()!), StringComparison.Ordinal);

        // PDF/A-2 needs the information dictionary and the packet to agree: the save is refused
        var pdfA2 = new PdfDocument { PdfAConformance = PdfAConformance.PdfA2B };
        pdfA2.Information.Title = "A\u0001B";
        pdfA2.Pages.Add(PdfSize.A4);
        Assert.Contains("the Title has a character that XML does not allow", Assert.Throws<InvalidOperationException>(() => pdfA2.ToByteArray()).Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ConformanceSavesKeepTheXmpStreamWhenTheMetadataDoesNotChange()
    {
        var document = new PdfDocument { PdfAConformance = PdfAConformance.PdfA2B };
        document.Information.Title = "Archived";
        document.Pages.Add(PdfSize.A4);
        var original = document.ToByteArray();
        Assert.Equal(original, document.ToByteArray());

        var read = PdfDocument.Open(original);
        read.PdfAConformance = PdfAConformance.PdfA2B;
        var metadata = Assert.IsType<PdfReference>(read.Catalog[PdfNames.Metadata]);
        using var unchanged = new MemoryStream();
        read.SaveIncremental(unchanged);
        Assert.DoesNotContain("xmpmeta", Encoding.Latin1.GetString(unchanged.ToArray().AsSpan(original.Length)), StringComparison.Ordinal);

        // A change updates the existing stream: the incremental update has no new object for it
        read.Information.Title = "Changed";
        using var changed = new MemoryStream();
        read.SaveIncremental(changed);
        var updated = PdfDocument.Open(changed.ToArray());
        Assert.Equal(metadata.ObjectNumber, Assert.IsType<PdfReference>(updated.Catalog[PdfNames.Metadata]).ObjectNumber);
        Assert.Contains("Changed", Encoding.UTF8.GetString(updated.GetXmpMetadata()!), StringComparison.Ordinal);
    }

    [Fact]
    public void ConformanceSavesKeepTheCustomXmpSchemas()
    {
        const string Packet = """
            <?xpacket begin="" id="W5M0MpCehiHzreSzNTczkc9d"?>
            <x:xmpmeta xmlns:x="adobe:ns:meta/">
             <rdf:RDF xmlns:rdf="http://www.w3.org/1999/02/22-rdf-syntax-ns#">
              <rdf:Description rdf:about="" xmlns:fx="urn:factur-x:pdfa:CrossIndustryDocument:invoice:1p0#" xmlns:pdfaid="http://www.aiim.org/pdfa/ns/id/" xmlns:dc="http://purl.org/dc/elements/1.1/" pdfaid:part="2" fx:Version="1.0">
               <dc:title><rdf:Alt><rdf:li xml:lang="x-default">Old title</rdf:li></rdf:Alt></dc:title>
               <fx:DocumentType>INVOICE</fx:DocumentType>
              </rdf:Description>
              <rdf:Description rdf:about="" xmlns:pdfaExtension="http://www.aiim.org/pdfa/ns/extension/" xmlns:pdfaSchema="http://www.aiim.org/pdfa/ns/schema#" xmlns:pdfaProperty="http://www.aiim.org/pdfa/ns/property#">
               <pdfaExtension:schemas>
                <rdf:Bag>
                 <rdf:li rdf:parseType="Resource">
                  <pdfaSchema:schema>Factur-X PDFA Extension Schema</pdfaSchema:schema>
                  <pdfaSchema:namespaceURI>urn:factur-x:pdfa:CrossIndustryDocument:invoice:1p0#</pdfaSchema:namespaceURI>
                  <pdfaSchema:prefix>fx</pdfaSchema:prefix>
                 </rdf:li>
                </rdf:Bag>
               </pdfaExtension:schemas>
              </rdf:Description>
             </rdf:RDF>
            </x:xmpmeta>
            <?xpacket end="w"?>
            """;
        var document = new PdfDocument { PdfAConformance = PdfAConformance.PdfA3B };
        document.Information.Title = "Invoice";
        document.Pages.Add(PdfSize.A4);
        document.SetXmpMetadata(Encoding.UTF8.GetBytes(Packet));

        var xmp = System.Xml.Linq.XDocument.Parse(Encoding.UTF8.GetString(PdfDocument.Open(document.ToByteArray()).GetXmpMetadata()!));

        System.Xml.Linq.XNamespace fx = "urn:factur-x:pdfa:CrossIndustryDocument:invoice:1p0#";
        System.Xml.Linq.XNamespace pdfaid = "http://www.aiim.org/pdfa/ns/id/";
        System.Xml.Linq.XNamespace pdfaSchema = "http://www.aiim.org/pdfa/ns/schema#";
        System.Xml.Linq.XNamespace dc = "http://purl.org/dc/elements/1.1/";
        Assert.Equal("INVOICE", xmp.Descendants(fx + "DocumentType").Single().Value);
        Assert.Equal("1.0", xmp.Descendants(fx + "Version").Single().Value);
        Assert.Equal("3", xmp.Descendants(pdfaid + "part").Single().Value);
        Assert.Equal("Invoice", xmp.Descendants(dc + "title").Single().Value);
        Assert.Equal("fx", xmp.Descendants(pdfaSchema + "prefix").Single().Value);
        Assert.Equal("pdfaSchema", xmp.Descendants(pdfaSchema + "prefix").Single().GetPrefixOfNamespace(pdfaSchema));
    }

    [Fact]
    public void TheTrappedEntryIsInTheInformationAndTheXmpMetadata()
    {
        var document = new PdfDocument();
        Assert.Null(document.Information.Trapped);
        document.Information.Trapped = PdfTrapped.True;
        document.UpdateXmpMetadata();

        var read = PdfDocument.Open(document.ToByteArray());
        Assert.Equal(PdfTrapped.True, read.Information.Trapped);
        Assert.Equal("True", read.Information.Dictionary!.GetName(PdfNames.Trapped)!.Value);
        Assert.Contains("<pdf:Trapped>True</pdf:Trapped>", Encoding.UTF8.GetString(read.GetXmpMetadata()!), StringComparison.Ordinal);

        // A packet that has the property keeps it when the information does not have the entry
        read.Information.Trapped = null;
        Assert.Null(read.Information.Trapped);
        read.UpdateXmpMetadata();
        Assert.Contains("<pdf:Trapped>True</pdf:Trapped>", Encoding.UTF8.GetString(read.GetXmpMetadata()!), StringComparison.Ordinal);

        read.Information.Trapped = PdfTrapped.False;
        read.UpdateXmpMetadata();
        Assert.Contains("<pdf:Trapped>False</pdf:Trapped>", Encoding.UTF8.GetString(read.GetXmpMetadata()!), StringComparison.Ordinal);
        Assert.Throws<ArgumentOutOfRangeException>(() => read.Information.Trapped = (PdfTrapped)42);
    }

    [Fact]
    public void XmpMetadataWithPadding()
    {
        var document = new PdfDocument();
        document.Information.Title = "Padded";
        document.UpdateXmpMetadata();
        var length = document.GetXmpMetadata()!.Length;

        #region Pdf_XmpPadding
        // Reserve 2 KB of whitespace in the packet, so that an editor can update it in place
        document.UpdateXmpMetadata(padding: 2048);

        // The packet a PDF/A or PDF/UA save writes
        var options = new PdfSaveOptions { XmpPadding = 2048 };
        #endregion

        var padded = document.GetXmpMetadata()!;
        Assert.HasCount(length + 2048 + 1, padded);
        Assert.EndsWith("\n<?xpacket end=\"w\"?>", Encoding.UTF8.GetString(padded), StringComparison.Ordinal);
        Assert.NotNull(System.Xml.Linq.XDocument.Parse(Encoding.UTF8.GetString(padded)));
        Assert.Throws<ArgumentOutOfRangeException>(() => document.UpdateXmpMetadata(-1));
        Assert.Throws<ArgumentOutOfRangeException>(() => new PdfSaveOptions { XmpPadding = -1 });

        var archived = new PdfDocument { PdfAConformance = PdfAConformance.PdfA2B };
        archived.Information.Title = "Padded";
        archived.Pages.Add(PdfSize.A4);
        var withoutPadding = archived.GetXmpMetadata();
        Assert.Null(withoutPadding);
        var unpadded = PdfDocument.Open(archived.ToByteArray()).GetXmpMetadata()!.Length;
        Assert.HasCount(unpadded + 2048 + 1, PdfDocument.Open(archived.ToByteArray(options)).GetXmpMetadata()!);
    }

    [Fact]
    public void PdfAKeepsTheExistingOutputIntents()
    {
        var document = new PdfDocument { PdfAConformance = PdfAConformance.PdfA2B };
        document.Information.Title = "Printed";
        // The CMYK profile of Imaging is an input profile: PDF/A needs an output profile
        var data = Imaging.Metadata.IccProfile.Cgats001CompatibleCmyk.Data.ToArray();
        var profile = new PdfStream(new PdfDictionary { [PdfNames.N] = PdfInteger.Get(4) });
        profile.SetData(data);
        var profileReference = document.Objects.Add(profile);
        document.Catalog[PdfName.Create("OutputIntents")] = new PdfArray(document.Objects.Add(new PdfDictionary
        {
            [PdfNames.Type] = PdfName.Create("OutputIntent"),
            [PdfNames.S] = PdfName.Create("GTS_PDFX"),
            [PdfName.Create("OutputConditionIdentifier")] = PdfString.FromText("CGATS TR 001"),
            [PdfName.Create("DestOutputProfile")] = profileReference,
        }));
        document.Pages.Add(PdfSize.A4).CreateContentBuilder().SetFillCmyk(0, 1, 1, 0).Rectangle(10, 10, 100, 100).Fill();
        Assert.Contains("needs an output profile of a printer or a monitor", Assert.Throws<InvalidOperationException>(() => document.ToByteArray()).Message, StringComparison.Ordinal);

        "prtr"u8.CopyTo(data.AsSpan(12));
        profile.SetData(data);
        var read = PdfDocument.Open(document.ToByteArray());

        var intents = read.Catalog.Get<PdfArray>(PdfName.Create("OutputIntents"))!.Select(intent => (PdfDictionary)PdfReference.ResolveIfReference(intent)).ToList();
        Assert.Equal(["GTS_PDFX", "GTS_PDFA1"], intents.Select(intent => intent.GetName(PdfNames.S)!.Value));
        var profiles = intents.Select(intent => Assert.IsType<PdfReference>(intent[PdfName.Create("DestOutputProfile")]).ObjectNumber).Distinct();
        Assert.Single(profiles);
        Assert.Equal("CGATS TR 001", intents[1].GetText(PdfName.Create("OutputConditionIdentifier")));

        // DeviceRGB needs an RGB output intent
        document.Pages[0].CreateContentBuilder().SetFillRgb(1, 0, 0).Rectangle(10, 10, 100, 100).Fill();
        Assert.Contains("page 1 uses DeviceRGB", Assert.Throws<InvalidOperationException>(() => document.ToByteArray()).Message, StringComparison.Ordinal);
    }

    [Fact]
    public void PdfANeedsTheSameProfileForEveryOutputIntent()
    {
        var document = new PdfDocument { PdfAConformance = PdfAConformance.PdfA2B };
        document.Pages.Add(PdfSize.A4);
        PdfReference AddIntent(string type, Imaging.Metadata.IccProfile iccProfile)
        {
            var profile = new PdfStream(new PdfDictionary());
            profile.SetData(iccProfile.Data.Span);
            return document.Objects.Add(new PdfDictionary
            {
                [PdfNames.S] = PdfName.Create(type),
                [PdfName.Create("DestOutputProfile")] = document.Objects.Add(profile),
            });
        }

        document.Catalog[PdfName.Create("OutputIntents")] = new PdfArray(AddIntent("GTS_PDFX", Imaging.Metadata.IccProfile.Cgats001CompatibleCmyk), AddIntent("GTS_PDFA1", Imaging.Metadata.IccProfile.Srgb));

        Assert.Contains("the same profile for every output intent", Assert.Throws<InvalidOperationException>(() => document.ToByteArray()).Message, StringComparison.Ordinal);
    }

    [Fact]
    public void PdfANeedsAnOutputIntentForTheDeviceColorSpaces()
    {
        var document = new PdfDocument { PdfAConformance = PdfAConformance.PdfA2B };
        var page = document.Pages.Add(PdfSize.A4);
        page.CreateContentBuilder().SetFillRgb(1, 0, 0).SetFillGray(0.5).Rectangle(10, 10, 100, 100).Fill();
        _ = document.ToByteArray();

        // The output intent is sRGB: DeviceCMYK needs a DefaultCMYK color space
        page.CreateContentBuilder().SetStrokeCmyk(0, 0, 0, 1).Rectangle(10, 10, 100, 100).Stroke();
        Assert.Contains("PDF/A-2 allows DeviceCMYK only with an output intent of the same color space or a DefaultCMYK color space (ISO 19005-2, 6.2.4.3): page 1 uses DeviceCMYK", Assert.Throws<InvalidOperationException>(() => document.ToByteArray()).Message, StringComparison.Ordinal);

        var colorSpaces = new PdfDictionary();
        page.Resources![PdfNames.ColorSpace] = colorSpaces;
        colorSpaces[PdfName.Create("DefaultCMYK")] = new PdfArray(PdfName.Create("ICCBased"), PdfNull.Instance);
        _ = document.ToByteArray();

        // The color spaces of the forms are checked too
        var form = document.CreateForm(new PdfRectangle(0, 0, 10, 10));
        form.CreateContentBuilder().SetFillCmyk(0, 0, 0, 1).Rectangle(0, 0, 10, 10).Fill();
        var other = document.Pages.Add(PdfSize.A4);
        other.CreateContentBuilder().DrawForm(form);
        Assert.Contains("page 2 uses DeviceCMYK", Assert.Throws<InvalidOperationException>(() => document.ToByteArray()).Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(PdfAConformance.PdfA2B, "JavaScript", null, "PDF/A-2 does not allow the JavaScript actions: the document (OpenAction) has one")]
    [InlineData(PdfAConformance.PdfA1B, "Launch", null, "PDF/A-1 does not allow the Launch actions: the document (OpenAction) has one")]
    [InlineData(PdfAConformance.PdfA3B, "Named", "Print", "allows the NextPage, PrevPage, FirstPage and LastPage named actions only: the document (OpenAction) has the Print named action")]
    [InlineData(PdfAConformance.PdfA4, "Launch", null, "PDF/A-4 does not allow the Launch actions")]
    [InlineData(PdfAConformance.PdfA4, "JavaScript", null, null)]
    [InlineData(PdfAConformance.PdfA2B, "Named", "NextPage", null)]
    public void PdfAChecksTheActions(PdfAConformance conformance, string type, string? named, string? message)
    {
        var document = new PdfDocument { PdfAConformance = conformance };
        document.Pages.Add(PdfSize.A4);
        var action = new PdfDictionary { [PdfNames.S] = PdfName.Create(type) };
        if (named is not null)
        {
            action[PdfNames.N] = PdfName.Create(named);
        }

        if (type == "JavaScript")
        {
            action[PdfName.Create("JS")] = PdfString.FromText("app.alert('Hello');");
        }

        document.Catalog[PdfName.Create("OpenAction")] = document.Objects.Add(action);

        if (message is null)
        {
            _ = document.ToByteArray();
        }
        else
        {
            Assert.Contains(message, Assert.Throws<InvalidOperationException>(() => document.ToByteArray()).Message, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void PdfAChecksTheActionsOfTheAnnotationsAndThePages()
    {
        var document = new PdfDocument { PdfAConformance = PdfAConformance.PdfA2B };
        var page = document.Pages.Add(PdfSize.A4);
        var link = page.Annotations.AddLink(new PdfRectangle(10, 10, 100, 20), new Uri("https://www.meziantou.net/"));
        _ = document.ToByteArray();

        link.Dictionary[PdfNames.A] = new PdfDictionary
        {
            [PdfNames.S] = PdfName.Create("URI"),
            [PdfName.Create("URI")] = PdfString.FromText("https://www.meziantou.net/"),
            [PdfNames.Next] = new PdfDictionary { [PdfNames.S] = PdfName.Create("Launch") },
        };
        page.Dictionary[PdfName.Create("AA")] = new PdfDictionary { [PdfName.Create("O")] = new PdfDictionary { [PdfNames.S] = PdfName.Create("GoTo"), [PdfName.Create("D")] = new PdfArray(page.Reference, PdfName.Create("Fit")) } };

        var message = Assert.Throws<InvalidOperationException>(() => document.ToByteArray()).Message;
        Assert.Contains("PDF/A-2 does not allow the Launch actions: a Link annotation of page 1 has one", message, StringComparison.Ordinal);
        Assert.Contains("PDF/A-2 does not allow additional actions: page 1 has some (AA)", message, StringComparison.Ordinal);
    }

    [Fact]
    public void PdfAChecksTheAppearancesOfTheAnnotations()
    {
        var document = new PdfDocument { PdfAConformance = PdfAConformance.PdfA2B };
        var page = document.Pages.Add(PdfSize.A4);
        var text = document.Objects.Add(new PdfDictionary
        {
            [PdfNames.Type] = PdfName.Create("Annot"),
            [PdfNames.Subtype] = PdfName.Create("Text"),
            [PdfNames.Rect] = new PdfArray(PdfInteger.Get(10), PdfInteger.Get(10), PdfInteger.Get(30), PdfInteger.Get(30)),
            [PdfNames.Contents] = PdfString.FromText("Note"),
        });
        page.Dictionary[PdfNames.Annots] = new PdfArray(text);

        Assert.Contains("PDF/A-2 needs the appearance of the annotations (AP, ISO 19005-2, 6.3.3): the Text annotation of page 1 has none", Assert.Throws<InvalidOperationException>(() => document.ToByteArray()).Message, StringComparison.Ordinal);

        // PDF/A-1 does not need appearances, but allows the normal one only
        document.PdfAConformance = PdfAConformance.PdfA1B;
        _ = document.ToByteArray();

        var appearance = document.Objects.Add(new PdfStream(new PdfDictionary { [PdfNames.Subtype] = PdfNames.Form, [PdfName.Create("BBox")] = new PdfArray(PdfInteger.Get(0), PdfInteger.Get(0), PdfInteger.Get(20), PdfInteger.Get(20)) }));
        ((PdfDictionary)PdfReference.ResolveIfReference(text)!)[PdfName.Create("AP")] = new PdfDictionary { [PdfNames.N] = appearance, [PdfName.Create("R")] = appearance };
        document.PdfAConformance = PdfAConformance.PdfA2B;
        Assert.Contains("PDF/A-2 allows only the normal appearance of the annotations", Assert.Throws<InvalidOperationException>(() => document.ToByteArray()).Message, StringComparison.Ordinal);

        ((PdfDictionary)PdfReference.ResolveIfReference(text)!)[PdfName.Create("AP")] = new PdfDictionary { [PdfNames.N] = appearance };
        _ = document.ToByteArray();
    }

    [Theory]
    [InlineData(false, false, true)]
    [InlineData(true, false, false)]
    [InlineData(true, true, true)]
    public void ConformanceChecksTheFontsOfTheInteractiveForm(bool hasAppearance, bool needAppearances, bool fails)
    {
        var document = new PdfDocument { PdfUAConformance = PdfUAConformance.PdfUA1, Language = "en" };
        document.Information.Title = "Form";
        document.StructureTree.AddElement("Document");
        var page = document.Pages.Add(PdfSize.A4);
        var helvetica = document.Objects.Add(new PdfDictionary
        {
            [PdfNames.Type] = PdfNames.Font,
            [PdfNames.Subtype] = PdfName.Create("Type1"),
            [PdfNames.BaseFont] = PdfName.Create("Helvetica"),
        });
        var widget = new PdfDictionary
        {
            [PdfNames.Type] = PdfName.Create("Annot"),
            [PdfNames.Subtype] = PdfName.Create("Widget"),
            [PdfName.Create("FT")] = PdfName.Create("Tx"),
            [PdfName.Create("T")] = PdfString.FromText("Name"),
            [PdfName.Create("DA")] = PdfString.FromText("/Helv 12 Tf 0 g"),
            [PdfNames.Rect] = new PdfArray(PdfInteger.Get(10), PdfInteger.Get(10), PdfInteger.Get(200), PdfInteger.Get(30)),
        };
        if (hasAppearance)
        {
            widget[PdfName.Create("AP")] = new PdfDictionary { [PdfNames.N] = document.Objects.Add(new PdfStream(new PdfDictionary { [PdfNames.Subtype] = PdfNames.Form, [PdfName.Create("BBox")] = new PdfArray(PdfInteger.Get(0), PdfInteger.Get(0), PdfInteger.Get(190), PdfInteger.Get(20)) })) };
        }

        var widgetReference = document.Objects.Add(widget);
        page.Dictionary[PdfNames.Annots] = new PdfArray(widgetReference);
        document.Catalog[PdfNames.AcroForm] = new PdfDictionary
        {
            [PdfName.Create("Fields")] = new PdfArray(widgetReference),
            [PdfName.Create("DR")] = new PdfDictionary { [PdfNames.Font] = new PdfDictionary { [PdfName.Create("Helv")] = helvetica } },
            [PdfName.Create("NeedAppearances")] = needAppearances ? PdfBoolean.True : PdfBoolean.False,
        };

        if (fails)
        {
            Assert.Contains("The font 'Helvetica' is not embedded", Assert.Throws<InvalidOperationException>(() => document.ToByteArray()).Message, StringComparison.Ordinal);
        }
        else
        {
            _ = document.ToByteArray();
        }
    }

    [Fact]
    public void PdfARefusesTheNeedAppearancesFlag()
    {
        var document = new PdfDocument { PdfAConformance = PdfAConformance.PdfA2B };
        document.Pages.Add(PdfSize.A4);
        document.Catalog[PdfNames.AcroForm] = new PdfDictionary
        {
            [PdfName.Create("Fields")] = new PdfArray(),
            [PdfName.Create("NeedAppearances")] = PdfBoolean.True,
        };

        Assert.Contains("PDF/A-2 does not allow the NeedAppearances flag", Assert.Throws<InvalidOperationException>(() => document.ToByteArray()).Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ImagesFromImaging()
    {
        using var image = Image.ImportPixelData<Rgba32>([new Rgba32(255, 0, 0, 255), new Rgba32(0, 255, 0, 128), new Rgba32(0, 0, 255, 0), new Rgba32(255, 255, 255, 255)], 2, 2);
        var pdfImage = PdfImage.FromImage(image);
        Assert.True(pdfImage.HasSoftMask);

        var document = new PdfDocument();
        document.Pages.Add(PdfSize.A5).CreateContentBuilder().DrawImage(pdfImage, 10, 10, 100, 100);
        var read = PdfDocument.Open(document.ToByteArray());

        var xobject = read.Pages[0].Resources!.Get<PdfDictionary>(PdfName.Create("XObject"))!.Get<PdfStream>(PdfName.Create("Im1"))!;
        Assert.Equal([255, 0, 0, 0, 255, 0, 0, 0, 255, 255, 255, 255], xobject.GetDecodedData());
        var mask = xobject.Dictionary.Get<PdfStream>(PdfName.Create("SMask"))!;
        Assert.Equal([255, 128, 0, 255], mask.GetDecodedData());
        Assert.Equal("DeviceGray", mask.Dictionary.GetName(PdfName.Create("ColorSpace"))!.Value);
        Assert.Contains("/Im1 Do", Encoding.ASCII.GetString(read.Pages[0].GetContentData()), StringComparison.Ordinal);
    }

    [Fact]
    public void OpaqueImageHasNoSoftMask()
    {
        using var image = Image.ImportPixelData<Rgba32>([new Rgba32(1, 2, 3, 255)], 1, 1);

        Assert.False(PdfImage.FromImage(image).HasSoftMask);

        // PDF samples are integers: floating-point pixels are converted by the caller, never reinterpreted
        using var floats = new Image<RgbaFloat>(1, 1);
        Assert.Throws<ArgumentException>("image", () => PdfImage.FromImage(floats));
    }

    [Fact]
    public void SixteenBitImagesAreBigEndian()
    {
        using var image = Image.ImportPixelData<Gray16>([new Gray16(0x1234)], 1, 1);
        var document = new PdfDocument();
        document.Pages.Add(PdfSize.A5).CreateContentBuilder().DrawImage(PdfImage.FromImage(image), 0, 0, 1, 1);

        var stream = (PdfStream)document.Objects.GetReferences().Select(r => r.Resolve()).Single(o => o is PdfStream s && s.Dictionary.ContainsKey(PdfName.Create("Width")));

        Assert.Equal([0x12, 0x34], stream.GetDecodedData());
        Assert.Equal(16, stream.Dictionary.GetInteger(PdfName.Create("BitsPerComponent")));
    }

    [Fact]
    public void InteropPixelFormatsAreWrittenFromTheirWidening()
    {
        // Another byte order: the samples of Rgba32, with the alpha as soft mask
        using var argb = Image.ImportPixelData<Argb32>([new Argb32(1, 2, 3, 128)], 1, 1);
        Assert.Equal([1, 2, 3], GetImageData(argb, out var bits, out var mask));
        Assert.Equal(8, bits);
        Assert.Equal([128], mask);

        // 5-6-5 bits: the nearest 8-bit samples
        using var bgr565 = Image.ImportPixelData<Bgr565>([new Bgr565(31, 0, 1)], 1, 1);
        Assert.Equal([255, 0, 8], GetImageData(bgr565, out bits, out mask));
        Assert.Equal(8, bits);
        Assert.Null(mask);

        // 10-bit colors: the nearest 16-bit samples, big-endian
        using var rgba1010102 = Image.ImportPixelData<Rgba1010102>([new Rgba1010102(1023, 1, 0, 3)], 1, 1);
        Assert.Equal([0xFF, 0xFF, 0x00, 0x40, 0x00, 0x00], GetImageData(rgba1010102, out bits, out mask));
        Assert.Equal(16, bits);
        Assert.Null(mask);

        static byte[] GetImageData(Image image, out long bitsPerComponent, out byte[]? mask)
        {
            var document = new PdfDocument();
            document.Pages.Add(PdfSize.A5).CreateContentBuilder().DrawImage(PdfImage.FromImage(image), 0, 0, 1, 1);
            var read = PdfDocument.Open(document.ToByteArray());
            var xobject = read.Pages[0].Resources!.Get<PdfDictionary>(PdfName.Create("XObject"))!.Get<PdfStream>(PdfName.Create("Im1"))!;
            bitsPerComponent = xobject.Dictionary.GetInteger(PdfName.Create("BitsPerComponent"));
            mask = xobject.Dictionary.Get<PdfStream>(PdfName.Create("SMask"))?.GetDecodedData();
            return xobject.GetDecodedData();
        }
    }

    [Fact]
    public void ImageIsSharedByThePagesOfADocument()
    {
        using var image = Image.ImportPixelData<Rgb24>([new Rgb24(1, 2, 3)], 1, 1);
        var pdfImage = PdfImage.FromImage(image);
        var document = new PdfDocument();
        document.Pages.Add(PdfSize.A5).CreateContentBuilder().DrawImage(pdfImage, 0, 0, 1, 1).DrawImage(pdfImage, 5, 5, 1, 1);
        document.Pages.Add(PdfSize.A5).CreateContentBuilder().DrawImage(pdfImage, 0, 0, 1, 1);

        var images = document.Objects.GetReferences().Select(r => r.Resolve()).OfType<PdfStream>().Count(s => s.Dictionary.GetName(PdfName.Create("Subtype"))?.Value == "Image");

        Assert.Equal(1, images);
    }

    [Fact]
    public void JpegIsKeptAsItIs()
    {
        using var image = Image.ImportPixelData<Rgb24>(Enumerable.Repeat(new Rgb24(200, 100, 50), 64).ToArray(), 8, 8);
        using var jpeg = new MemoryStream();
        image.Save(jpeg, new Imaging.Formats.JpegEncoder());

        var pdfImage = PdfImage.Load(jpeg.ToArray());
        var document = new PdfDocument();
        document.Pages.Add(PdfSize.A5).CreateContentBuilder().DrawImage(pdfImage, 0, 0, 8, 8);
        var stream = (PdfStream)document.Objects.GetReferences().Select(r => r.Resolve()).Single(o => o is PdfStream s && s.Dictionary.ContainsKey(PdfName.Create("Width")));

        Assert.Equal("DCTDecode", stream.Filters.Single().Value);
        Assert.Equal(jpeg.ToArray(), stream.EncodedData.ToArray());
        Assert.HasCount(8 * 8 * 3, stream.GetDecodedData());
    }

    [Fact]
    public void ImageSamplesUseThePngPredictorsWhenTheyAreSmaller()
    {
        // A gradient: each row differs from the one above by the same bytes, which the Up filter turns into zeros
        using var gradient = Image.ImportPixelData<Rgb24>([.. Enumerable.Range(0, 64 * 64).Select(i => new Rgb24((byte)(i % 64 * 4), (byte)(i / 64 * 4), 128))], 64, 64);
        using var flat = Image.ImportPixelData<Gray8>([.. Enumerable.Repeat(new Gray8(200), 64 * 64)], 64, 64);
        var document = new PdfDocument();
        document.Pages.Add(PdfSize.A5).CreateContentBuilder()
            .DrawImage(PdfImage.FromImage(gradient), 0, 0, 64, 64)
            .DrawImage(PdfImage.FromImage(flat), 100, 0, 64, 64);

        var read = PdfDocument.Open(document.ToByteArray());
        var images = read.Objects.GetReferences().Select(r => r.Resolve()).OfType<PdfStream>().Where(s => s.Dictionary.ContainsKey(PdfNames.Width)).ToArray();
        var predicted = images.Single(s => s.Dictionary.Get<PdfName>(PdfNames.ColorSpace)?.Value == "DeviceRGB");
        var parameters = predicted.Dictionary.Get<PdfDictionary>(PdfNames.DecodeParms);

        Assert.NotNull(parameters);
        Assert.Equal(15, parameters.GetInteger(PdfNames.Predictor));
        Assert.Equal(3, parameters.GetInteger(PdfNames.Colors));
        Assert.Equal(8, parameters.GetInteger(PdfNames.BitsPerComponent));
        Assert.Equal(64, parameters.GetInteger(PdfNames.Columns));
        var expected = new byte[64 * 64 * 3];
        gradient.Frames[0].CopyPixelBytesTo(expected);
        Assert.Equal(expected, predicted.GetDecodedData());

        var plain = images.Single(s => s.Dictionary.Get<PdfName>(PdfNames.ColorSpace)?.Value == "DeviceGray");
        Assert.Equal(Enumerable.Repeat((byte)200, 64 * 64), plain.GetDecodedData());

        // Without compression, the samples are stored as they are
        var uncompressed = PdfDocument.Open(document.ToByteArray(new PdfSaveOptions { CompressStreams = false }));
        var stored = uncompressed.Objects.GetReferences().Select(r => r.Resolve()).OfType<PdfStream>().First(s => s.Dictionary.Get<PdfName>(PdfNames.ColorSpace)?.Value == "DeviceRGB");
        Assert.Empty(stored.Filters);
        Assert.Equal(expected, stored.GetDecodedData());
    }

    [Fact]
    public void ImagesCanBeCompressedAsJpeg()
    {
        using var directory = TemporaryDirectory.Create();
        var path = directory / "photo.pdf";
        using (var photo = Image.ImportPixelData<Rgba32>([.. Enumerable.Range(0, 32 * 32).Select(i => new Rgba32((byte)(i % 32 * 8), 100, 50, (byte)(i < 512 ? 255 : 0)))], 32, 32))
        {
            DrawPhotoAsJpeg(photo, path);
        }

        var read = PdfDocument.Open(path);
        var stream = read.Objects.GetReferences().Select(r => r.Resolve()).OfType<PdfStream>().Single(s => s.Dictionary.ContainsKey(PdfNames.SMask));
        var softMask = Assert.IsType<PdfStream>(stream.Dictionary.GetResolved(PdfNames.SMask));

        Assert.Equal("DCTDecode", stream.Filters.Single().Value);
        Assert.Equal(8, stream.Dictionary.GetInteger(PdfNames.BitsPerComponent));
        Assert.Equal(ImageFormat.Jpeg, Image.DetectFormat(stream.EncodedData.Span[..Image.FormatDetectionPrefixLength]));
        var samples = stream.GetDecodedData();
        Assert.HasCount(32 * 32 * 3, samples);
        Assert.InRange(samples[(5 * 32 * 3) + (10 * 3)], 80 - 8, 80 + 8);
        Assert.InRange(samples[(5 * 32 * 3) + (10 * 3) + 1], 100 - 8, 100 + 8);
        Assert.Equal([.. Enumerable.Repeat((byte)255, 512), .. Enumerable.Repeat((byte)0, 512)], softMask.GetDecodedData());
    }

    #region Pdf_FromImageAsJpeg
    private static void DrawPhotoAsJpeg(Image photo, string outputPath)
    {
        // A photo is much smaller as JPEG than as samples compressed without loss; its transparency stays a soft mask
        var image = PdfImage.FromImageAsJpeg(photo, new Imaging.Formats.JpegEncoder { Quality = 85 });

        var document = new PdfDocument();
        document.Pages.Add(new PdfSize(300, 150)).CreateContentBuilder().DrawImage(image, 10, 10, 128, 128);
        document.Save(outputPath);
    }
    #endregion

    [Fact]
    public void ImagesOf16BitsAreRoundedTo8BitsForJpeg()
    {
        using var gray = Image.ImportPixelData<Gray16>([.. Enumerable.Repeat(new Gray16(0x8080), 64)], 8, 8);

        var image = PdfImage.FromImageAsJpeg(gray);
        var document = new PdfDocument();
        document.Pages.Add(PdfSize.A5).CreateContentBuilder().DrawImage(image, 0, 0, 8, 8);
        var stream = (PdfStream)document.Objects.GetReferences().Select(r => r.Resolve()).Single(o => o is PdfStream s && s.Dictionary.ContainsKey(PdfNames.Width));

        Assert.False(image.HasSoftMask);
        Assert.Equal("DeviceGray", stream.Dictionary.Get<PdfName>(PdfNames.ColorSpace)?.Value);
        Assert.All(stream.GetDecodedData(), sample => Assert.InRange(sample, 0x80 - 1, 0x80 + 1));
    }

    [Fact]
    public void Example_BilevelImage()
    {
        #region PdfImage_FromBilevel
        // A 16 x 2 image, one bit per pixel: a set bit is a black pixel
        byte[] pixels = [0b1111_0000, 0b0000_1111, 0b1010_1010, 0b0101_0101];
        var image = PdfImage.FromBilevel(pixels, width: 16, height: 2, PdfBilevelCompression.CcittGroup4);

        var document = new PdfDocument();
        document.Pages.Add(PdfSize.A4).CreateContentBuilder().DrawImage(image, 50, 700, 160, 20);
        var data = document.ToByteArray();
        #endregion

        var xobject = PdfDocument.Open(data).Pages[0].Resources!.Get<PdfDictionary>(PdfName.Create("XObject"))!.Get<PdfStream>(PdfName.Create("Im1"))!;
        Assert.Equal("CCITTFaxDecode", xobject.Filters.Single().Value);
        Assert.Equal([0b0000_1111, 0b1111_0000, 0b0101_0101, 0b1010_1010], xobject.GetDecodedData());
    }

    [SuppressMessage("Security", "CA5394:Do not use insecure randomness", Justification = "Generating test inputs, and the fixed seed keeps the cases reproducible.")]
    [Theory]
    [InlineData(PdfBilevelCompression.CcittGroup4, "CCITTFaxDecode")]
    [InlineData(PdfBilevelCompression.Jbig2, "JBIG2Decode")]
    [InlineData(PdfBilevelCompression.Flate, "FlateDecode")]
    public void BilevelImages(PdfBilevelCompression compression, string filter)
    {
        // 101 pixels per row: the bits after the last pixel of a row are random, they are not pixels
        const int Width = 101;
        const int Height = 37;
        const int Stride = 13;
        var pixels = new byte[Stride * Height];
        new Random(7).NextBytes(pixels);
        for (var y = 10; y < 20; y++)
        {
            // Runs of white and black rows
            pixels.AsSpan(y * Stride, Stride).Fill(y < 15 ? (byte)0 : (byte)0xFF);
        }

        var document = new PdfDocument();
        document.Pages.Add(PdfSize.A5).CreateContentBuilder().DrawImage(PdfImage.FromBilevel(pixels, Width, Height, compression), 0, 0, Width, Height);
        var read = PdfDocument.Open(document.ToByteArray());
        var xobject = read.Pages[0].Resources!.Get<PdfDictionary>(PdfName.Create("XObject"))!.Get<PdfStream>(PdfName.Create("Im1"))!;

        Assert.Equal(filter, xobject.Filters.Single().Value);
        Assert.Equal(1, xobject.Dictionary.GetInteger(PdfName.Create("BitsPerComponent")));
        Assert.Equal("DeviceGray", xobject.Dictionary.GetName(PdfName.Create("ColorSpace"))!.Value);
        var decoded = xobject.GetDecodedData();
        Assert.HasCount(Stride * Height, decoded);
        for (var y = 0; y < Height; y++)
        {
            for (var x = 0; x < Width; x++)
            {
                // DeviceGray: 0 is black
                var black = (pixels[(y * Stride) + (x / 8)] & (0x80 >> (x % 8))) != 0;
                var sample = (decoded[(y * Stride) + (x / 8)] & (0x80 >> (x % 8))) != 0;
                Assert.Equal(black, !sample);
            }
        }
    }

    [Fact]
    public void BilevelImageArgumentsAreValidated()
    {
        Assert.Throws<ArgumentOutOfRangeException>("width", () => PdfImage.FromBilevel([0], 0, 1));
        Assert.Throws<ArgumentOutOfRangeException>("height", () => PdfImage.FromBilevel([0], 1, -1));
        Assert.Throws<ArgumentException>("pixels", () => PdfImage.FromBilevel([0, 0, 0], 9, 2));
        Assert.Throws<ArgumentOutOfRangeException>("width", () => PdfImage.FromBilevel(new byte[131073], 1048577, 1));
        Assert.Throws<ArgumentOutOfRangeException>("compression", () => PdfImage.FromBilevel([0], 1, 1, (PdfBilevelCompression)42));
        Assert.Equal(1048577, PdfImage.FromBilevel(new byte[131073], 1048577, 1, PdfBilevelCompression.Jbig2).Width);
    }

    [Fact]
    public void Example_CreateCard()
    {
        using var directory = TemporaryDirectory.Create();
        var path = directory / "card.pdf";

        CreateCard(path);

        var document = PdfDocument.Open(path);
        Assert.Equal("Welcome", document.Information.Title);
        Assert.Equal(new PdfRectangle(0, 0, 300, 180), document.Pages[0].MediaBox);
        Assert.Equal("Welcome\nYour first PDF document", document.Pages[0].ExtractText());
    }

#pragma warning disable xUnit1013 // An example of the documentation, called by DocumentationImageTests
    #region Pdf_CreateCard
    public static void CreateCard(string outputPath)
    {
        var document = new PdfDocument();
        document.Information.Title = "Welcome";

        // A page of 300 by 180 points (1 point is 1/72 inch); the origin is its lower-left corner
        var page = document.Pages.Add(new PdfSize(300, 180));
        var bold = PdfFont.GetStandardFont(PdfStandardFont.HelveticaBold);
        var regular = PdfFont.GetStandardFont(PdfStandardFont.Helvetica);

        page.CreateContentBuilder()
            .SetFillRgb(0.13, 0.33, 0.62).Rectangle(0, 120, 300, 60).Fill() // a band at the top of the page
            .SetFillGray(1).DrawText("Welcome", bold, 28, 20, 140) // x and y are the start of the baseline
            .SetFillGray(0.2).DrawText("Your first PDF document", regular, 14, 20, 80)
            .SetStrokeRgb(0.13, 0.33, 0.62).SetLineWidth(2).MoveTo(20, 65).LineTo(280, 65).Stroke();

        document.Save(outputPath);
    }
    #endregion
#pragma warning restore xUnit1013

    [Fact]
    public void Example_PageSizes()
    {
        #region Pdf_PageSizes
        var document = new PdfDocument();
        var letter = document.Pages.Add(PdfSize.Letter); // 8.5 x 11 inches: 612 x 792 points
        var landscape = document.Pages.Add(PdfSize.A4.ToLandscape()); // 297 x 210 mm
        var label = document.Pages.Add(PdfSize.FromMillimeters(100, 50));
        var cover = document.Pages.Insert(0, PdfSize.A4); // the new first page

        // The crop box is the region viewers show and print: here, the label without a margin of 18 points (1/4 inch)
        label.CropBox = new PdfRectangle(18, 18, label.MediaBox.Width - 18, label.MediaBox.Height - 18);

        // A page drawn in portrait, shown turned 90 degrees clockwise
        letter.Rotation = 90;

        document.Pages.RemoveAt(document.Pages.IndexOf(cover));
        #endregion

        var read = PdfDocument.Open(document.ToByteArray());
        Assert.Equal(3, read.Pages.Count);
        Assert.Equal(90, read.Pages[0].Rotation);
        Assert.Equal(842, Math.Round(read.Pages[1].MediaBox.Width, MidpointRounding.AwayFromZero));
        Assert.Equal(label.MediaBox.Width - 36, read.Pages[2].CropBox.Width, 5);
        Assert.Equal(landscape.MediaBox.Height, read.Pages[1].MediaBox.Height, 5);
    }

    [Fact]
    public void Example_SaveOptions()
    {
        var document = new PdfDocument();
        document.Pages.Add(PdfSize.A4).CreateContentBuilder().DrawText("Hello", PdfFont.GetStandardFont(PdfStandardFont.Helvetica), 12, 50, 800);

        #region Pdf_SaveOptions
        // The default: compressed streams, object streams and a cross-reference stream (PDF 1.5 and later)
        var compact = document.ToByteArray();

        // A file for older readers: a cross-reference table and a PDF 1.4 header
        var compatible = document.ToByteArray(new PdfSaveOptions { UseObjectStreams = false, Version = PdfVersion.Pdf14 });

        // Content streams left as text, to read them in an editor
        var readable = document.ToByteArray(new PdfSaveOptions { CompressStreams = false });
        #endregion

        Assert.Equal("Hello", PdfDocument.Open(compact).Pages[0].ExtractText());
        Assert.StartsWith("%PDF-1.4", Encoding.ASCII.GetString(compatible), StringComparison.Ordinal);
        Assert.Contains("xref", Encoding.ASCII.GetString(compatible), StringComparison.Ordinal);
        Assert.Contains("(Hello) Tj", Encoding.ASCII.GetString(readable), StringComparison.Ordinal);
        Assert.Equal("Hello", PdfDocument.Open(compatible).Pages[0].ExtractText());
    }

    [Fact]
    public void Example_Metadata()
    {
        #region Pdf_Metadata
        var document = new PdfDocument { Language = "en-US" };
        document.Information.Title = "Quarterly report";
        document.Information.Author = "Finance team";
        document.Information.Subject = "The results of the third quarter";
        document.Information.Keywords = "finance, report";
        document.Information.CreationDate = new DateTimeOffset(2026, 10, 1, 9, 0, 0, TimeSpan.Zero);
        document.Information.SetText("Department", "Finance"); // a custom entry
        document.Pages.Add(PdfSize.A4);

        // PDF 2.0 prefers the XMP metadata: write it from the information above
        document.UpdateXmpMetadata();
        var pdf = document.ToByteArray();

        var read = PdfDocument.Open(pdf);
        var title = read.Information.Title;
        var department = read.Information.GetText("Department");
        var xmp = read.GetXmpMetadata(); // the XMP packet: UTF-8 XML, or null
        #endregion

        Assert.Equal("Quarterly report", title);
        Assert.Equal("Finance", department);
        Assert.Equal("en-US", read.Language);
        Assert.Equal(new DateTimeOffset(2026, 10, 1, 9, 0, 0, TimeSpan.Zero), read.Information.CreationDate);
        Assert.Null(read.Information.Producer);
        Assert.Contains("Quarterly report", Encoding.UTF8.GetString(xmp!), StringComparison.Ordinal);
    }

    [Fact]
    public void Example_OutlineStyles()
    {
        var document = new PdfDocument();
        var page = document.Pages.Add(PdfSize.A4);

        #region Pdf_OutlineStyles
        var summary = document.Outlines.Add("Summary", PdfDestination.FitHorizontally(page, top: 800));
        summary.IsBold = true;
        summary.Color = PdfColor.FromRgb(0.6, 0, 0); // an RGB color
        summary.IsOpen = true; // its children are shown when the document is opened
        summary.Children.Add("Key figures", PdfDestination.Xyz(page, left: 0, top: 600, zoom: 1.5));

        // The outline is a list: items can be inserted, moved and removed
        document.Outlines.Insert(0, new PdfOutlineItem("Cover", PdfDestination.Fit(page)) { IsItalic = true });
        #endregion

        var read = PdfDocument.Open(document.ToByteArray());
        Assert.Equal(["Cover", "Summary"], read.Outlines.Select(item => item.Title));
        Assert.True(read.Outlines[0].IsItalic);
        Assert.True(read.Outlines[1].IsBold);
        Assert.True(read.Outlines[1].IsOpen);
        Assert.Equal(PdfColor.FromRgb(0.6, 0, 0), read.Outlines[1].Color);
        Assert.Equal(1.5, read.Outlines[1].Children[0].Destination!.Zoom);
    }

    [Fact]
    public void Example_ReadLinks()
    {
        var source = new PdfDocument();
        var first = source.Pages.Add(PdfSize.A4);
        var second = source.Pages.Add(PdfSize.A4);
        source.Outlines.Add("Introduction", PdfDestination.Fit(first)).Children.Add("Details", PdfDestination.Fit(second));
        first.Annotations.AddLink(new PdfRectangle(50, 50, 250, 80), PdfDestination.Fit(second));
        first.Annotations.AddLink(new PdfRectangle(50, 100, 250, 130), new Uri("https://www.meziantou.net"));
        var file = source.ToByteArray();

        #region Pdf_ReadLinks
        var document = PdfDocument.Open(file);

        // The outline: a tree of items, each with its title and destination
        var bookmarks = new List<string>();
        foreach (var item in document.Outlines)
        {
            bookmarks.Add(item.Title);
            bookmarks.AddRange(item.Children.Select(child => "  " + child.Title));
        }

        // The links of a page: a web address or a destination in the document
        var targets = new List<string>();
        foreach (var link in document.Pages[0].Annotations.OfType<PdfLinkAnnotation>())
        {
            if (link.Uri is not null)
            {
                targets.Add(link.Uri.AbsoluteUri);
            }
            else if (link.Destination is not null)
            {
                targets.Add("page " + (document.Pages.IndexOf(link.Destination.Page) + 1));
            }
        }
        #endregion

        Assert.Equal(["Introduction", "  Details"], bookmarks);
        Assert.Equal(["page 2", "https://www.meziantou.net/"], targets);
    }

    [Fact]
    public void Example_StampPages()
    {
        using var directory = TemporaryDirectory.Create();
        var input = directory / "card.pdf";
        var output = directory / "approved.pdf";
        CreateCard(input);

        StampPages(input, output);

        var page = PdfDocument.Open(output).Pages[0];
        Assert.Equal("Welcome\nYour first PDF document\nAPPROVED", page.ExtractText());
    }

#pragma warning disable xUnit1013 // An example of the documentation, called by DocumentationImageTests
    #region Pdf_StampPages
    public static void StampPages(string inputPath, string outputPath)
    {
        var document = PdfDocument.Open(inputPath);
        var font = PdfFont.GetStandardFont(PdfStandardFont.HelveticaBold);
        var translucent = new PdfExtGState { FillAlpha = 0.4 };
        var width = font.GetTextWidth("APPROVED", 40);

        foreach (var page in document.Pages)
        {
            // The new content is painted over the content of the page, from the default graphics state
            var box = page.CropBox;
            page.CreateContentBuilder()
                .SetGraphicsState(translucent)
                .SetFillRgb(0.8, 0, 0)
                .Translate(box.Left + (box.Width / 2), box.Bottom + (box.Height / 2)) // the center of the page
                .Rotate(20)
                .DrawText("APPROVED", font, 40, -width / 2, -14);
        }

        document.Save(outputPath);
    }
    #endregion
#pragma warning restore xUnit1013

    [Fact]
    public void Example_SearchText()
    {
        using var directory = TemporaryDirectory.Create();
        var path = directory / "report.pdf";
        var source = new PdfDocument();
        var font = PdfFont.GetStandardFont(PdfStandardFont.Helvetica);
        source.Pages.Add(PdfSize.A4).CreateContentBuilder().DrawText("Introduction", font, 12, 50, 800);
        source.Pages.Add(PdfSize.A4).CreateContentBuilder().DrawText("Revenue grew by 12%", font, 12, 50, 800);
        source.Pages.Add(PdfSize.A4).CreateContentBuilder().DrawText("Revenue by region", font, 12, 50, 800);
        source.Save(path);

        #region Pdf_SearchText
        var document = PdfDocument.Open(path);

        // The numbers of the pages whose text contains a word
        var pages = new List<int>();
        for (var i = 0; i < document.Pages.Count; i++)
        {
            var text = document.Pages[i].ExtractText();
            if (text.Contains("revenue", StringComparison.OrdinalIgnoreCase))
            {
                pages.Add(i + 1);
            }
        }
        #endregion

        Assert.Equal([2, 3], pages);
    }

    [Fact]
    public void Example_FindTextPositions()
    {
        var source = new PdfDocument();
        source.Pages.Add(PdfSize.A4).CreateContentBuilder().DrawText("Revenue grew by 12%", PdfFont.GetStandardFont(PdfStandardFont.Helvetica), 12, 50, 800);
        var file = source.ToByteArray();

        #region Pdf_FindTextPositions
        var document = PdfDocument.Open(file);
        var layout = document.Pages[0].ExtractTextLayout();

        // The boxes of a word, in the coordinates of the page (points, from its bottom left corner)
        var boxes = new List<PdfRectangle>();
        foreach (var line in layout.Lines)
        {
            foreach (var word in line.Words)
            {
                if (word.Text.Equals("grew", StringComparison.OrdinalIgnoreCase))
                {
                    boxes.Add(word.Bounds);
                }
            }
        }
        #endregion

        var box = Assert.Single(boxes);
        Assert.Equal("Revenue grew by 12%", layout.Text);
        Assert.Equal(797.6, box.Bottom, 6);
        Assert.Equal(809.6, box.Top, 6);
        Assert.True(box.Left > 50 && box.Right > box.Left);
    }

    [Fact]
    public void Example_ExpandLigatures()
    {
        // A font whose glyph "fi" is the ligature U+FB01
        var file = new HandWrittenPdf()
            .Add(1, "<< /Type /Catalog /Pages 2 0 R >>")
            .Add(2, "<< /Type /Pages /Kids [3 0 R] /Count 1 >>")
            .Add(3, "<< /Type /Page /Parent 2 0 R /Resources << /Font << /F1 4 0 R >> >> /Contents 5 0 R >>")
            .Add(4, "<< /Type /Font /Subtype /Type1 /BaseFont /Times-Roman /Encoding << /Differences [1 /fi] >> >>")
            .Add(5, HandWrittenPdf.Stream("BT /F1 12 Tf 72 720 Td (\\001nancial) Tj ET"))
            .ToArray();

        #region Pdf_ExpandLigatures
        var document = PdfDocument.Open(file);
        var options = new PdfTextExtractionOptions { ExpandLigatures = true };
        var text = document.Pages[0].ExtractText(options);
        #endregion

        Assert.Equal("financial", text);
        Assert.Equal("ﬁnancial", document.Pages[0].ExtractText());
    }

    [Fact]
    public void Example_InspectFonts()
    {
        var source = new PdfDocument();
        source.Pages.Add(PdfSize.A4).CreateContentBuilder()
            .DrawText("Title", PdfFont.GetStandardFont(PdfStandardFont.HelveticaBold), 20, 50, 800)
            .DrawText("Text", PdfFont.GetStandardFont(PdfStandardFont.TimesRoman), 12, 50, 760);
        source.Pages.Add(PdfSize.A4).CreateContentBuilder().DrawText("Code", PdfFont.GetStandardFont(PdfStandardFont.Courier), 12, 50, 800);
        var file = source.ToByteArray();

        #region Pdf_InspectFonts
        var document = PdfDocument.Open(file);

        // The fonts of the resources of the pages: the /BaseFont of each font dictionary
        var fonts = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var page in document.Pages)
        {
            if (page.Resources?.GetResolved("Font") is not PdfDictionary pageFonts)
                continue;

            foreach (var name in pageFonts.Keys)
            {
                if (pageFonts.GetResolved(name) is PdfDictionary font && font.GetName("BaseFont") is { } baseFont)
                {
                    fonts.Add(baseFont.Value);
                }
            }
        }
        #endregion

        Assert.Equal(["Courier", "Helvetica-Bold", "Times-Roman"], fonts);
    }

    [Fact]
    public async Task Example_DescribeUpload()
    {
        var valid = new PdfDocument();
        valid.Pages.Add(PdfSize.A4);
        valid.Pages.Add(PdfSize.A4);
        var encrypted = new PdfDocument();
        encrypted.Pages.Add(PdfSize.A4);
        encrypted.Encryption = new PdfEncryption("secret", "owner");

        Assert.Equal("2 pages", await DescribeUploadAsync(new MemoryStream(valid.ToByteArray()), XunitCancellationToken));
        Assert.Equal("The document needs a password", await DescribeUploadAsync(new MemoryStream(encrypted.ToByteArray()), XunitCancellationToken));
        Assert.Equal("The file is not a PDF document that can be read", await DescribeUploadAsync(new MemoryStream("Not a PDF file"u8.ToArray()), XunitCancellationToken));
    }

#pragma warning disable xUnit1013 // An example of the documentation, called by DocumentationImageTests
    #region Pdf_DescribeUpload
    public static async Task<string> DescribeUploadAsync(Stream upload, CancellationToken cancellationToken)
    {
        // Lower limits for the files of unknown users: 16 MiB per decoded stream
        var options = new PdfReadOptions { MaxDecodedStreamLength = 16 * 1024 * 1024 };
        try
        {
            var document = await PdfDocument.OpenAsync(upload, options, cancellationToken);
            return $"{document.Pages.Count} pages";
        }
        catch (PdfPasswordException)
        {
            return "The document needs a password";
        }
        catch (PdfResourceLimitException)
        {
            return "The document is too large";
        }
        catch (PdfException)
        {
            // InvalidPdfContentException: not a PDF document, or damaged beyond repair
            // UnsupportedPdfFeatureException: a feature the library does not support
            return "The file is not a PDF document that can be read";
        }
    }
    #endregion
#pragma warning restore xUnit1013

    [Fact]
    public void Example_ChangePassword()
    {
        var source = new PdfDocument();
        source.Pages.Add(PdfSize.A4).CreateContentBuilder().DrawText("Secret", PdfFont.GetStandardFont(PdfStandardFont.Helvetica), 12, 50, 800);
        source.Encryption = new PdfEncryption("user password", "owner password");
        var file = source.ToByteArray();

        #region Pdf_ChangePassword
        // The owner password opens the document with every permission
        var document = PdfDocument.Open(file, new PdfReadOptions { Password = "owner password" });

        // Saved without encryption...
        document.Encryption = null;
        var clear = document.ToByteArray();

        // ...or with new passwords and another algorithm
        document.Encryption = new PdfEncryption("new user password", "new owner password")
        {
            Algorithm = PdfEncryptionAlgorithm.AesWith128BitKey,
        };
        var protectedAgain = document.ToByteArray();
        #endregion

        Assert.True(document.IsEncrypted);
        Assert.False(PdfDocument.Open(clear).IsEncrypted);
        Assert.Equal("Secret", PdfDocument.Open(clear).Pages[0].ExtractText());
        Assert.Throws<PdfPasswordException>(() => PdfDocument.Open(protectedAgain, new PdfReadOptions { Password = "user password" }));
        Assert.Equal("Secret", PdfDocument.Open(protectedAgain, new PdfReadOptions { Password = "new user password" }).Pages[0].ExtractText());
    }

    [Fact]
    public void Example_PdfA3Attachment()
    {
        var fontData = File.ReadAllBytes(TestHarness.RepositoryPaths.Fonts / "noto-sans" / "NotoSans-Regular.subset.ttf");
        var invoiceXml = "<Invoice><Number>2026-001</Number></Invoice>"u8.ToArray();

        #region Pdf_PdfA3Attachment
        var font = PdfFont.Load(fontData);
        var document = new PdfDocument { PdfAConformance = PdfAConformance.PdfA3B };
        document.Information.Title = "Invoice 2026-001";
        document.Pages.Add(PdfSize.A4).CreateContentBuilder().DrawText("Invoice 2026-001", font, 18, 60, 760);

        // PDF/A-3 accepts embedded files of any format, with a MIME type and their relationship to the document
        document.EmbeddedFiles.Add("invoice.xml", invoiceXml, new PdfEmbeddedFileOptions
        {
            Description = "The invoice as structured data",
            MimeType = "application/xml",
            Relationship = PdfAssociatedFileRelationship.Alternative,
            ModificationDate = new DateTimeOffset(2026, 10, 29, 8, 0, 0, TimeSpan.Zero),
        });

        var pdf = document.ToByteArray();
        #endregion

        var read = PdfDocument.Open(pdf);
        var file = Assert.Single(read.EmbeddedFiles);
        Assert.Equal("invoice.xml", file.FileName);
        Assert.Equal(PdfAssociatedFileRelationship.Alternative, file.Relationship);
        Assert.Equal(invoiceXml, file.GetData());
        Assert.Contains("<pdfaid:part>3</pdfaid:part>", Encoding.UTF8.GetString(read.GetXmpMetadata()!), StringComparison.Ordinal);
    }

    [Fact]
    public void Example_TaggedLink()
    {
        var fontData = File.ReadAllBytes(TestHarness.RepositoryPaths.Fonts / "noto-sans" / "NotoSans-Regular.subset.ttf");

        #region Pdf_TaggedLink
        var font = PdfFont.Load(fontData);
        var document = new PdfDocument { PdfUAConformance = PdfUAConformance.PdfUA1, Language = "en-US" };
        document.Information.Title = "Contact";
        var page = document.Pages.Add(PdfSize.A4);

        // A type of the application, mapped to the standard type assistive technologies know
        document.StructureTree.MapRole("Lead", "P");
        var root = document.StructureTree.AddElement("Document");
        var lead = root.AddElement("Lead");
        var link = root.AddElement("Link");

        // A link is both content and an annotation: the element holds both
        var annotation = page.Annotations.AddLink(new PdfRectangle(60, 715, 200, 735), new Uri("https://www.meziantou.net"));
        annotation.Contents = "The website of the author";
        link.AddAnnotation(annotation);

        page.CreateContentBuilder()
            .BeginStructureElement(lead).DrawText("Contact the author", font, 14, 60, 760).EndMarkedContent()
            .BeginStructureElement(link).DrawText("www.meziantou.net", font, 12, 60, 720).EndMarkedContent();

        var pdf = document.ToByteArray();
        #endregion

        var read = PdfDocument.Open(pdf);
        Assert.Equal("P", read.StructureTree.RoleMap["Lead"]);
        Assert.Equal(["Lead", "Link"], Assert.Single(read.StructureTree.Elements).Children.Select(child => child.Type));
        Assert.Equal("The website of the author", Assert.Single(read.Pages[0].Annotations).Contents);
    }

    [Fact]
    public void Example_OpenSeekable()
    {
        using var directory = TemporaryDirectory.Create();
        var path = directory.GetFullPath("document.pdf");
        File.WriteAllBytes(path, HandWrittenPdf.CreateSimple("Some text").ToArray());

        #region Pdf_OpenSeekable
        // The objects are read from the stream when they are first used: it stays open while the document is used
        using var stream = File.OpenRead(path);
        var document = PdfDocument.OpenSeekable(stream);
        var text = document.Pages[0].ExtractText();
        #endregion

        Assert.Equal("Some text", text);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task OpenStreamReadsFromItsPosition(bool seekable)
    {
        var file = HandWrittenPdf.CreateSimple().ToArray();
        var content = Encoding.ASCII.GetBytes("prefix").Concat(file).ToArray();

        var sync = new TestStream(content, seekable) { Position = 6 };
        var asynchronous = new TestStream(content, seekable) { Position = 6 };

        Assert.Equal("Hello", PdfDocument.Open(sync).Pages[0].ExtractText());
        Assert.Equal("Hello", (await PdfDocument.OpenAsync(asynchronous)).Pages[0].ExtractText());
        Assert.Equal(content.Length, sync.Offset);
        Assert.Equal(content.Length, asynchronous.Offset);
    }

    [Fact]
    public void OpenStreamCopiesTheBufferOfAMemoryStream()
    {
        var file = HandWrittenPdf.CreateSimple().ToArray();
        var buffer = file.ToArray();
        var stream = new MemoryStream(buffer, 0, buffer.Length, writable: true, publiclyVisible: true);

        var document = PdfDocument.Open(stream);
        Array.Clear(buffer);

        Assert.Equal("Hello", document.Pages[0].ExtractText());
    }

    [Fact]
    public void OpenStreamReadsALargeNonSeekableStreamInChunks()
    {
        var file = HandWrittenPdf.CreateSimple().Add(6, HandWrittenPdf.Stream(new string('x', 3 * 1024 * 1024))).ToArray();

        var document = PdfDocument.Open(new TestStream(file, seekable: false));

        Assert.True(document.Objects.TryGetReference(6, out var reference));
        Assert.HasCount(3 * 1024 * 1024, Assert.IsType<PdfStream>(reference.Resolve()).GetDecodedData());
    }

    [Fact]
    public void OpenSeekableReadsTheObjectsItUses()
    {
        var large = new string('x', 8 * 1024 * 1024);
        var file = HandWrittenPdf.CreateSimple().Add(6, HandWrittenPdf.Stream(large)).ToArray();
        var stream = new TestStream(file, seekable: true);

        var document = PdfDocument.OpenSeekable(stream);
        Assert.Equal("Hello", document.Pages[0].ExtractText());
        Assert.True(document.Objects.TryGetReference(6, out var reference));
        var data = Assert.IsType<PdfStream>(reference.Resolve());

        Assert.InRange(stream.BytesRead, 1, 256 * 1024);
        Assert.Equal(large, Encoding.Latin1.GetString(data.GetDecodedData()));
        Assert.InRange(stream.BytesRead, large.Length, large.Length + (256 * 1024));
    }

    [Fact]
    public void OpenSeekableReadsObjectsLargerThanTheWindow()
    {
        var items = string.Join(' ', Enumerable.Range(0, 50_000).Select(i => i.ToString(System.Globalization.CultureInfo.InvariantCulture)));
        var file = HandWrittenPdf.CreateSimple().Add(6, "[" + items + "]").Add(7, "<< /Length 8 0 R >>\nstream\n" + items + "\nendstream").Add(8, Encoding.ASCII.GetByteCount(items).ToString(System.Globalization.CultureInfo.InvariantCulture)).ToArray();

        var document = PdfDocument.OpenSeekable(new MemoryStream(file));

        Assert.True(document.Objects.TryGetReference(6, out var array));
        Assert.HasCount(50_000, Assert.IsType<PdfArray>(array.Resolve()));
        Assert.True(document.Objects.TryGetReference(7, out var stream));
        Assert.Equal(items, Encoding.ASCII.GetString(Assert.IsType<PdfStream>(stream.Resolve()).GetDecodedData()));
    }

    [Fact]
    public void OpenSeekableRepairsADamagedFile()
    {
        var file = HandWrittenPdf.CreateSimple().Compress(1, 2, 3, 4);
        file.UseXrefStream = true;
        var text = Encoding.Latin1.GetString(file.ToArray()).Replace("startxref", "startxreX", StringComparison.Ordinal);

        var document = PdfDocument.OpenSeekable(new MemoryStream(Encoding.Latin1.GetBytes(text)));

        Assert.True(document.IsRepaired);
        Assert.Equal("Hello", document.Pages[0].ExtractText());
    }

    [Fact]
    public void OpenSeekableReadsEncryptedDocuments()
    {
        var document = new PdfDocument();
        document.Pages.Add(PdfSize.A4).CreateContentBuilder().DrawText("Text", PdfFont.GetStandardFont(PdfStandardFont.Helvetica), 24, 60, 700);
        document.Information.Title = "Title";
        document.Encryption = new PdfEncryption("user", "owner");
        var file = document.ToByteArray(new PdfSaveOptions { UseObjectStreams = false });

        var read = PdfDocument.OpenSeekable(new MemoryStream(file), new PdfReadOptions { Password = "user" });

        Assert.Equal("Title", read.Information.Title);
        Assert.Equal("Text", read.Pages[0].ExtractText());
    }

    [Fact]
    public void OpenSeekableDocumentIsSavedIncrementally()
    {
        var file = HandWrittenPdf.CreateSimple().ToArray();
        var document = PdfDocument.OpenSeekable(new MemoryStream(file));
        document.Information.Title = "Updated";

        using var output = new MemoryStream();
        document.SaveIncremental(output);

        Assert.Equal(file, output.ToArray().AsSpan(0, file.Length).ToArray());
        var read = PdfDocument.Open(output.ToArray());
        Assert.Equal("Updated", read.Information.Title);
        Assert.Equal("Hello", read.Pages[0].ExtractText());
        Assert.Equal("Hello", PdfDocument.Open(document.ToByteArray()).Pages[0].ExtractText());
    }

    [Fact]
    public void OpenSeekableRequiresASeekableStream()
    {
        Assert.Throws<ArgumentException>(() => PdfDocument.OpenSeekable(new TestStream([], seekable: false)));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void OpenSeekableReadsFilesLargerThan2GB(bool repair)
    {
        // A file of 3 GB whose objects are after 2.5 GB, and NUL bytes (whitespace) before them: the stream generates it
        const long Start = 2_500_000_000L;
        var header = Encoding.ASCII.GetBytes("%PDF-1.7\n");
        var body = new StringBuilder();
        var offsets = new List<long>();
        foreach (var obj in new[]
        {
            "<< /Type /Catalog /Pages 2 0 R >>",
            "<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
            "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] /Resources << /Font << /F1 4 0 R >> >> /Contents 5 0 R >>",
            "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>",
            HandWrittenPdf.Stream("BT /F1 12 Tf 72 720 Td (Far away) Tj ET"),
        })
        {
            offsets.Add(Start + body.Length);
            body.Append(System.Globalization.CultureInfo.InvariantCulture, $"{offsets.Count} 0 obj\n{obj}\nendobj\n");
        }

        var xref = Start + body.Length;
        if (!repair)
        {
            body.Append("xref\n0 6\n0000000000 65535 f \n");
            foreach (var offset in offsets)
            {
                body.Append(System.Globalization.CultureInfo.InvariantCulture, $"{offset:D10} 00000 n \n");
            }

            body.Append(System.Globalization.CultureInfo.InvariantCulture, $"trailer\n<< /Size 6 /Root 1 0 R >>\nstartxref\n{xref}\n%%EOF\n");
        }

        var stream = new TestStream([(0, header), (Start, Encoding.ASCII.GetBytes(body.ToString()))], Start + body.Length);

        var document = PdfDocument.OpenSeekable(stream);

        Assert.Equal(repair, document.IsRepaired);
        Assert.Equal("Far away", document.Pages[0].ExtractText());
    }

    [Fact]
    public void RecoveryScanOfAStreamFindsWhatTheScanOfMemoryFinds()
    {
        // The stream is scanned in chunks of 4 MiB: objects and trailers are placed around the end of the first chunk
        var file = new StringBuilder("%PDF-1.7\n");
        file.Append('%', (4 * 1024 * 1024) - 300).Append('\n');
        for (var i = 0; i < 40; i++)
        {
            file.Append(System.Globalization.CultureInfo.InvariantCulture, $"{i + 1} 0 obj\n(x)\nendobj\n");
            if (i % 5 == 0)
            {
                file.Append("trailer\n<< /Root 1 0 R >>\n");
            }
        }

        for (var shift = 0; shift < 12; shift++)
        {
            var bytes = Encoding.Latin1.GetBytes(new string(' ', shift) + file);
            var memory = Parsing.PdfRecoveryScanner.Scan(new Parsing.MemoryPdfInput(bytes), maxDepth: 64);
            var stream = Parsing.PdfRecoveryScanner.Scan(new Parsing.StreamPdfInput(new MemoryStream(bytes)), maxDepth: 64);

            Assert.HasCount(40, memory.Offsets);
            Assert.Equal(memory.Offsets.OrderBy(o => o.Key), stream.Offsets.OrderBy(o => o.Key));
            Assert.Equal(memory.TrailerOffsets, stream.TrailerOffsets);
        }
    }

    [Fact]
    public void ReadingADocumentDoesNotChangeItsObjects()
    {
        var file = HandWrittenPdf.CreateSimple().Add(6, "<< /Title (T) /Nested << /Array [1 [2 3] << /A 1 >>] >> >>").ToArray();
        var document = PdfDocument.Open(file);
        document.Pages[0].ExtractText();
        foreach (var reference in document.Objects.GetReferences())
        {
            reference.Resolve();
        }

        using var output = new MemoryStream();
        document.SaveIncremental(output);

        var update = Encoding.Latin1.GetString(output.ToArray()[file.Length..]);
        Assert.DoesNotContain(" obj", update, StringComparison.Ordinal);
    }

    [Fact]
    public void IncrementalSaveWritesTheObjectsWhoseNestedObjectsChanged()
    {
        var file = HandWrittenPdf.CreateSimple().Add(6, "<< /Nested << /Array [1 [2 3]] >> >>").Add(7, "(Seven)").Add(8, HandWrittenPdf.Stream("q Q")).ToArray();
        var document = PdfDocument.Open(file);
        var objects = document.Objects;
        Assert.True(objects.TryGetReference(6, out var nested));
        Assert.True(objects.TryGetReference(7, out var replaced));
        Assert.True(objects.TryGetReference(8, out var stream));
        Assert.True(objects.TryGetReference(4, out var font));
        font.Resolve();

        var array = Assert.IsType<PdfArray>(Assert.IsType<PdfDictionary>(nested.Resolve()).Get<PdfDictionary>(PdfName.Create("Nested"))!.Get<PdfArray>(PdfName.Create("Array"))!);
        Assert.IsType<PdfArray>(array[1]).Add(PdfInteger.Get(4));
        objects.Set(replaced, PdfString.FromText("Replaced"));
        Assert.IsType<PdfStream>(stream.Resolve()).SetData("Q q"u8);

        using var output = new MemoryStream();
        document.SaveIncremental(output);

        var update = Encoding.Latin1.GetString(output.ToArray()[file.Length..]);
        Assert.Contains("6 0 obj", update, StringComparison.Ordinal);
        Assert.Contains("7 0 obj", update, StringComparison.Ordinal);
        Assert.Contains("8 0 obj", update, StringComparison.Ordinal);
        Assert.DoesNotContain("4 0 obj", update, StringComparison.Ordinal);
        var read = PdfDocument.Open(output.ToArray());
        Assert.True(read.Objects.TryGetReference(6, out var readNested));
        Assert.HasCount(3, Assert.IsType<PdfArray>(Assert.IsType<PdfDictionary>(readNested.Resolve()).Get<PdfDictionary>(PdfName.Create("Nested"))!.Get<PdfArray>(PdfName.Create("Array"))![1]));
    }

    [Fact]
    public void Example_UnencryptedWrapper()
    {
        var secret = new PdfDocument();
        secret.Pages.Add(PdfSize.A4).CreateContentBuilder().DrawText("Secret", PdfFont.GetStandardFont(PdfStandardFont.Helvetica), 24, 60, 700);
        secret.Encryption = new PdfEncryption("password", "owner");
        var encryptedPdf = secret.ToByteArray();

        #region Pdf_UnencryptedWrapper
        // The wrapper is not encrypted: its pages tell a reader without the cryptographic filter how to open the payload
        var wrapper = new PdfDocument();
        wrapper.Pages.Add(PdfSize.A4).CreateContentBuilder()
            .DrawText("This document is encrypted: open it with a PDF 2.0 reader", PdfFont.GetStandardFont(PdfStandardFont.Helvetica), 12, 60, 700);
        wrapper.SetEncryptedPayload("document.pdf", encryptedPdf, cryptographicFilter: "Standard");
        var file = wrapper.ToByteArray();

        // A reader finds the payload, and opens it with its password
        var payload = PdfDocument.Open(file).EncryptedPayload;
        var document = payload?.OpenDocument(new PdfReadOptions { Password = "password" });
        #endregion

        Assert.NotNull(payload);
        Assert.Equal("Standard", payload.CryptographicFilter);
        Assert.Null(payload.Version);
        Assert.Equal("Secret", document!.Pages[0].ExtractText());
        var read = PdfDocument.Open(file);
        Assert.False(read.IsEncrypted);
        Assert.Equal(PdfVersion.Pdf20, read.Version);
        Assert.Throws<InvalidOperationException>(() => read.SetEncryptedPayload("other.pdf", encryptedPdf, "Standard"));
    }

    [Fact]
    public void HandWrittenUnencryptedWrapperIsRead()
    {
        // ISO 32000-2, 7.6.7: the payload named by the collection, among other embedded files
        var payload = Encoding.Latin1.GetString(HandWrittenPdf.CreateSimple("Payload").ToArray());
        var file = new HandWrittenPdf()
            .Add(1, "<< /Type /Catalog /Pages 2 0 R /Collection << /Type /Collection /D (payload.pdf) /View /H >> /AF [5 0 R] /Names << /EmbeddedFiles << /Names [(notes.txt) 7 0 R (payload.pdf) 5 0 R] >> >> >>")
            .Add(2, "<< /Type /Pages /Kids [] /Count 0 >>")
            .Add(5, "<< /Type /Filespec /F (payload.pdf) /UF (payload.pdf) /AFRelationship /EncryptedPayload /EP << /Type /EncryptedPayload /Subtype /ExampleFilter /Version /1.2 >> /EF << /F 6 0 R >> >>")
            .Add(6, HandWrittenPdf.Stream(payload, " /Type /EmbeddedFile /Subtype /application#2Fpdf"))
            .Add(7, "<< /Type /Filespec /F (notes.txt) /EF << /F 8 0 R >> >>")
            .Add(8, HandWrittenPdf.Stream("Notes", " /Type /EmbeddedFile"));
        file.Header = "%PDF-2.0\n";

        var document = PdfDocument.Open(file.ToArray());
        var encrypted = document.EncryptedPayload;

        Assert.NotNull(encrypted);
        Assert.Equal("payload.pdf", encrypted.File.FileName);
        Assert.Equal("ExampleFilter", encrypted.CryptographicFilter);
        Assert.Equal("1.2", encrypted.Version);
        Assert.Equal("Payload", encrypted.OpenDocument().Pages[0].ExtractText());
        Assert.Null(PdfDocument.Open(HandWrittenPdf.CreateSimple().ToArray()).EncryptedPayload);
    }

    [Fact]
    public void EncryptedPayloadIsDecodedWithTheLimitsOfTheWrapper()
    {
        var wrapper = new PdfDocument();
        wrapper.SetEncryptedPayload("document.pdf", new byte[100_000], "Standard");
        var file = wrapper.ToByteArray();

        var payload = PdfDocument.Open(file, new PdfReadOptions { MaxDecodedStreamLength = 1000 }).EncryptedPayload;

        Assert.NotNull(payload);
        Assert.Throws<PdfResourceLimitException>(payload.GetData);
    }

    /// <summary>A stream over bytes, or over segments of a sparse file of NUL bytes, that counts what is read.</summary>
    private sealed class TestStream : Stream
    {
        private readonly (long Offset, byte[] Data)[] _segments;
        private readonly long _length;
        private readonly bool _seekable;
        private long _position;

        public TestStream(byte[] data, bool seekable)
        {
            _segments = [(0, data)];
            _length = data.Length;
            _seekable = seekable;
        }

        public TestStream((long Offset, byte[] Data)[] segments, long length)
        {
            _segments = segments;
            _length = length;
            _seekable = true;
        }

        public long BytesRead { get; private set; }

        public long Offset => _position;

        public override bool CanRead => true;

        public override bool CanSeek => _seekable;

        public override bool CanWrite => false;

        public override long Length => _seekable ? _length : throw new NotSupportedException();

        public override long Position
        {
            get => _seekable ? _position : throw new NotSupportedException();
            set => _position = value;
        }

        public override void Flush()
        {
        }

        public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));

        public override int Read(Span<byte> buffer)
        {
            var count = (int)Math.Min(buffer.Length, Math.Max(0, _length - _position));
            buffer = buffer[..count];
            buffer.Clear();
            foreach (var (start, data) in _segments)
            {
                var from = Math.Max(start, _position);
                var to = Math.Min(start + data.Length, _position + count);
                if (from < to)
                {
                    data.AsSpan((int)(from - start), (int)(to - from)).CopyTo(buffer[(int)(from - _position)..]);
                }
            }

            _position += count;
            BytesRead += count;
            return count;
        }

        public override long Seek(long offset, SeekOrigin origin)
        {
            if (!_seekable)
                throw new NotSupportedException();

            _position = origin switch
            {
                SeekOrigin.Begin => offset,
                SeekOrigin.Current => _position + offset,
                _ => _length + offset,
            };
            return _position;
        }

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
