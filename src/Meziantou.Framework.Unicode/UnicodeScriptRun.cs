using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;

namespace Meziantou.Framework;

/// <summary>Represents a range of a text that is written in one script.</summary>
[StructLayout(LayoutKind.Auto)]
public readonly struct UnicodeScriptRun : IEquatable<UnicodeScriptRun>
{
    internal UnicodeScriptRun(int index, int length, UnicodeScript script)
    {
        Index = index;
        Length = length;
        Script = script;
    }

    /// <summary>Gets the index of the first UTF-16 code unit of the run.</summary>
    public int Index { get; }

    /// <summary>Gets the number of UTF-16 code units of the run.</summary>
    public int Length { get; }

    /// <summary>Gets the script of the run. It is <see cref="UnicodeScript.Common"/> when no character of the run belongs to a script.</summary>
    public UnicodeScript Script { get; }

    /// <inheritdoc />
    public bool Equals(UnicodeScriptRun other) => Index == other.Index && Length == other.Length && Script == other.Script;

    /// <inheritdoc />
    public override bool Equals([NotNullWhen(true)] object? obj) => obj is UnicodeScriptRun other && Equals(other);

    /// <inheritdoc />
    public override int GetHashCode() => HashCode.Combine(Index, Length, Script);

    /// <summary>Determines whether two values are equal.</summary>
    /// <param name="left">The first value.</param>
    /// <param name="right">The second value.</param>
    /// <returns><see langword="true"/> when the values are equal.</returns>
    public static bool operator ==(UnicodeScriptRun left, UnicodeScriptRun right) => left.Equals(right);

    /// <summary>Determines whether two values are different.</summary>
    /// <param name="left">The first value.</param>
    /// <param name="right">The second value.</param>
    /// <returns><see langword="true"/> when the values are different.</returns>
    public static bool operator !=(UnicodeScriptRun left, UnicodeScriptRun right) => !left.Equals(right);
}
