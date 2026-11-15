using Meziantou.Framework.VectorGraphics;

namespace Meziantou.Framework.Barcodes;

/// <summary>
/// A logo drawn over the center of a QR code (<see cref="QRCodeRenderOptions.Logo"/>).
/// </summary>
/// <remarks>
/// <para>
/// The image keeps its proportions and is centered in a square of <see cref="SizePercent"/> of the side of the drawing.
/// By default, the modules under the logo are not drawn (<see cref="ClearModules"/>): the logo sits on a clean area,
/// snapped to whole modules, instead of over half-visible modules.
/// </para>
/// <para>
/// The scanner recovers the missing modules with the error correction of the QR code, so create the QR code with
/// <see cref="ErrorCorrectionLevel.H"/> and keep the logo small. When the modules are cleared, rendering checks that the
/// error correction can recover them, and throws an <see cref="ArgumentOutOfRangeException"/> when it cannot.
/// </para>
/// </remarks>
public sealed class BarcodeLogo
{
    /// <summary>Initializes a new instance of the <see cref="BarcodeLogo"/> class.</summary>
    /// <param name="data">The bytes of a PNG, JPEG, GIF or WebP file.</param>
    public BarcodeLogo(ReadOnlyMemory<byte> data)
    {
        Data = data;
    }

    /// <summary>Gets the bytes of the image: a PNG, JPEG, GIF or WebP file.</summary>
    /// <remarks>
    /// SVG output embeds the bytes as a data URI, and image and PDF output decode them, so every output draws the same
    /// logo. Rendering throws an <see cref="ArgumentException"/> when the data is not one of these formats.
    /// </remarks>
    public ReadOnlyMemory<byte> Data { get; }

    /// <summary>Gets or sets the size of the logo, as a percentage of the smallest side of the drawing, quiet zone included. Default is 20.</summary>
    /// <remarks>Valid values are between 1 and 100.</remarks>
    public int SizePercent { get; set; } = 20;

    /// <summary>Gets or sets a value indicating whether the modules under the logo are not drawn. Default is <see langword="true"/>.</summary>
    /// <remarks>
    /// <para>
    /// The cleared area is the smallest rectangle of whole modules that contains the logo, grown by <see cref="MarginModules"/>
    /// on each side. A logo that is not square covers a band of its square, and only that band is cleared; an image with
    /// Exif metadata clears its whole square, as viewers that apply its orientation can turn it. Its modules are not drawn, so the light paint of the QR code shows under the logo, or
    /// <see cref="BackdropPaint"/> when it is set.
    /// </para>
    /// <para>
    /// Rendering throws an <see cref="ArgumentOutOfRangeException"/> when the area covers a finder pattern, a timing pattern,
    /// the format information or the version information, which the scanner needs to locate and read the code, or when an
    /// error correction block of the QR code has more codewords in the area than its error correction level can recover.
    /// The alignment patterns in the area are cleared, like the data. Set <see langword="false"/> to draw the modules under the
    /// logo, without this check.
    /// </para>
    /// </remarks>
    public bool ClearModules { get; set; } = true;

    /// <summary>Gets or sets the number of modules cleared around the logo, on each side. Default is 0.</summary>
    /// <remarks>
    /// The cleared area already ends on module boundaries, so there is up to a module of space between the logo and the
    /// nearest dark module. A margin of 1 separates them more clearly, but clears more modules. Ignored when
    /// <see cref="ClearModules"/> is <see langword="false"/>. Valid values are 0 or more.
    /// </remarks>
    public int MarginModules { get; set; }

    /// <summary>Gets or sets the paint of the cleared area, drawn under the logo. Default is <see langword="null"/>: the light paint of the QR code shows.</summary>
    /// <remarks>
    /// Set a backdrop when the QR code has no light paint (a transparent background), or to draw the logo on a color of its
    /// own. Keep it light: a dark backdrop reads as dark modules. Ignored when <see cref="ClearModules"/> is <see langword="false"/>.
    /// </remarks>
    public ScenePaint? BackdropPaint { get; set; }

    /// <summary>Gets or sets the radius of the corners of the backdrop, in modules. Default is 0: square corners.</summary>
    /// <remarks>
    /// The radius is at most half the smallest side of the backdrop. Only the backdrop is rounded: the cleared area stays a
    /// rectangle of whole modules. Valid values are 0 or more.
    /// </remarks>
    public double BackdropCornerRadius { get; set; }
}
