namespace Meziantou.Framework.Imaging;

/// <summary>
/// Selects the curve that compresses the highlights of high dynamic range samples into a standard dynamic range
/// (<see cref="HdrToneMappingOptions"/>).
/// </summary>
/// <remarks>
/// Every curve is applied to the largest of the red, green and blue components of a color, in linear display light
/// divided by the target white, and the three components are scaled by the same factor: the hue and the saturation of
/// the color are kept, and its brightest component never exceeds the white. In what follows <c>x</c> is that largest
/// component and <c>p</c> the peak of the source in the same unit (at least 1).
/// </remarks>
public enum HdrToneMappingOperator
{
    /// <summary>
    /// The electrical-electrical transfer function of Report ITU-R BT.2390 (section 5.4.1), evaluated on PQ values:
    /// light up to a knee is kept as it is, and the light above it follows a Hermite spline that reaches the white at
    /// the peak of the source. This is the default.
    /// </summary>
    Bt2390 = 0,

    /// <summary>Keeps the light up to the white and clips what is above: <c>min(x, 1)</c>. Highlights lose their detail.</summary>
    Clip = 1,

    /// <summary>
    /// The operator of Reinhard et al. ("Photographic Tone Reproduction for Digital Images", 2002, equation 4) with the
    /// peak of the source as its white point: <c>x (1 + x / p^2) / (1 + x)</c>. It compresses every tone, the dark ones
    /// the least.
    /// </summary>
    Reinhard = 2,

    /// <summary>
    /// The filmic curve of John Hable ("Filmic Tonemapping Operators", 2010), <c>h(x) / h(p)</c> with
    /// <c>h(x) = (x (A x + C B) + D E) / (x (A x + B) + D F) - E / F</c> and A = 0.15, B = 0.50, C = 0.10, D = 0.20,
    /// E = 0.02, F = 0.30: a toe that darkens the shadows and a long shoulder.
    /// </summary>
    Hable = 3,

    /// <summary>
    /// A straight line up to 0.3, continued by the Möbius function <c>s (x + a) / (x + b)</c> that has the same value
    /// and the same slope there and reaches the white at the peak of the source: the tones below the knee are kept
    /// exactly.
    /// </summary>
    Mobius = 4,
}
