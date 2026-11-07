namespace Meziantou.Framework;

/// <summary>
/// A dictionary break engine of ICU: it finds the words of a run of characters of a script that is written without spaces
/// between words, with a word list of ICU (<see cref="IcuWordDictionary"/>).
/// </summary>
/// <remarks>
/// <para>
/// The engines are ports of the ones of ICU (<c>icu4c/source/common/dictbe.cpp</c>), which the word break iterator and the
/// line break iterator of ICU use, and so the browsers and <c>Intl.Segmenter</c> of JavaScript:
/// </para>
/// <list type="bullet">
/// <item><description><see cref="ChineseJapanese"/> (<c>CjkBreakEngine</c>, without the phrase breaking): the segmentation of the run whose words have the smallest total cost. The run is normalized to NFKC; a character that does not start a word of one character is a word of the highest cost, and a run of katakana is a candidate word with a cost that depends on its length.</description></item>
/// <item><description><see cref="Thai"/>, <see cref="Lao"/>, <see cref="Khmer"/> and <see cref="Burmese"/> (<c>ThaiBreakEngine</c>, <c>LaoBreakEngine</c>, <c>KhmerBreakEngine</c> and <c>BurmeseBreakEngine</c>): the longest words of the list that are followed by other words of the list, with up to three words of lookahead. The characters that start no word are attached to the word before them, up to a plausible word start; a break is never before a combining mark, and Thai attaches the elision and repetition characters to the word before them.</description></item>
/// </list>
/// <para>
/// The engines do not find the runs: a caller applies the rules of its segmentation (the word or line breaking rules of
/// Unicode, of ICU, or of CSS) and gives each run of the characters an engine handles (<see cref="Handles"/>) to that engine.
/// The text is read by code point, an unpaired surrogate being a code point, as ICU reads it. The engines are stateless and
/// can be used by several threads at the same time; the word list of an engine is loaded on its first use.
/// </para>
/// </remarks>
public abstract class IcuDictionaryBreakEngine
{
    private protected IcuDictionaryBreakEngine(IcuWordDictionaryKind dictionaryKind)
    {
        DictionaryKind = dictionaryKind;
    }

    /// <summary>Gets the engine of Chinese and Japanese: the characters of the Han, Hiragana and Katakana scripts, and U+30FC, U+FF70, U+FF9E and U+FF9F.</summary>
    public static IcuDictionaryBreakEngine ChineseJapanese => IcuCjkBreakEngine.Instance;

    /// <summary>Gets the engine of Thai: the characters of the Thai script whose line breaking class is Complex_Context (SA).</summary>
    public static IcuDictionaryBreakEngine Thai => IcuComplexContextBreakEngine.ThaiEngine;

    /// <summary>Gets the engine of Lao: the characters of the Lao script whose line breaking class is Complex_Context (SA).</summary>
    public static IcuDictionaryBreakEngine Lao => IcuComplexContextBreakEngine.LaoEngine;

    /// <summary>Gets the engine of Khmer: the characters of the Khmer script whose line breaking class is Complex_Context (SA).</summary>
    public static IcuDictionaryBreakEngine Khmer => IcuComplexContextBreakEngine.KhmerEngine;

    /// <summary>Gets the engine of Burmese: the characters of the Myanmar script whose line breaking class is Complex_Context (SA).</summary>
    public static IcuDictionaryBreakEngine Burmese => IcuComplexContextBreakEngine.BurmeseEngine;

    /// <summary>Gets the word list the engine uses.</summary>
    public IcuWordDictionaryKind DictionaryKind { get; }

    /// <summary>Gets the engine that handles a code point.</summary>
    /// <param name="codePoint">The code point.</param>
    /// <returns>The engine whose <see cref="Handles"/> method returns <see langword="true"/> for the code point, or <see langword="null"/> when no engine handles it (the sets of characters of the engines do not overlap).</returns>
    public static IcuDictionaryBreakEngine? GetEngine(int codePoint)
    {
        if ((uint)codePoint > 0x10FFFF)
            return null;

        if (UnicodeProperties.GetLineBreakClass(codePoint) == UnicodeLineBreakClass.ComplexContext)
        {
            return UnicodeScripts.GetScript(codePoint) switch
            {
                UnicodeScript.Thai => Thai,
                UnicodeScript.Lao => Lao,
                UnicodeScript.Khmer => Khmer,
                UnicodeScript.Myanmar => Burmese,
                _ => null,
            };
        }

        return ChineseJapanese.Handles(codePoint) ? ChineseJapanese : null;
    }

    /// <summary>Determines whether the engine handles a code point (the set of characters of the engine in ICU).</summary>
    /// <param name="codePoint">The code point. An unpaired surrogate is a code point.</param>
    /// <returns><see langword="true"/> when the code point is one of the characters of the engine; <see langword="false"/> for a value that is not a code point.</returns>
    public abstract bool Handles(int codePoint);

    /// <summary>Finds the boundaries between the words of a run of characters the engine handles.</summary>
    /// <param name="text">The run: characters the engine handles (<see cref="Handles"/>). It is segmented as a whole: the result for a text that has other characters is not specified.</param>
    /// <param name="breakPositions">The buffer that receives the positions, in increasing order. A position is the index of the UTF-16 code unit of <paramref name="text"/> where a word starts, other than the start of the text. As many elements as the length of the text are always enough.</param>
    /// <returns>The number of positions written; 0 when the run is one word (or too short to have two words).</returns>
    /// <remarks>
    /// The start and the end of the run are boundaries, and are never written. The engines of ICU are given the whole text
    /// and the range of the run; the engines of this package are given the run.
    /// </remarks>
    /// <exception cref="ArgumentException"><paramref name="breakPositions"/> is too small.</exception>
    public int FindBreaks(ReadOnlySpan<char> text, Span<int> breakPositions)
    {
        if (text.Length < 2)
            return 0;

        return FindBreaksCore(text, breakPositions);
    }

    /// <summary>Finds the boundaries inside a run of at least two code units (<c>divideUpDictionaryRange</c>).</summary>
    private protected abstract int FindBreaksCore(ReadOnlySpan<char> text, Span<int> breakPositions);

    private protected static void AddBreak(Span<int> breakPositions, ref int count, int position)
    {
        if ((uint)count >= (uint)breakPositions.Length)
            throw new ArgumentException("The buffer is too small", nameof(breakPositions));

        breakPositions[count] = position;
        count++;
    }

    // The iteration of UText over the text (utext_current32, utext_next32, utext_previous32, utext_moveIndex32)
    private protected static int Current32(ReadOnlySpan<char> text, int position) => position < text.Length ? IcuWordDictionary.DecodeAt(text, position, out _) : -1;

    private protected static int Next32(ReadOnlySpan<char> text, ref int position)
    {
        if (position >= text.Length)
            return -1;

        var codePoint = IcuWordDictionary.DecodeAt(text, position, out var length);
        position += length;
        return codePoint;
    }

    private protected static int Previous32(ReadOnlySpan<char> text, ref int position)
    {
        if (position <= 0)
            return -1;

        position--;
        if (position > 0 && char.IsLowSurrogate(text[position]) && char.IsHighSurrogate(text[position - 1]))
        {
            position--;
        }

        return Current32(text, position);
    }

    private protected static int MoveIndex32(ReadOnlySpan<char> text, int position, int count)
    {
        for (var i = 0; i < count && position < text.Length; i++)
        {
            Next32(text, ref position);
        }

        return position;
    }
}
