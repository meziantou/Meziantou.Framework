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
