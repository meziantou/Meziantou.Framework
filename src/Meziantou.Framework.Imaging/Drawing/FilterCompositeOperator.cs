namespace Meziantou.Framework.Imaging.Drawing;

/// <summary>How a <see cref="CompositePrimitive"/> combines its two inputs: the <c>operator</c> of <c>feComposite</c> of SVG.</summary>
/// <remarks>With <c>A</c> the first input and <c>B</c> the second one, colors multiplied by their alpha <c>Aa</c> and <c>Ba</c>.</remarks>
public enum FilterCompositeOperator
{
    /// <summary><c>A</c> over <c>B</c>: <c>A + B (1 - Aa)</c>.</summary>
    Over = 0,

    /// <summary>The part of <c>A</c> inside <c>B</c>: <c>A Ba</c>.</summary>
    In = 1,

    /// <summary>The part of <c>A</c> outside <c>B</c>: <c>A (1 - Ba)</c>.</summary>
    Out = 2,

    /// <summary><c>A</c> inside <c>B</c>, over <c>B</c>: <c>A Ba + B (1 - Aa)</c>.</summary>
    Atop = 3,

    /// <summary>What is in one input only: <c>A (1 - Ba) + B (1 - Aa)</c>.</summary>
    Xor = 4,

    /// <summary>The sum of the inputs, up to 1: <c>min(A + B, 1)</c>.</summary>
    Lighter = 5,

    /// <summary>
    /// <c>k1 A B + k2 A + k3 B + k4</c> for each component, clamped to the range from 0 to 1, and each color then
    /// limited to the alpha (<see cref="CompositePrimitive.K1"/> to <see cref="CompositePrimitive.K4"/>).
    /// </summary>
    Arithmetic = 6,
}
