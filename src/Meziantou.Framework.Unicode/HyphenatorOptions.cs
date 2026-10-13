namespace Meziantou.Framework;

/// <summary>Specifies how a <see cref="Hyphenator"/> is loaded and where it allows a word to break.</summary>
/// <remarks>The limits protect the caller from pattern files that come from an untrusted source: a file that exceeds one is rejected before memory is allocated for it.</remarks>
public sealed class HyphenatorOptions
{
    /// <summary>Gets the default options.</summary>
    public static HyphenatorOptions Default { get; } = new();

    /// <summary>Gets the smallest number of characters before the first break of a word. The default is 2.</summary>
    /// <remarks>The value depends on the language; it is the <c>\lefthyphenmin</c> of TeX. A character is an extended grapheme cluster.</remarks>
    /// <exception cref="ArgumentOutOfRangeException">The value is less than 1.</exception>
    public int LeftHyphenMin
    {
        get;
        init
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(value, 1);
            field = value;
        }
    } = 2;

    /// <summary>Gets the smallest number of characters after the last break of a word. The default is 3.</summary>
    /// <remarks>The value depends on the language; it is the <c>\righthyphenmin</c> of TeX. A character is an extended grapheme cluster.</remarks>
    /// <exception cref="ArgumentOutOfRangeException">The value is less than 1.</exception>
    public int RightHyphenMin
    {
        get;
        init
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(value, 1);
            field = value;
        }
    } = 3;

    /// <summary>Gets the greatest number of patterns, and of exceptions, a hyphenator accepts. The default is 250,000.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is less than 1.</exception>
    public int MaxPatternCount
    {
        get;
        init
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(value, 1);
            field = value;
        }
    } = 250_000;

    /// <summary>Gets the greatest number of letters of a pattern or of an exception. The default is 64.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is less than 1 or greater than 255.</exception>
    public int MaxPatternLength
    {
        get;
        init
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(value, 1);
            ArgumentOutOfRangeException.ThrowIfGreaterThan(value, 255);
            field = value;
        }
    } = 64;

    /// <summary>Gets the greatest length of the patterns, and of the exceptions, in UTF-16 code units or in UTF-8 bytes. The default is 8 MiB.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is less than 1.</exception>
    public long MaxInputLength
    {
        get;
        init
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(value, 1);
            field = value;
        }
    } = 8 * 1024 * 1024;

    /// <summary>Gets the greatest length, in UTF-16 code units, of a word that is hyphenated: a longer word has no break. The default is 255.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is less than 1 or greater than 4096.</exception>
    public int MaxWordLength
    {
        get;
        init
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(value, 1);
            ArgumentOutOfRangeException.ThrowIfGreaterThan(value, 4096);
            field = value;
        }
    } = 255;
}
