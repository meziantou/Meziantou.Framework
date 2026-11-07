using System;
using Meziantou.Framework.Toml.Syntax;

namespace Meziantou.Framework.Toml.Parsing;

/// <summary>
/// A lightweight token struct to avoid GC allocations.
/// </summary>
internal readonly struct SyntaxTokenValue : IEquatable<SyntaxTokenValue>
{
    /// <summary>
    /// Initializes a new instance of the <see cref="SyntaxTokenValue"/> struct.
    /// </summary>
    /// <param name="kind">The type.</param>
    /// <param name="start">The start.</param>
    /// <param name="end">The end.</param>
    /// <param name="stringValue">Optional string value (e.g. decoded string literal).</param>
    /// <param name="data">Optional scalar payload (e.g. integer/float/boolean encoded as <see cref="ulong"/>).</param>
    /// <exception cref="System.ArgumentOutOfRangeException"></exception>
    public SyntaxTokenValue(TokenKind kind, TextPosition start, TextPosition end, string? stringValue = null, ulong data = 0)
    {
        if (start.Offset > end.Offset) throw new ArgumentOutOfRangeException(nameof(start), $"[{nameof(start)}] index must be <= to [{nameof(end)}]");
        Kind = kind;
        Start = start;
        End = end;
        StringValue = stringValue;
        Data = data;
    }

    /// <summary>
    /// The type of token.
    /// </summary>
#pragma warning disable CA1051 // Public fields are part of the syntax API
    public readonly TokenKind Kind;
#pragma warning restore CA1051

    /// <summary>
    /// The start position of this token.
    /// </summary>
#pragma warning disable CA1051 // Public fields are part of the syntax API
    public readonly TextPosition Start;
#pragma warning restore CA1051

    /// <summary>
    /// The end position of this token.
    /// </summary>
#pragma warning disable CA1051 // Public fields are part of the syntax API
    public readonly TextPosition End;
#pragma warning restore CA1051

    /// <summary>
    /// Optional decoded string value for tokens that carry a string payload.
    /// </summary>
#pragma warning disable CA1051 // Public fields are part of the syntax API
    public readonly string? StringValue;
#pragma warning restore CA1051

    /// <summary>
    /// Optional scalar payload for tokens that carry a non-string value.
    /// </summary>
#pragma warning disable CA1051 // Public fields are part of the syntax API
    public readonly ulong Data;
#pragma warning restore CA1051

    /// <inheritdoc />
    public override string ToString()
    {
        return $"{Kind}({Start}:{End})";
    }

    /// <summary>
    /// Gets the text represented by this token from a source string.
    /// </summary>
    /// <param name="text">The full source text.</param>
    /// <returns>The token text, or <c>null</c> if out of range.</returns>
    public string? GetText(string text)
    {
        if (Kind == TokenKind.Eof)
        {
            return "<eof>";
        }
        return End.Offset < text.Length ? text.Substring(Start.Offset, GetLength(text)) : null;
    }

    internal string? GetText(ReadOnlySpan<char> text)
    {
        if (Kind == TokenKind.Eof)
        {
            return "<eof>";
        }

        var start = Start.Offset;
        var length = GetLength(text);
        if (start < 0 || length < 0)
        {
            return null;
        }

        if ((uint)start > (uint)text.Length || start + length > text.Length)
        {
            return null;
        }

        return text.Slice(start, length).ToString();
    }

    /// <inheritdoc />
    public bool Equals(SyntaxTokenValue other)
    {
        return Kind == other.Kind && Start.Equals(other.Start) && End.Equals(other.End);
    }

    /// <inheritdoc />
    public override bool Equals([NotNullWhen(true)] object? obj)
    {
        if (ReferenceEquals(null, obj)) return false;
        return obj is SyntaxTokenValue && Equals((SyntaxTokenValue) obj);
    }

    /// <inheritdoc />
    public override int GetHashCode()
    {
        unchecked
        {
            var hashCode = (int) Kind;
            hashCode = (hashCode*397) ^ Start.GetHashCode();
            hashCode = (hashCode*397) ^ End.GetHashCode();
            return hashCode;
        }
    }

    /// <summary>
    /// Compares two <see cref="SyntaxTokenValue"/> instances for equality.
    /// </summary>
    /// <param name="left">The left value.</param>
    /// <param name="right">The right value.</param>
    /// <returns><c>true</c> if the values are equal; otherwise <c>false</c>.</returns>
    public static bool operator ==(SyntaxTokenValue left, SyntaxTokenValue right)
    {
        return left.Equals(right);
    }

    /// <summary>
    /// Compares two <see cref="SyntaxTokenValue"/> instances for inequality.
    /// </summary>
    /// <param name="left">The left value.</param>
    /// <param name="right">The right value.</param>
    /// <returns><c>true</c> if the values are not equal; otherwise <c>false</c>.</returns>
    public static bool operator !=(SyntaxTokenValue left, SyntaxTokenValue right)
    {
        return !left.Equals(right);
    }

    // The end is the position of the last character, which is the first code unit of a surrogate pair
    private int GetLength(ReadOnlySpan<char> text)
    {
        var length = End.Offset - Start.Offset + 1;
        var end = End.Offset;
        if ((uint)(end + 1) < (uint)text.Length && char.IsHighSurrogate(text[end]) && char.IsLowSurrogate(text[end + 1]))
        {
            length++;
        }

        return length;
    }
}
