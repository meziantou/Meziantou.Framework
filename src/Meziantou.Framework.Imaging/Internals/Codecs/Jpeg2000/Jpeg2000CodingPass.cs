using System.Runtime.InteropServices;

namespace Meziantou.Framework.Imaging.Internals;

/// <summary>What the block encoder records of a coding pass, for the truncation of its codeword segment by the rate control.</summary>
[StructLayout(LayoutKind.Sequential)]
internal struct Jpeg2000CodingPass
{
    /// <summary>The number of bytes of the codeword segment from which a decoder decodes this pass and the previous ones.</summary>
    public int Length;

    /// <summary>
    /// How much the pass reduces the squared error of the coefficients of the code-block, in squared units of their
    /// quantization step (a coefficient of the reversible path counts for its value); it may be negative.
    /// </summary>
    public double Distortion;
}
