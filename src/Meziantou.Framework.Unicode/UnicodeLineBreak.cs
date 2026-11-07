using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;

namespace Meziantou.Framework;

/// <summary>Represents a position where a line can or must break.</summary>
[StructLayout(LayoutKind.Auto)]
public readonly struct UnicodeLineBreak : IEquatable<UnicodeLineBreak>
{
    internal UnicodeLineBreak(int index, bool isMandatory)
    {
        Index = index;
        IsMandatory = isMandatory;
    }

    /// <summary>Gets the index of the first UTF-16 code unit of the next line: the line breaks before it.</summary>
    public int Index { get; }

    /// <summary>Gets a value indicating whether the line must break: after a line feed, a paragraph separator and the like, and at the end of the text.</summary>
    public bool IsMandatory { get; }

    /// <inheritdoc />
    public bool Equals(UnicodeLineBreak other) => Index == other.Index && IsMandatory == other.IsMandatory;

    /// <inheritdoc />
    public override bool Equals([NotNullWhen(true)] object? obj) => obj is UnicodeLineBreak other && Equals(other);

    /// <inheritdoc />
    public override int GetHashCode() => HashCode.Combine(Index, IsMandatory);

    /// <summary>Determines whether two values are equal.</summary>
    /// <param name="left">The first value.</param>
    /// <param name="right">The second value.</param>
    /// <returns><see langword="true"/> when the values are equal.</returns>
    public static bool operator ==(UnicodeLineBreak left, UnicodeLineBreak right) => left.Equals(right);

    /// <summary>Determines whether two values are different.</summary>
    /// <param name="left">The first value.</param>
    /// <param name="right">The second value.</param>
    /// <returns><see langword="true"/> when the values are different.</returns>
    public static bool operator !=(UnicodeLineBreak left, UnicodeLineBreak right) => !left.Equals(right);
}
