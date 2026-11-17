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
    /// Gets or initializes the purpose of the rendering: it selects the annotations drawn (the Print, NoView and Hidden
    /// flags) and the optional content that the usage of its groups turns on or off.
    /// </summary>
    public PdfRenderIntent Intent
    {
        get;
        init
        {
            if (!Enum.IsDefined(value))
                throw new ArgumentOutOfRangeException(nameof(value), value, "The intent is not defined");

            field = value;
        }
    }

    /// <summary>
    /// Gets or initializes the name (<c>/Name</c>) of the optional content configuration of the document that sets which
    /// layers are visible (ISO 32000-2, 8.11.4.3); <see langword="null"/>, or a name the document does not have, for the
    /// default configuration.
    /// </summary>
    public string? OptionalContentConfiguration { get; init; }

    /// <summary>
    /// Gets or initializes the visibility of optional content groups (layers) that replaces the one of the configuration:
    /// the keys are the names of the groups (<c>/Name</c>), and every group of a name is turned on or off.
    /// </summary>
    public IReadOnlyDictionary<string, bool>? OptionalContentGroupStates { get; init; }

    /// <summary>
    /// Gets or initializes whether the halftones of the document (<c>/HT</c> and <c>/HTO</c> of the graphics states, ISO
    /// 32000-2, 10.6) are applied, as a printer that turns each color component fully on or off would apply them: the
    /// red, green and blue components of the colors an opaque object paints are compared with the thresholds of the
    /// screen of the halftone, in the pixels of the image. The default halftone leaves the colors as they are. When
    /// <see langword="false"/> (the default), the halftones are ignored, as viewers do for a screen.
    /// </summary>
    /// <remarks>
    /// The screens are in the pixels of the image, so the result depends on the resolution. Turn
    /// <see cref="PathAntialiasing"/> and <see cref="TextAntialiasing"/> off to get only fully on or off components.
    /// </remarks>
    public bool UseHalftones { get; init; }

    /// <summary>
    /// Gets or initializes whether overprinting is simulated (ISO 32000-2, 8.6.7), as a print preview shows it: the page
    /// is separated into the plates of its colorants (<see cref="Plate"/>), and the inks of the plates are combined on
    /// white paper. The process inks are cyan, magenta, yellow and black; a spot ink has the color of its alternate color
    /// space. The default is <see langword="false"/>: the page is composited in RGB, and overprinting is ignored, as viewers
    /// do for a screen.
    /// </summary>
    /// <remarks>
    /// The page is rendered once per colorant, and the background of the options is not painted. The colors of the
    /// images and shadings are separated into the process colorants from their RGB colors: their spot colorants are not
    /// separated. <see cref="PdfSeparations.GetColorants(PdfPage)"/> lists the colorants of a page.
    /// </remarks>
    public bool SimulateOverprint { get; init; }

    /// <summary>
    /// Gets or initializes the colorant whose plate is rendered, or <see langword="null"/> (the default) for the colors of
    /// the page: <c>Cyan</c>, <c>Magenta</c>, <c>Yellow</c>, <c>Black</c>, or the name of a spot colorant of a separation or
    /// DeviceN color space. The plate is rendered in gray, as a film: black for full ink, white for none.
    /// </summary>
    /// <remarks>
    /// Each object paints the tint of the colorant in its color, and erases the plate where its color does not have the
    /// colorant, unless it overprints (<c>OP</c>, <c>op</c> and the overprint mode <c>OPM</c>, 8.6.7), which leaves the
    /// plate unchanged. Gray is separated as black, RGB with a full gray component replacement. The background of the
    /// options is not painted.
    /// </remarks>
    public string? Plate { get; init; }

    /// <summary>Gets or initializes whether the edges of the paths, the images and the shadings are antialiased; <see langword="false"/> draws them with hard edges.</summary>
    public bool PathAntialiasing { get; init; } = true;

    /// <summary>Gets or initializes whether the edges of the glyphs of the text are antialiased; <see langword="false"/> draws them with hard edges.</summary>
    public bool TextAntialiasing { get; init; } = true;

    /// <summary>
    /// Gets or initializes the region of the page rendered, in the default user space of the page (the coordinates of its
    /// boxes); <see langword="null"/> for the crop box. The region is intersected with the media box, and the image is
    /// turned by the <c>/Rotate</c> of the page as the whole page would be.
    /// </summary>
    public PdfRectangle? Region { get; init; }

    /// <summary>
    /// Gets or initializes the width of the image, in pixels: the resolution is the one that gives the region (the crop
    /// box by default, turned by the rotation of the page) this width, instead of <see cref="Dpi"/>. With
    /// <see cref="Height"/>, the region is fitted inside both, keeping its proportions.
    /// </summary>
    public int? Width
    {
        get;
        init
        {
            if (value is <= 0)
                throw new ArgumentOutOfRangeException(nameof(value), value, "The width must be a positive number");

            field = value;
        }
    }

    /// <summary>
    /// Gets or initializes the height of the image, in pixels: the resolution is the one that gives the region (the crop
    /// box by default, turned by the rotation of the page) this height, instead of <see cref="Dpi"/>. With
    /// <see cref="Width"/>, the region is fitted inside both, keeping its proportions.
    /// </summary>
    public int? Height
    {
        get;
        init
        {
            if (value is <= 0)
                throw new ArgumentOutOfRangeException(nameof(value), value, "The height must be a positive number");

            field = value;
        }
    }

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
