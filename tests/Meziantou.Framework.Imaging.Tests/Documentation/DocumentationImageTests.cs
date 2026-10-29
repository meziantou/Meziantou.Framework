using Meziantou.Framework.Imaging.Formats;
using Meziantou.Framework.Imaging.Samples;
using Meziantou.Framework.Imaging.TestHarness;
using Meziantou.Framework.Imaging.TestHarness.Fixtures;
using Xunit.Sdk;

namespace Meziantou.Framework.Imaging.Tests.Documentation;

/// <summary>
/// Renders the images of the documentation site (docs/01-Imaging/*/images): each test runs a drawing or text example of
/// the samples with fixed inputs, and compares its pixels with the committed PNG. With the environment variable
/// <see cref="UpdateVariable"/> set to 1, a missing or different image is written instead (see docs/AGENTS.md).
/// </summary>
public sealed class DocumentationImageTests : IDisposable
{
    private const string UpdateVariable = "MEZIANTOU_IMAGING_UPDATE_DOCS_IMAGES";
    private const string Drawing = "07-Drawing";
    private const string Text = "08-Text";

    private static readonly FullPath DocumentationRoot = RepositoryPaths.Root / "docs" / "01-Imaging";
    private static readonly FullPath RegularFont = FixtureRoot.GetDirectory() / "fonts" / "noto-sans" / "NotoSans-Regular.subset.ttf";

    private readonly FullPath _directory = FullPath.GetTempPath() / "Meziantou.Framework.Imaging.Tests" / ("docs-images-" + Guid.NewGuid().ToString("N"));

    public DocumentationImageTests() => Directory.CreateDirectory(_directory);

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    private FullPath GetPath(string name) => _directory / name;

    /// <summary>The picture the examples that need a photo draw on: the landscape of the first drawing example.</summary>
    private FullPath CreatePhoto()
    {
        var path = GetPath("photo.png");
        Examples.DrawLandscape(path);
        return path;
    }

    [Fact]
    public void DrawLandscape() => AssertImage(Drawing, nameof(Examples.DrawLandscape), Examples.DrawLandscape);

    [Fact]
    public void DrawChart() => AssertImage(Drawing, nameof(Examples.DrawChart), Examples.DrawChart);

    [Fact]
    public void DrawSticker() => AssertImage(Drawing, nameof(Examples.DrawSticker), Examples.DrawSticker);

    [Fact]
    public void DrawIcon() => AssertImage(Drawing, nameof(Examples.DrawIcon), output => Examples.DrawIcon(output));

    [Fact]
    public void DrawRoute() => AssertImage(Drawing, nameof(Examples.DrawRoute), Examples.DrawRoute);

    [Fact]
    public void ComposeCard()
    {
        var photo = CreatePhoto();
        AssertImage(Drawing, nameof(Examples.ComposeCard), output => Examples.ComposeCard(photo, output));
    }

    [Fact]
    public void DrawOnScreen()
    {
        var desk = GetPath("desk.png");
        using (var image = new Image<Rgba32>(220, 180, new Rgba32(90, 80, 70)))
        {
            image.Save(desk);
        }

        var screenshot = CreatePhoto();
        AssertImage(Drawing, nameof(Examples.DrawOnScreen), output => Examples.DrawOnScreen(desk, screenshot, output));
    }

    [Fact]
    public void DrawTiltedCard() => AssertImage(Drawing, nameof(Examples.DrawTiltedCard), Examples.DrawTiltedCard);

    [Fact]
    public void DrawBadge() => AssertImage(Drawing, nameof(Examples.DrawBadge), Examples.DrawBadge);

    [Fact]
    public void DrawGlassPanel()
    {
        var photo = CreatePhoto();
        AssertImage(Drawing, nameof(Examples.DrawGlassPanel), output => Examples.DrawGlassPanel(photo, output));
    }

    [Fact]
    public void DrawFrostedCard()
    {
        var photo = CreatePhoto();
        AssertImage(Drawing, nameof(Examples.DrawFrostedCard), output => Examples.DrawFrostedCard(photo, output));
    }

    [Fact]
    public void DrawGlowingRing() => AssertImage(Drawing, nameof(Examples.DrawGlowingRing), Examples.DrawGlowingRing);

    [Fact]
    public void DrawSprite() => AssertImage(Drawing, nameof(Examples.DrawSprite), Examples.DrawSprite);

    [Fact]
    public void DrawBrandColors()
    {
        // A photograph with the profile of a wider color space
        var photo = GetPath("wide.png");
        using (var image = Image.Load<Rgb24>(CreatePhoto()))
        {
            image.Metadata.IccProfile = IccTestProfiles.Rgb(IccTestProfiles.DisplayP3Colorants, IccTestProfiles.SrgbCurve());
            image.Save(photo);
        }

        AssertImage(Drawing, nameof(Examples.DrawBrandColors), output => Examples.DrawBrandColors(photo, output));
    }

    [Fact]
    public void MarkAnimation()
    {
        var apng = GetPath("animation.apng");
        Examples.BuildAnimation(apng, GetPath("animation.gif"));
        AssertImage(Drawing, nameof(Examples.MarkAnimation), output => Examples.MarkAnimation(apng, output));
    }

    [Fact]
    public void DrawWeights()
    {
        var font = FixtureRoot.GetDirectory() / "fonts" / "noto-sans-variable" / "NotoSans-Variable.subset.ttf";
        AssertImage(Text, nameof(Examples.DrawWeights), output => Examples.DrawWeights(font, output));
    }

    [Fact]
    public void DrawLabel() => AssertImage(Text, nameof(Examples.DrawLabel), output => Examples.DrawLabel(RegularFont, output));

    [Fact]
    public void DrawSmallLabel()
    {
        var font = FixtureRoot.GetDirectory() / "font-suites" / "truetype-hinting" / "DroidSans.subset.ttf";
        AssertImage(Text, nameof(Examples.DrawSmallLabel), output => Examples.DrawSmallLabel(font, output));
    }

    [Fact]
    public void DrawPayButton() => AssertImage(Text, nameof(Examples.DrawPayButton), output => Examples.DrawPayButton(RegularFont, output));

    [Fact]
    public void DrawParagraph() => AssertImage(Text, nameof(Examples.DrawParagraph), output => Examples.DrawParagraph(RegularFont, output));

    [Fact]
    public void DrawWatermark()
    {
        var photo = CreatePhoto();
        AssertImage(Text, nameof(Examples.DrawWatermark), output => Examples.DrawWatermark(RegularFont, photo, output));
    }

    [Fact]
    public void DrawTitleCard()
    {
        var icon = GetPath("icon.png");
        Examples.DrawIcon(icon);
        AssertImage(Text, nameof(Examples.DrawTitleCard), output => Examples.DrawTitleCard(RegularFont, icon, output));
    }

    [Fact]
    public void DrawVerticalText()
    {
        var font = FixtureRoot.GetDirectory() / "fonts" / "noto-sans-cjk" / "NotoSansCJKjp-Regular.subset.otf";
        AssertImage(Text, nameof(Examples.DrawVerticalText), output => Examples.DrawVerticalText(font, output));
    }

    [Fact]
    public void DrawTextOnPath() => AssertImage(Text, nameof(Examples.DrawTextOnPath), output => Examples.DrawTextOnPath(RegularFont, output));

    [Fact]
    public void AddLogoWatermark()
    {
        // The sticker of the drawing examples is the logo: a shape with a transparent background
        var logo = GetPath("logo.png");
        Examples.DrawSticker(logo);
        var photo = CreatePhoto();
        AssertImage(Drawing, nameof(GuideExamples.AddLogoWatermark), output => GuideExamples.AddLogoWatermark(photo, logo, output));
    }

    [Fact]
    public void DrawBasicShapes() => AssertImage(Drawing, nameof(DrawingExamples.DrawBasicShapes), DrawingExamples.DrawBasicShapes);

    [Fact]
    public void DrawPin() => AssertImage(Drawing, nameof(DrawingExamples.DrawPin), DrawingExamples.DrawPin);

    [Fact]
    public void DrawFillRules() => AssertImage(Drawing, nameof(DrawingExamples.DrawFillRules), DrawingExamples.DrawFillRules);

    [Fact]
    public void DrawCombinedPaths() => AssertImage(Drawing, nameof(DrawingExamples.DrawCombinedPaths), DrawingExamples.DrawCombinedPaths);

    [Fact]
    public void DrawGradients() => AssertImage(Drawing, nameof(DrawingExamples.DrawGradients), DrawingExamples.DrawGradients);

    [Fact]
    public void DrawGradientColorSpaces() => AssertImage(Drawing, nameof(DrawingExamples.DrawGradientColorSpaces), DrawingExamples.DrawGradientColorSpaces);

    [Fact]
    public void DrawJoinsAndCaps() => AssertImage(Drawing, nameof(DrawingExamples.DrawJoinsAndCaps), DrawingExamples.DrawJoinsAndCaps);

    [Fact]
    public void DrawDashes() => AssertImage(Drawing, nameof(DrawingExamples.DrawDashes), DrawingExamples.DrawDashes);

    [Fact]
    public void DrawPatterns() => AssertImage(Drawing, nameof(DrawingExamples.DrawPatterns), DrawingExamples.DrawPatterns);

    [Fact]
    public void DrawClock() => AssertImage(Drawing, nameof(DrawingExamples.DrawClock), DrawingExamples.DrawClock);

    [Fact]
    public void DrawScaledIcons() => AssertImage(Drawing, nameof(DrawingExamples.DrawScaledIcons), DrawingExamples.DrawScaledIcons);

    [Fact]
    public void DrawBlendModes() => AssertImage(Drawing, nameof(DrawingExamples.DrawBlendModes), DrawingExamples.DrawBlendModes);

    [Fact]
    public void DrawTicket() => AssertImage(Drawing, nameof(DrawingExamples.DrawTicket), DrawingExamples.DrawTicket);

    [Fact]
    public void DrawSpotlight()
    {
        var photo = CreatePhoto();
        AssertImage(Drawing, nameof(DrawingExamples.DrawSpotlight), output => DrawingExamples.DrawSpotlight(photo, output));
    }

    [Fact]
    public void DrawEdges() => AssertImage(Drawing, nameof(DrawingExamples.DrawEdges), DrawingExamples.DrawEdges);

    [Fact]
    public void DrawProgressBar()
    {
        var apng = GetPath("animation.apng");
        Examples.BuildAnimation(apng, GetPath("animation.gif"));
        AssertImage(Drawing, nameof(DrawingExamples.DrawProgressBar), output => DrawingExamples.DrawProgressBar(apng, output));
    }

    [Fact]
    public void DrawFontStack()
    {
        var fonts = FixtureRoot.GetDirectory() / "fonts";
        var japanese = fonts / "noto-sans-cjk" / "NotoSansCJKjp-Regular.subset.otf";
        var emoji = fonts / "noto-color-emoji" / "Noto-COLRv1.subset.ttf";
        AssertImage(Text, nameof(TextExamples.DrawFontStack), output => TextExamples.DrawFontStack(RegularFont, japanese, emoji, output));
    }

    [Fact]
    public void DrawMixedDirections()
    {
        var hebrew = FixtureRoot.GetDirectory() / "fonts" / "noto-sans-hebrew" / "NotoSansHebrew-Regular.ttf";
        AssertImage(Text, nameof(TextExamples.DrawMixedDirections), output => TextExamples.DrawMixedDirections(RegularFont, hebrew, output));
    }

    [Fact]
    public void DrawSyntheticStyles() => AssertImage(Text, nameof(TextExamples.DrawSyntheticStyles), output => TextExamples.DrawSyntheticStyles(RegularFont, output));

    [Fact]
    public void DrawProductCard() => AssertImage(Text, nameof(TextExamples.DrawProductCard), output => TextExamples.DrawProductCard(RegularFont, output));

    [Fact]
    public void DrawMeasuredText() => AssertImage(Text, nameof(TextExamples.DrawMeasuredText), output => TextExamples.DrawMeasuredText(RegularFont, output));

    [Fact]
    public void DrawSelection() => AssertImage(Text, nameof(TextExamples.DrawSelection), output => TextExamples.DrawSelection(RegularFont, new PointD(120, 50), output));

    /// <summary>Runs an example and compares the image it writes with docs/01-Imaging/<paramref name="group"/>/images/<paramref name="name"/>.png.</summary>
    private void AssertImage(string group, string name, Action<string> draw)
    {
        var actualPath = GetPath(name + ".png");
        draw(actualPath);

        var expectedPath = DocumentationRoot / group / "images" / (name + ".png");
        var difference = File.Exists(expectedPath) ? Compare(expectedPath, actualPath) : "the image does not exist";
        if (difference is null)
            return;

        var relativePath = expectedPath.MakePathRelativeTo(RepositoryPaths.Root).Replace('\\', '/');
        if (Environment.GetEnvironmentVariable(UpdateVariable) is not "1")
            throw new XunitException($"{relativePath}: {difference}. Run the test with the environment variable {UpdateVariable}=1 to write the image, look at it, and commit it.");

        // The test runs for each target framework at the same time: publish the file with a rename
        Directory.CreateDirectory(expectedPath.Parent);
        var temporaryPath = expectedPath.Parent / (name + "." + Guid.NewGuid().ToString("N") + ".tmp");
        using (var image = Image.Load(actualPath))
        using (var stream = File.Create(temporaryPath))
        {
            image.Save(stream, new PngEncoder { Optimization = PngOptimization.MinimumSize });
        }

        File.Move(temporaryPath, expectedPath, overwrite: true);
    }

    /// <summary>Compares the size, the frames, their durations and their pixels, at 16 bits per sample.</summary>
    private static string? Compare(string expectedPath, string actualPath)
    {
        using var expected = Image.Load<Rgba64>(expectedPath);
        using var actual = Image.Load<Rgba64>(actualPath);
        if (expected.Size != actual.Size)
            return $"the size changed from {expected.Size} to {actual.Size}";

        if (expected.Frames.Count != actual.Frames.Count)
            return $"the frame count changed from {expected.Frames.Count} to {actual.Frames.Count}";

        var expectedBytes = new byte[expected.Width * expected.Height * 8];
        var actualBytes = new byte[expectedBytes.Length];
        for (var i = 0; i < expected.Frames.Count; i++)
        {
            if (expected.Frames[i].Metadata.Duration != actual.Frames[i].Metadata.Duration)
                return $"the duration of frame {i} changed";

            expected.Frames[i].CopyPixelBytesTo(expectedBytes);
            actual.Frames[i].CopyPixelBytesTo(actualBytes);
            if (!expectedBytes.AsSpan().SequenceEqual(actualBytes))
                return $"the pixels of frame {i} changed";
        }

        return null;
    }
}
