using System.Runtime.InteropServices;

namespace Meziantou.Framework.Language.Json;

/// <summary>Represents a contiguous character range in text.</summary>
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
