namespace Meziantou.Framework.Imaging.Drawing;

/// <summary>How a paint is combined with the pixels it is drawn on: the compositing operators of Porter and Duff.</summary>
/// <remarks>
/// <para>
/// An operator gives the result where the shape covers a pixel entirely, from the paint (the source) and the pixel (the
/// destination). With the alphas <c>as</c> and <c>ad</c> as fractions of one, each operator has two factors <c>Fa</c> and
/// <c>Fb</c>: the result has the alpha <c>Fa as + Fb ad</c>, and its color multiplied by its alpha is
/// <c>Fa as source + Fb ad destination</c>. A pixel that a shape covers in part is, for that part, the result of the
/// operator, and for the rest what it was: a pixel the shape does not touch is never changed, whatever the operator.
/// </para>
/// <para>
/// The arithmetic is exact, on the stored sample values, and rounded to nearest with ties upward, as for
/// <see cref="SourceOver"/>. A pixel whose alpha becomes zero becomes transparent black; a pixel that was fully
/// transparent and stays so is not written. A pixel format without alpha is opaque before and after: its pixels get
/// the color the result would have over black (the color multiplied by the alpha the result would have).
/// </para>
/// </remarks>
public enum CompositeMode
{
    /// <summary>The paint is drawn over the pixel (<c>Fa = 1</c>, <c>Fb = 1 - as</c>). This is the default.</summary>
    SourceOver = 0,

    /// <summary>The paint replaces the pixel, alpha included (<c>Fa = 1</c>, <c>Fb = 0</c>): a transparent paint erases.</summary>
    Source = 1,

    /// <summary>The paint is drawn behind the pixel: it shows where the pixel is transparent (<c>Fa = 1 - ad</c>, <c>Fb = 1</c>).</summary>
    DestinationOver = 2,

    /// <summary>The paint replaces the pixel, with its alpha multiplied by the alpha of the pixel (<c>Fa = ad</c>, <c>Fb = 0</c>).</summary>
    SourceIn = 3,

    /// <summary>The pixel is kept where the paint is opaque: its alpha is multiplied by the alpha of the paint (<c>Fa = 0</c>, <c>Fb = as</c>). The color of the paint is not used.</summary>
    DestinationIn = 4,

    /// <summary>The paint replaces the pixel, and only shows where the pixel was transparent (<c>Fa = 1 - ad</c>, <c>Fb = 0</c>).</summary>
    SourceOut = 5,

    /// <summary>The pixel is erased where the paint is opaque: its alpha is multiplied by one minus the alpha of the paint (<c>Fa = 0</c>, <c>Fb = 1 - as</c>). The color of the paint is not used.</summary>
    DestinationOut = 6,

    /// <summary>The paint is drawn over the pixel, which keeps its alpha (<c>Fa = ad</c>, <c>Fb = 1 - as</c>).</summary>
    SourceAtop = 7,

    /// <summary>The pixel is drawn over the paint, and the result has the alpha of the paint (<c>Fa = 1 - ad</c>, <c>Fb = as</c>).</summary>
    DestinationAtop = 8,

    /// <summary>The paint and the pixel each show where the other one is transparent (<c>Fa = 1 - ad</c>, <c>Fb = 1 - as</c>).</summary>
    Xor = 9,

    /// <summary>The pixel is erased (<c>Fa = 0</c>, <c>Fb = 0</c>). Neither the color nor the alpha of the paint is used: only the shape counts.</summary>
    Clear = 10,

    /// <summary>
    /// The paint is added to the pixel (<c>Fa = 1</c>, <c>Fb = 1</c>), and the result is clipped to one: its alpha is
    /// <c>min(as + ad, 1)</c>, and its color multiplied by its alpha <c>min(as source + ad destination, 1)</c>, for each
    /// color. Light added to light: <c>plus-lighter</c> of CSS, <c>lighter</c> of the HTML canvas, <c>lighter</c> of
    /// <c>feComposite</c>.
    /// </summary>
    Plus = 11,
}
