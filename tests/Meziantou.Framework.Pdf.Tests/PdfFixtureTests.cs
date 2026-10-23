using System.Text;
using Meziantou.Framework.Pdf.TestHarness;
using Meziantou.Framework.Pdf.TestHarness.Fixtures;

namespace Meziantou.Framework.Pdf.Tests;

public sealed class PdfFixtureTests
{
    private static readonly PdfFixtureManifest Manifest = PdfFixtureManifest.Load();

    public static TheoryData<string> Fixtures => new(Manifest.Fixtures.Select(f => f.Path));

    [Fact]
    public void EveryFileIsInTheManifest()
    {
        var listed = new HashSet<string>(Manifest.Fixtures.Select(f => f.Path), StringComparer.Ordinal);
        foreach (var dataSet in Manifest.Vendored)
        {
            foreach (var file in dataSet.Files.Keys)
            {
                listed.Add(dataSet.Path + "/" + file);
            }
        }

        string[] notListed = ["README.md", "manifest.json", "arlington/LICENSE"];
        var root = RepositoryPaths.Fixtures;
        var files = Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
            .Select(path => Path.GetRelativePath(root, path).Replace('\\', '/'))
            .Where(path => !notListed.Contains(path, StringComparer.Ordinal) && !path.StartsWith("sources/", StringComparison.Ordinal))
            .ToList();

        Assert.DoesNotContain(files, file => !listed.Contains(file));
        Assert.DoesNotContain(listed, file => !files.Contains(file, StringComparer.Ordinal));
    }

    [Fact]
    public void HashesMatch()
    {
        foreach (var fixture in Manifest.Fixtures)
        {
            Assert.Equal(fixture.Sha256, PdfFixtureManifest.ComputeSha256(PdfFixtureManifest.GetPath(fixture)), $"{fixture.Path}");
        }

        foreach (var dataSet in Manifest.Vendored)
        {
            foreach (var (file, hash) in dataSet.Files)
            {
                Assert.Equal(hash, PdfFixtureManifest.ComputeSha256(RepositoryPaths.Fixtures / dataSet.Path / file), $"{dataSet.Path}/{file}");
            }
        }
    }

    [Theory]
    [MemberData(nameof(Fixtures))]
    public void Expectations(string path)
    {
        var fixture = Manifest.Fixtures.Single(f => f.Path == path);
        var expected = fixture.Expected;
        var data = File.ReadAllBytes(PdfFixtureManifest.GetPath(fixture));
        if (expected?.Exception is { } exception)
        {
            var ex = Assert.ThrowsAny<PdfException>(() => PdfDocument.Open(data));
            Assert.Equal(exception, ex.GetType().Name);
            return;
        }

        var document = PdfDocument.Open(data);
        if (expected is null)
            return;

        if (expected.Pages is { } pages)
        {
            Assert.Equal(pages, document.Pages.Count);
        }

        if (expected.Version is { } version)
        {
            Assert.Equal(version, document.Version.ToString());
        }

        if (expected.Repaired is { } repaired)
        {
            Assert.Equal(repaired, document.IsRepaired);
        }

        if (expected.Title is { } title)
        {
            Assert.Equal(title, document.Information.Title);
        }

        if (expected.Text is { } text)
        {
            Assert.Equal(text, document.Pages.Select(page => page.ExtractText()));
        }

        // Every fixture that can be read can be written in full and read back with the same text
        var saved = PdfDocument.Open(document.ToByteArray());
        Assert.False(saved.IsRepaired);
        Assert.Equal(document.Pages.Select(page => page.ExtractText()), saved.Pages.Select(page => page.ExtractText()));
    }

    [Fact]
    public void Navigation()
    {
        var document = Open("structure/navigation.pdf");

        Assert.Equal(["Chapter 1", "Chapter 2"], document.Outlines.Select(o => o.Title));
        var chapter1 = document.Outlines[0];
        Assert.False(chapter1.IsOpen);
        Assert.Equal(PdfDestinationKind.Xyz, chapter1.Destination!.Kind);
        Assert.Equal(792, chapter1.Destination.Top);
        Assert.Equal(0, chapter1.Destination.Left);
        Assert.Null(chapter1.Destination.Zoom);
        Assert.Equal("Section 1.1", Assert.Single(chapter1.Children).Title);
        Assert.Same(document.Pages[1], chapter1.Children[0].Destination!.Page);
        Assert.Equal(PdfDestinationKind.FitHorizontally, chapter1.Children[0].Destination!.Kind);

        var chapter2 = document.Outlines[1];
        Assert.Same(document.Pages[1], chapter2.Destination!.Page);
        Assert.Equal(PdfColor.FromRgb(0, 0, 1), chapter2.Color);
        Assert.True(chapter2.IsBold);
        Assert.True(chapter2.IsItalic);

        var annotations = document.Pages[0].Annotations;
        Assert.HasCount(3, annotations);
        var uri = Assert.IsType<PdfLinkAnnotation>(annotations[0]);
        Assert.Equal(new Uri("https://example.com/a?b=c"), uri.Uri);
        Assert.Equal(new PdfRectangle(72, 700, 200, 720), uri.Rectangle);
        Assert.Same(document.Pages[1], Assert.IsType<PdfLinkAnnotation>(annotations[1]).Destination!.Page);
        Assert.Equal("Text", annotations[2].Subtype);
        Assert.Equal("A note", annotations[2].Contents);
    }

    [Fact]
    public void Metadata()
    {
        var document = Open("structure/metadata.pdf");
        var information = document.Information;

        Assert.Equal("M\u00E9tadata", information.Title);
        Assert.Equal("Ann\u00E9e (B)", information.Author);
        Assert.Equal("Line 1\nLine 2", information.Subject);
        Assert.Equal("pdf, metadata", information.Keywords);
        Assert.Equal("Hand", information.Creator);
        Assert.Equal("Template", information.Producer);
        Assert.Equal(new DateTimeOffset(2024, 2, 29, 12, 34, 56, TimeSpan.FromHours(1)), information.CreationDate);
        Assert.Equal(new DateTimeOffset(2024, 3, 1, 0, 0, 0, TimeSpan.Zero), information.ModificationDate);
        Assert.Equal("Value", information.GetText("Custom"));
        Assert.Contains("<rdf:li xml:lang=\"x-default\">Metadata</rdf:li>", Encoding.UTF8.GetString(document.GetXmpMetadata()!), StringComparison.Ordinal);
    }

    [Fact]
    public void IncrementalUpdateOfAFileWithIncrementalUpdates()
    {
        var original = File.ReadAllBytes(RepositoryPaths.Fixtures / "structure" / "incremental.pdf");
        var document = PdfDocument.Open(original);
        document.Information.Author = "Third revision";

        using var output = new MemoryStream();
        document.SaveIncremental(output);

        var read = PdfDocument.Open(output.ToArray());
        Assert.Equal("Third revision", read.Information.Author);
        Assert.Equal("Second revision", read.Information.Title);
        Assert.Equal("Updated", read.Pages[0].ExtractText());
    }

    private static PdfDocument Open(string path) => PdfDocument.Open(File.ReadAllBytes(RepositoryPaths.Fixtures / path));
}
