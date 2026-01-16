namespace Meziantou.Framework;

/// <summary>Represents a Unicode block (named range of code points).</summary>
public sealed class UnicodeBlock : IEquatable<UnicodeBlock>
{
    /// <summary>Gets the name of the block.</summary>
    public string Name { get; }

    /// <summary>Gets the range of code points in this block.</summary>
    public UnicodeRange Range { get; }

    private UnicodeBlock(string name, UnicodeRange range)
    {
        Name = name;
        Range = range;
    }

    internal static UnicodeBlock CreateInternal(string name, UnicodeRange range) => new(name, range);

    /// <summary>Determines whether the specified code point is in this block.</summary>
    /// <param name="codePoint">The code point to check.</param>
    /// <returns><see langword="true"/> if the code point is in the block; otherwise, <see langword="false"/>.</returns>
    public bool Contains(int codePoint) => Range.Contains(codePoint);

    /// <summary>Determines whether the specified rune is in this block.</summary>
    /// <param name="rune">The rune to check.</param>
    /// <returns><see langword="true"/> if the rune is in the block; otherwise, <see langword="false"/>.</returns>
    public bool Contains(Rune rune) => Range.Contains(rune);

    public bool Equals(UnicodeBlock? other)
    {
        if (other is null)
            return false;

        if (ReferenceEquals(this, other))
            return true;

        return Name == other.Name && Range == other.Range;
    }

    public override bool Equals(object? obj) => obj is UnicodeBlock other && Equals(other);

    public override int GetHashCode() => HashCode.Combine(Name, Range);

    public override string ToString() => $"{Name} ({Range})";
}
