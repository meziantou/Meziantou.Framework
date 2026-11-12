using System.Runtime.InteropServices;

namespace Meziantou.Framework.Fonts;

/// <summary>A part of a line of text that is shaped at once: one script and one bidirectional level, as given by <see cref="TextShaper.SplitRuns"/>.</summary>
/// <param name="Index">The index of the first UTF-16 code unit of the run in the line.</param>
/// <param name="Length">The number of UTF-16 code units of the run.</param>
/// <param name="Script">The script of the run.</param>
/// <param name="BidiLevel">The embedding level of the run, from the Unicode Bidirectional Algorithm: odd for a right-to-left run.</param>
[StructLayout(LayoutKind.Auto)]
public readonly record struct ShapingRun(int Index, int Length, UnicodeScript Script, byte BidiLevel)
{
    /// <summary>Gets a value indicating whether the run is right to left: its level is odd.</summary>
    public bool IsRightToLeft => (BidiLevel & 1) != 0;
}
