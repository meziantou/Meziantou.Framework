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
    /// <returns>The image: the crop box of the page at the resolution of the options, rotated by the <c>/Rotate</c> of the page.</returns>
    /// <exception cref="ArgumentException">The page is not a page of <see cref="Document"/>.</exception>
    /// <exception cref="ObjectDisposedException">The session is disposed of.</exception>
    /// <exception cref="PdfResourceLimitException">The page is too large at the resolution, or it does more work than <see cref="PdfRenderOptions.MaxOperations"/>.</exception>
    public Image<Rgba32> RenderPage(PdfPage page)
    {
        ArgumentNullException.ThrowIfNull(page);
        ObjectDisposedException.ThrowIf(_isDisposed, this);
        if (!ReferenceEquals(page.Document, Document))
            throw new ArgumentException("The page is not a page of the document of the session", nameof(page));

        var options = Options;
        var (width, height, matrix) = PageGeometry.Compute(page, options.Dpi);
        var image = new Image<Rgba32>(width, height, options.Configuration);
        _cache.BeginPage();
        try
        {
            image.Draw(canvas =>
            {
                var optionalContent = new OptionalContent(page.Document);

                // A knockout page group (11.4.7): the objects of the page are elements composited with the paper
                var pageGroup = page.Dictionary.Get<PdfDictionary>(PdfNames.Group);
                var isKnockout = pageGroup?.GetName(PdfNames.S)?.Value == "Transparency" && pageGroup.Get<PdfBoolean>(PdfNames.K)?.Value is true;
                void Draw(DrawingCanvas target)
                {
                    var renderer = new ContentRenderer(_cache, page.Document, new RenderGraphicsState { Ctm = matrix }, depth: 0, optionalContent)
                    {
                        BaseMatrix = matrix,
                        DeviceSize = (width, height),
                        IsKnockoutContext = isKnockout,
                    };
                    renderer.Run(target, GetContent(page), page.Resources);
                }

                if (isKnockout)
                {
                    canvas.Group(new GroupOptions { IsKnockout = true }, content => Draw(content));
                }
                else
                {
                    Draw(canvas);
                }

                if (options.RenderAnnotations)
                {
                    AnnotationRenderer.Render(page, canvas, _cache, matrix, (width, height), optionalContent);
                }

                // The page is a group composited on the paper (11.4.7): the content is drawn on a transparent backdrop,
                // which the blend modes see, then the background is put behind it
                if (options.Background is { } background)
                {
                    canvas.Fill(new SolidBrush(background.ToRgba64()) { CompositeMode = CompositeMode.DestinationOver });
                }
            });
        }
        catch
        {
            image.Dispose();
            throw;
        }
        finally
        {
            _cache.EndPage();
        }

        return image;
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
