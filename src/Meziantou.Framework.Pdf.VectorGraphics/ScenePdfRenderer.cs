using Meziantou.Framework.Pdf.VectorGraphics.Internal;
using Meziantou.Framework.VectorGraphics;

namespace Meziantou.Framework.Pdf.VectorGraphics;

/// <summary>Draws the scenes of <c>Meziantou.Framework.VectorGraphics</c> (charts, diagrams) as PDF, as vector shapes and text.</summary>
/// <remarks>
/// <para>
/// A source is laid out with the fonts that draw it (<see cref="CreateMeasurer"/>), so its texts fit. Each text is drawn
/// with the face of its family of the fonts of the scene (<see cref="Scene.Fonts"/>, then <see cref="ScenePdfOptions.Fonts"/>),
/// and the PDF embeds the subsets of the faces it uses. Without fonts, the texts are drawn with the standard font
/// Helvetica, which has the widths <see cref="AfmTextMeasurer"/> measures; the characters it cannot show are drawn as
/// question marks.
/// </para>
/// <para>
/// The hyperlinks of the groups become link annotations of the page. The tooltips, the CSS classes and the data
/// attributes are not drawn. A gradient whose stops have different opacities is drawn with their average opacity.
/// PDF has no filters: a scene with a blur or a drop shadow (<see cref="SceneElement.Filter"/>) throws
/// <see cref="NotSupportedException"/>.
/// </para>
/// </remarks>
public static class ScenePdfRenderer
{
    /// <summary>Lays a source out with the fonts of the options and draws it as a document of one page of its size times <see cref="ScenePdfOptions.Scale"/>.</summary>
    /// <param name="source">The chart or the diagram.</param>
    /// <param name="options">The options, or <see langword="null"/> for the defaults.</param>
    /// <returns>The document. Save it with <see cref="PdfDocument.Save(string, PdfSaveOptions?)"/>.</returns>
    /// <exception cref="ArgumentOutOfRangeException">The size of the scene is not a positive finite number.</exception>
    /// <exception cref="NotSupportedException">An element has a filter (<see cref="SceneElement.Filter"/>): PDF has no filters.</exception>
    public static PdfDocument ToPdf(this ISceneSource source, ScenePdfOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(source);
        options ??= new ScenePdfOptions();
        return ToPdf(source.CreateScene(CreateMeasurer(options)), options);
    }

    /// <summary>
    /// Draws a scene as a document of one page of its size times <see cref="ScenePdfOptions.Scale"/>. The title and the
    /// description of the scene are the title and the subject of the document. Unless <see cref="ScenePdfOptions.Tagged"/>
    /// is <see langword="false"/>, the document is tagged: its structure is a document whose figure is the drawing.
    /// </summary>
    /// <param name="scene">The scene, laid out with <see cref="CreateMeasurer"/> so its texts fit.</param>
    /// <param name="options">The options, or <see langword="null"/> for the defaults.</param>
    /// <returns>The document. Save it with <see cref="PdfDocument.Save(string, PdfSaveOptions?)"/>.</returns>
    /// <exception cref="ArgumentOutOfRangeException">The size of the scene is not a positive finite number.</exception>
    /// <exception cref="NotSupportedException">An element has a filter (<see cref="SceneElement.Filter"/>): PDF has no filters. Nothing is drawn.</exception>
    public static PdfDocument ToPdf(this Scene scene, ScenePdfOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(scene);
        options ??= new ScenePdfOptions();
        var width = scene.Width * options.Scale;
        var height = scene.Height * options.Scale;
        if (!(width > 0) || !(height > 0) || !double.IsFinite(width) || !double.IsFinite(height))
            throw new ArgumentOutOfRangeException(nameof(scene), "The size of the scene must be a positive finite number.");

        ScenePdfWriter.ThrowIfUnsupported(scene);
        var document = new PdfDocument();
        if (!string.IsNullOrEmpty(scene.Title))
        {
            document.Information.Title = scene.Title;
        }

        if (!string.IsNullOrEmpty(scene.Description))
        {
            document.Information.Subject = scene.Description;
        }

        var page = document.Pages.Add(new PdfSize(width, height));
        page.DrawScene(scene, new PdfPoint(0, height), new ScenePdfOptions
        {
            Scale = options.Scale,
            Fonts = options.Fonts,
            Tagged = options.Tagged,
            StructureParent = options.Tagged ? document.StructureTree.AddElement("Document") : null,
        });
        return document;
    }

    /// <summary>Lays a source out with the fonts of the options and draws it on a page.</summary>
    /// <param name="page">The page.</param>
    /// <param name="source">The chart or the diagram.</param>
    /// <param name="position">The top-left corner of the drawing, in the coordinates of the page (the y axis points up).</param>
    /// <param name="options">The options, or <see langword="null"/> for the defaults.</param>
    /// <returns>The rectangle the drawing covers.</returns>
    /// <exception cref="NotSupportedException">An element has a filter (<see cref="SceneElement.Filter"/>): PDF has no filters. Nothing is drawn.</exception>
    public static PdfRectangle DrawScene(this PdfPage page, ISceneSource source, PdfPoint position, ScenePdfOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(source);
        options ??= new ScenePdfOptions();
        return page.DrawScene(source.CreateScene(CreateMeasurer(options)), position, options);
    }

    /// <summary>Draws a scene on a page, over its content.</summary>
    /// <param name="page">The page.</param>
    /// <param name="scene">The scene, laid out with <see cref="CreateMeasurer"/> so its texts fit.</param>
    /// <param name="position">The top-left corner of the drawing, in the coordinates of the page (the y axis points up).</param>
    /// <param name="options">The options, or <see langword="null"/> for the defaults.</param>
    /// <returns>The rectangle the drawing covers.</returns>
    /// <exception cref="ArgumentException">The position is not finite.</exception>
    /// <exception cref="NotSupportedException">An element has a filter (<see cref="SceneElement.Filter"/>): PDF has no filters. Nothing is drawn.</exception>
    public static PdfRectangle DrawScene(this PdfPage page, Scene scene, PdfPoint position, ScenePdfOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(page);
        ArgumentNullException.ThrowIfNull(scene);
        if (!double.IsFinite(position.X) || !double.IsFinite(position.Y))
            throw new ArgumentException("The position must be finite.", nameof(position));

        options ??= new ScenePdfOptions();
        ScenePdfWriter.ThrowIfUnsupported(scene);
        var content = page.CreateContentBuilder();

        // Tagged, the drawing is a figure of the structure tree, whose alternate description is the title and the
        // description of the scene (ISO 32000-2, 14.8.4.6)
        PdfStructureElement? figure = null;
        if (options.Tagged)
        {
            figure = options.StructureParent is { } parent ? parent.AddElement("Figure") : page.Document.StructureTree.AddElement("Figure");
            figure.AlternateDescription = GetAlternateDescription(scene);
            content.BeginStructureElement(figure);
        }

        new ScenePdfWriter(page, content, scene, scene.Fonts ?? options.Fonts, figure).Draw(position, options.Scale);
        if (figure is not null)
        {
            content.EndMarkedContent();
        }

        content.Complete();
        return PdfRectangle.FromSize(position.X, position.Y - (scene.Height * options.Scale), scene.Width * options.Scale, scene.Height * options.Scale);
    }

    // The title and the description, as one sentence each
    private static string? GetAlternateDescription(Scene scene)
    {
        var title = scene.Title?.Trim();
        var description = scene.Description?.Trim();
        if (string.IsNullOrEmpty(title))
            return string.IsNullOrEmpty(description) ? null : description;

        if (string.IsNullOrEmpty(description))
            return title;

        return title[^1] is '.' or '!' or '?' or ':' ? title + " " + description : title + ". " + description;
    }

    /// <summary>Creates the text measurer of the fonts of the options, to lay a source out with <see cref="ISceneSource.CreateScene"/>.</summary>
    /// <param name="options">The options, or <see langword="null"/> for the defaults.</param>
    /// <returns>A <see cref="FontFaceTextMeasurer"/> of <see cref="ScenePdfOptions.Fonts"/>, or <see cref="AfmTextMeasurer.Default"/>, the metrics of Helvetica, without fonts.</returns>
    public static ITextMeasurer CreateMeasurer(ScenePdfOptions? options = null)
    {
        return options?.Fonts is { } fonts ? new FontFaceTextMeasurer(fonts) : AfmTextMeasurer.Default;
    }
}
