using System.Runtime.InteropServices;

namespace Meziantou.Framework.Language;

/// <summary>Represents a zero-based line number and the zero-based character offset within that line.</summary>
[StructLayout(LayoutKind.Auto)]
public readonly record struct LinePosition(int Line, int Character)
{
    public override string ToString() => $"{Line},{Character}";
}
