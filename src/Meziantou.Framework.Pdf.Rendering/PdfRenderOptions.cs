using Meziantou.Framework.Fonts;
using Meziantou.Framework.Imaging;

namespace Meziantou.Framework.Pdf.Rendering;

/// <summary>The options of <see cref="PdfRenderer"/>.</summary>
public sealed class PdfRenderOptions
{
    /// <summary>Gets the default options: 72 dots per inch, a white background, the annotations, the system fonts.</summary>
    public static PdfRenderOptions Default { get; } = new();

    /// <summary>Gets or initializes the resolution, in dots per inch: 72 renders one pixel per point.</summary>
    public double Dpi
    {
        get;
        init
        {
            if (!double.IsFinite(value) || value <= 0)
                throw new ArgumentOutOfRangeException(nameof(value), value, "The resolution must be a positive number");

            field = value;
        }
    } = 72;

    /// <summary>Gets or initializes the color the page is painted on before its content; <see langword="null"/> for a transparent background.</summary>
    public Rgba32? Background { get; init; } = new Rgba32(255, 255, 255, 255);

    /// <summary>Gets or initializes whether the appearances of the annotations are drawn (ISO 32000-2, 12.5.5).</summary>
    public bool RenderAnnotations { get; init; } = true;

    /// <summary>
    /// Gets or initializes the font that replaces a font the document does not embed. When it is <see langword="null"/>
    /// or returns <see langword="null"/>, a standard font is replaced by a bundled font of the same widths, another font
    /// by a font of the system, else by a bundled font of its kind.
    /// </summary>
    public Func<PdfFontRequest, FontFace?>? FontResolver { get; init; }

    /// <summary>
    /// Gets or initializes whether the fonts of the system replace the fonts the document does not embed and that are not
    /// standard fonts. When it is <see langword="false"/>, they are replaced by the bundled fonts.
    /// </summary>
    public bool UseSystemFonts { get; init; } = true;

    /// <summary>Gets or initializes the configuration of the image the page is rendered to: its limits and allocation scope.</summary>
    public ImageConfiguration? Configuration { get; init; }

    /// <summary>
    /// Gets or initializes the work a page may do: the operators of its content and of the forms, patterns and glyphs
    /// it paints, a content stream being able to paint the others many times.
    /// </summary>
    public long MaxOperations
    {
        get;
        init
        {
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(value);
            field = value;
        }
    } = 50_000_000;
}
