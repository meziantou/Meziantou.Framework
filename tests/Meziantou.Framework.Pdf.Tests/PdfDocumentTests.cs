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
}
