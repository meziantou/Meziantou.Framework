using System.Runtime.InteropServices;

namespace Meziantou.Framework.SimpleQueryLanguage.Syntax;

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

    public static TextSpan FromBounds(int start, int end)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(start);
        ArgumentOutOfRangeException.ThrowIfLessThan(end, start);

        var length = end - start;
        return new TextSpan(start, length);
    }

    public int Start { get; }
    public int End => Start + Length;
    public int Length { get; }

    public override string ToString() => $"[{Start},{End})";
}
