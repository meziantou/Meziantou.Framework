namespace Meziantou.Framework;

/// <summary>Represents a Unicode block (named range of code points).</summary>
public sealed class UnicodeBlock : IEquatable<UnicodeBlock>
{
    /// <summary>Gets the name of the block.</summary>
    public string Name { get; }

    /// <summary>Gets the range of code points in this block.</summary>
    /// <value>
    /// The range of code points in this block. For <see cref="UnicodeBlocks.Unknown"/>, whose code points are the gaps
    /// between the defined blocks, the range is the whole code space (U+0000..U+10FFFF): use <see cref="Contains(int)"/>
    /// to test whether a code point is in the block.
    /// </value>
    public UnicodeRange Range { get; }

    // True for UnicodeBlocks.Unknown, which contains the code points that are in no defined block.
    private readonly bool _isUnknown;

    private UnicodeBlock(string name, UnicodeRange range, bool isUnknown)
    {
        Name = name;
        Range = range;
        _isUnknown = isUnknown;
    }

    internal static UnicodeBlock CreateInternal(string name, UnicodeRange range) => new(name, range, isUnknown: false);

    internal static UnicodeBlock CreateUnknown() => new("Unknown", new UnicodeRange(0, 0x10FFFF), isUnknown: true);

    /// <summary>Determines whether the specified code point is in this block.</summary>
    /// <param name="codePoint">The code point to check.</param>
    /// <returns><see langword="true"/> if the code point is in the block; otherwise, <see langword="false"/>.</returns>
    /// <remarks>
    /// <see cref="UnicodeBlocks.Unknown"/> contains the code points, from U+0000 to U+10FFFF, that are in no defined block.
    /// </remarks>
    public bool Contains(int codePoint) => Range.Contains(codePoint) && (!_isUnknown || ReferenceEquals(UnicodeBlocks.GetBlock(codePoint), this));

    /// <summary>Determines whether the specified rune is in this block.</summary>
    /// <param name="rune">The rune to check.</param>
    /// <returns><see langword="true"/> if the rune is in the block; otherwise, <see langword="false"/>.</returns>
    /// <remarks>
    /// <see cref="UnicodeBlocks.Unknown"/> contains the code points that are in no defined block.
    /// </remarks>
    public bool Contains(Rune rune) => Contains(rune.Value);

    public bool Equals([NotNullWhen(true)] UnicodeBlock? other)
    {
        if (other is null)
            return false;

        if (ReferenceEquals(this, other))
            return true;

        return Name == other.Name && Range == other.Range;
    }

    public override bool Equals([NotNullWhen(true)] object? obj) => obj is UnicodeBlock other && Equals(other);

    public override int GetHashCode() => HashCode.Combine(Name, Range);

    public override string ToString() => $"{Name} ({Range})";
}
