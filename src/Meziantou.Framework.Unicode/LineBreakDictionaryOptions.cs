namespace Meziantou.Framework;

/// <summary>Specifies the limits a <see cref="LineBreakDictionary"/> applies to the word list it is loaded from.</summary>
/// <remarks>The limits protect the caller from word lists that come from an untrusted source: a list that exceeds one is rejected before memory is allocated for it.</remarks>
public sealed class LineBreakDictionaryOptions
{
    /// <summary>Gets the default options.</summary>
    public static LineBreakDictionaryOptions Default { get; } = new();

    /// <summary>Gets the greatest number of words a dictionary accepts. The default is 500,000.</summary>
    /// <remarks>Every line that has a word counts, including a word that is in the list more than once.</remarks>
    /// <exception cref="ArgumentOutOfRangeException">The value is less than 1.</exception>
    public int MaxWordCount
    {
        get;
        init
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(value, 1);
            field = value;
        }
    } = 500_000;

    /// <summary>Gets the greatest length of a word, in UTF-16 code units. The default is 128.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is less than 1 or greater than 255.</exception>
    public int MaxWordLength
    {
        get;
        init
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(value, 1);
            ArgumentOutOfRangeException.ThrowIfGreaterThan(value, 255);
            field = value;
        }
    } = 128;

    /// <summary>Gets the greatest length of the word list, in UTF-16 code units or in UTF-8 bytes. The default is 8 MiB.</summary>
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
}
