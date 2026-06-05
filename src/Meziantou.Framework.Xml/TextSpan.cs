using System.Runtime.InteropServices;

namespace Meziantou.Framework.Xml;

/// <summary>Represents a contiguous character range in text.</summary>
/// <example>
/// <code>
/// var span = new TextSpan(start: 5, length: 3);
/// var sameSpan = TextSpan.FromBounds(5, 8);
/// </code>
/// </example>
[StructLayout(LayoutKind.Auto)]
public readonly struct TextSpan
{
    public TextSpan(int start, int length)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(start);
        ArgumentOutOfRangeException.ThrowIfNegative(length);

        Start = start;
        Length = length;
    }

    public int Start { get; }
    public int Length { get; }
    public int End => Start + Length;

    public static TextSpan FromBounds(int start, int end)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(start);
        ArgumentOutOfRangeException.ThrowIfLessThan(end, start);
        return new TextSpan(start, end - start);
    }

    public override string ToString() => $"[{Start}..{End})";
}
