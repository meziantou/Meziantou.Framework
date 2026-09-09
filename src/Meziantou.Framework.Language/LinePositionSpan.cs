using System.Runtime.InteropServices;

namespace Meziantou.Framework.Language;

/// <summary>Represents a range of text between two <see cref="LinePosition"/> values.</summary>
[StructLayout(LayoutKind.Auto)]
public readonly record struct LinePositionSpan(LinePosition Start, LinePosition End)
{
    public override string ToString() => $"({Start})-({End})";
}
