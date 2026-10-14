using System.Runtime.InteropServices;

namespace Meziantou.Framework.Imaging.Internals;

/// <summary>A codeword segment of a code-block: the bytes of consecutive coding passes, between two terminations of the coder (T.800, D.4).</summary>
/// <param name="Offset">The offset of the first byte.</param>
/// <param name="Length">The number of bytes.</param>
/// <param name="PassCount">The number of coding passes.</param>
[StructLayout(LayoutKind.Auto)]
internal readonly record struct Jpeg2000CodeBlockSegment(int Offset, int Length, int PassCount)
{
    /// <summary>
    /// The first coding pass that bypasses the arithmetic coder when the code-block style has the selective bypass: the
    /// significance propagation pass of the fifth bit-plane (D.6). The ten passes before it are the cleanup pass of the
    /// first bit-plane and the three passes of the next three.
    /// </summary>
    public const int FirstBypassPass = 10;

    /// <summary>Determines whether the coder is terminated after a coding pass (Table D.9), which ends a codeword segment.</summary>
    /// <param name="pass">The index of the coding pass in its code-block, from 0.</param>
    /// <param name="style">The style of the coding passes (<see cref="Jpeg2000CodingStyle.CodeBlockStyle"/>).</param>
    public static bool IsTerminatedAfter(int pass, int style)
    {
        if ((style & Jpeg2000CodingStyle.StyleTerminateAll) != 0)
            return true;

        if ((style & Jpeg2000CodingStyle.StyleBypass) == 0 || pass < FirstBypassPass - 1)
            return false;

        // With the bypass: the last arithmetic-coded pass before the raw passes, then each run of raw passes (a
        // significance propagation pass and a magnitude refinement pass) and each cleanup pass
        return pass == FirstBypassPass - 1 || (pass - FirstBypassPass) % 3 != 0;
    }
}
