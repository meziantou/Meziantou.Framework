using System.Runtime.InteropServices;

namespace Meziantou.Framework.SimpleQueryLanguage.Syntax;

[StructLayout(LayoutKind.Auto)]
public readonly struct TextSpan
{
    public TextSpan(int start, int length)
    {
        if (start < 0)
            throw new ArgumentOutOfRangeException(nameof(start));

        if (length < 0)
            throw new ArgumentOutOfRangeException(nameof(length));

        Start = start;
        Length = length;
    }

    public static TextSpan FromBounds(int start, int end)
    {
        if (start < 0)
            throw new ArgumentOutOfRangeException(nameof(start));

        if (end < start)
            throw new ArgumentOutOfRangeException(nameof(end));

        var length = end - start;
        return new TextSpan(start, length);
    }

    public int Start { get; }
    public int End => Start + Length;
    public int Length { get; }

    public override string ToString() => $"[{Start},{End})";
}
