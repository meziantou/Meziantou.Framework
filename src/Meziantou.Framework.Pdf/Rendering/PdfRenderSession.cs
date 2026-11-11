using Meziantou.Framework.Imaging;
using Meziantou.Framework.Imaging.Drawing;
using Meziantou.Framework.Pdf.Objects;
using Meziantou.Framework.Pdf.Rendering.Graphics;

namespace Meziantou.Framework.Pdf.Rendering;

/// <summary>
/// Renders the pages of a document with caches the pages share: the fonts, images, color spaces, functions, glyph
/// procedures and meshes a page reads are kept for the next pages, so that the resources several pages use are read
/// and decoded once.
/// </summary>
/// <remarks>
/// <para>
/// The work each page may do is counted separately (<see cref="PdfRenderOptions.MaxOperations"/>): an image decoded for
/// a page is not decoded, nor charged, again for the next ones. The caches are emptied between two pages when what they
/// keep exceeds about 256 MB, and when the session is disposed of.
/// </para>
/// <para>
/// The caches are found by the objects of the document: a change made to an object of the document after a page that
/// uses it is rendered (the data of a stream, an entry of a dictionary) may not be seen by the next pages. Create a new
/// session after changing the document. A session renders one page at a time: it must not be used by several threads at
/// once.
/// </para>
/// </remarks>
public sealed class PdfRenderSession : IDisposable
{
    private readonly RenderCache _cache;
    private bool _isDisposed;

    /// <summary>Initializes a new instance of the <see cref="PdfRenderSession"/> class.</summary>
    /// <param name="document">The document whose pages are rendered.</param>
    /// <param name="options">The options; <see langword="null"/> for <see cref="PdfRenderOptions.Default"/>.</param>
    public PdfRenderSession(PdfDocument document, PdfRenderOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(document);
        Document = document;
        Options = options ?? PdfRenderOptions.Default;
        _cache = new RenderCache(Options);
    }

    /// <summary>Gets the document whose pages are rendered.</summary>
    public PdfDocument Document { get; }

    /// <summary>Gets the options of the rendering.</summary>
    public PdfRenderOptions Options { get; }

    /// <summary>Renders a page of the document to an image.</summary>
    /// <param name="page">The page, a page of <see cref="Document"/>.</param>
    /// <returns>The image: the crop box of the page (or the region of the options) at the resolution of the options, rotated by the <c>/Rotate</c> of the page.</returns>
    /// <exception cref="ArgumentException">The page is not a page of <see cref="Document"/>.</exception>
    /// <exception cref="ObjectDisposedException">The session is disposed of.</exception>
    /// <exception cref="PdfResourceLimitException">The page is too large at the resolution, or it does more work than <see cref="PdfRenderOptions.MaxOperations"/>.</exception>
    public Image<Rgba32> RenderPage(PdfPage page) => RenderPage(page, CancellationToken.None);

    /// <summary>Renders a page of the document to an image.</summary>
    /// <param name="page">The page, a page of <see cref="Document"/>.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests: it is checked as the operators, glyphs, images and shadings of the page are drawn.</param>
    /// <returns>The image: the crop box of the page (or the region of the options) at the resolution of the options, rotated by the <c>/Rotate</c> of the page.</returns>
    /// <exception cref="ArgumentException">The page is not a page of <see cref="Document"/>.</exception>
    /// <exception cref="ObjectDisposedException">The session is disposed of.</exception>
    /// <exception cref="PdfResourceLimitException">The page is too large at the resolution, or it does more work than <see cref="PdfRenderOptions.MaxOperations"/>.</exception>
    /// <exception cref="OperationCanceledException">The rendering was canceled.</exception>
    public Image<Rgba32> RenderPage(PdfPage page, CancellationToken cancellationToken)
    {
        CheckPage(page);
        var (width, height, _) = PageGeometry.Compute(page, Options);
        Image<Rgba32> image;
        try
        {
            image = new Image<Rgba32>(width, height, Options.Configuration);
        }
        catch (ImageResourceLimitException ex)
        {
            // The limits of the image configuration can be lower than the largest page the renderer accepts
            throw new PdfResourceLimitException($"The page is {width} x {height} pixels at {Options.Dpi} dpi: {ex.Message}", ex);
        }

        try
        {
            Render(page, image, backgroundBehind: true, cancellationToken);
        }
        catch
        {
            image.Dispose();
            throw;
        }

        return image;
    }

    /// <summary>
    /// Renders a page of the document to an image of the application, whatever its pixel format (a <see cref="Gray8"/>
    /// image gives a grayscale rendering, for instance): the page is drawn from the top left corner of the image, at the
    /// size <see cref="PdfRenderer.GetImageSize"/> gives. The background of the options is painted first, over the
    /// pixels of the image: set it to <see langword="null"/> to draw the page over what the image shows.
    /// </summary>
    /// <param name="page">The page, a page of <see cref="Document"/>.</param>
    /// <param name="target">The image the page is drawn on: every frame of it. What exceeds it is not drawn.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests. The shapes already drawn stay on the image.</param>
    /// <exception cref="ArgumentException">The page is not a page of <see cref="Document"/>.</exception>
    /// <exception cref="ObjectDisposedException">The session is disposed of.</exception>
    /// <exception cref="PdfResourceLimitException">The page is too large at the resolution, or it does more work than <see cref="PdfRenderOptions.MaxOperations"/>.</exception>
    /// <exception cref="OperationCanceledException">The rendering was canceled.</exception>
    public void RenderPage(PdfPage page, Image target, CancellationToken cancellationToken = default)
    {
        CheckPage(page);
        ArgumentNullException.ThrowIfNull(target);
        Render(page, target, backgroundBehind: false, cancellationToken);
    }

    private void CheckPage(PdfPage page)
    {
        ArgumentNullException.ThrowIfNull(page);
        ObjectDisposedException.ThrowIf(_isDisposed, this);
        if (!ReferenceEquals(page.Document, Document))
            throw new ArgumentException("The page is not a page of the document of the session", nameof(page));
    }

    /// <summary>Renders a page on an image: the background is painted behind the page on a new image, which is transparent, and before it on an image of the application, which may have no alpha.</summary>
    private void Render(PdfPage page, Image target, bool backgroundBehind, CancellationToken cancellationToken)
    {
        // The fonts a document that is being built embeds are written when it is saved: they are written now, so that
        // their glyphs are drawn with the embedded program
        Document.WriteEmbeddedFonts();
        var options = Options;
        var (pageWidth, pageHeight, matrix) = PageGeometry.Compute(page, options);
        var width = Math.Min(pageWidth, target.Width);
        var height = Math.Min(pageHeight, target.Height);
        _cache.BeginPage(cancellationToken);

        // The resolution of the device: the default user space unit is 1/72 inch times the user unit of the page
        var pageUserUnit = page.UserUnit is > 0 and var value && double.IsFinite(value) ? value : 1;
        _cache.DeviceResolution = Math.Sqrt(Math.Abs((matrix.A * matrix.D) - (matrix.B * matrix.C))) * 72 / pageUserUnit;
        try
        {
            target.Draw(canvas => canvas.Clip(new Rectangle(0, 0, width, height), clipped => clipped.Antialiasing(options.PathAntialiasing, content =>
            {
                if (!backgroundBehind && options.Background is { } paper)
                {
                    content.Fill(new SolidBrush(paper.ToRgba64()));
                }

                // The magnification: 100% is one pixel per point
                var userUnit = page.UserUnit is > 0 and var unit && double.IsFinite(unit) ? unit : 1;
                var zoom = Math.Sqrt(Math.Abs((matrix.A * matrix.D) - (matrix.B * matrix.C))) / userUnit * 100;
                var optionalContent = new OptionalContent(page.Document, options, zoom);

                // A knockout page group (11.4.7): the objects of the page are elements composited with the paper
                var pageGroup = page.Dictionary.Get<PdfDictionary>(PdfNames.Group);
                var isKnockout = pageGroup?.GetName(PdfNames.S)?.Value == "Transparency" && pageGroup.Get<PdfBoolean>(PdfNames.K)?.Value is true;
                void Draw(DrawingCanvas surface)
                {
                    var renderer = new ContentRenderer(_cache, page.Document, new RenderGraphicsState { Ctm = matrix }, depth: 0, optionalContent)
                    {
                        BaseMatrix = matrix,
                        DeviceSize = (width, height),
                        IsKnockoutContext = isKnockout,
                    };
                    renderer.Run(surface, GetContent(page), page.Resources);
                }

                if (isKnockout)
                {
                    content.Group(new GroupOptions { IsKnockout = true }, group => Draw(group));
                }
                else
                {
                    Draw(content);
                }

                if (options.RenderAnnotations)
                {
                    AnnotationRenderer.Render(page, content, _cache, matrix, (width, height), optionalContent);
                }

                // The page is a group composited on the paper (11.4.7): the content is drawn on a transparent backdrop,
                // which the blend modes see, then the background is put behind it
                if (backgroundBehind && options.Background is { } background)
                {
                    content.Fill(new SolidBrush(background.ToRgba64()) { CompositeMode = CompositeMode.DestinationOver });
                }
            })), cancellationToken);
        }
        finally
        {
            _cache.EndPage();
        }
    }

    /// <summary>Releases the objects the caches keep.</summary>
    public void Dispose()
    {
        if (_isDisposed)
            return;

        _isDisposed = true;
        _cache.Dispose();
    }

    /// <summary>Gets the content of a page: its streams joined, each that cannot be decoded left out (7.8.2).</summary>
    private static byte[] GetContent(PdfPage page)
    {
        var output = new MemoryStream();
        foreach (var stream in page.GetContentStreams())
        {
            try
            {
                var data = stream.GetDecodedData();
                output.Write(data);
                output.WriteByte((byte)'\n');
            }
            catch (Exception ex) when (ex is InvalidPdfContentException or UnsupportedPdfFeatureException)
            {
            }
        }

        return output.ToArray();
    }
}
