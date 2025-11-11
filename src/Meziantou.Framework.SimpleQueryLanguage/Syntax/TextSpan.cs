using System.Runtime.InteropServices;

namespace Meziantou.Framework.SimpleQueryLanguage.Syntax;

/// <summary>Represents a span of text in the source query string.</summary>
[StructLayout(LayoutKind.Auto)]
public readonly struct TextSpan
{
    /// <summary>Initializes a new instance of the <see cref="TextSpan"/> struct.</summary>
    /// <param name="start">The starting position of the span.</param>
    /// <param name="length">The length of the span.</param>
    public TextSpan(int start, int length)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(start);
        ArgumentOutOfRangeException.ThrowIfNegative(length);

        Start = start;
        Length = length;
    }

    /// <summary>Creates a text span from start and end positions.</summary>
    /// <param name="start">The starting position of the span.</param>
    /// <param name="end">The ending position of the span (exclusive).</param>
    /// <returns>A text span covering the specified range.</returns>
    public static TextSpan FromBounds(int start, int end)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(start);
        ArgumentOutOfRangeException.ThrowIfLessThan(end, start);

        var length = end - start;
        return new TextSpan(start, length);
    }

    /// <summary>Gets the starting position of the span.</summary>
    public int Start { get; }

    /// <summary>Gets the ending position of the span (exclusive).</summary>
    public int End => Start + Length;

    /// <summary>Gets the length of the span.</summary>
    public int Length { get; }

    public override string ToString() => $"[{Start},{End})";
}
