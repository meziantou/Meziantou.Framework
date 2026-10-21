namespace Meziantou.Framework;

// This file is compiled into tools/Meziantou.Framework.Unicode.Generator, which writes the kind of each code point in
// the tables: the values are part of their format.

/// <summary>Specifies whether a segment delimited by the word boundaries of a text is a word, and which kind of word.</summary>
/// <remarks>
/// <para>
/// The kinds are the rule status values that ICU gives to its word boundaries (<c>UBRK_WORD_NONE</c>,
/// <c>UBRK_WORD_NUMBER</c>, <c>UBRK_WORD_LETTER</c>, <c>UBRK_WORD_KANA</c> and <c>UBRK_WORD_IDEO</c>), from which
/// <c>Intl.Segmenter</c> computes <c>isWordLike</c>: a segment is word-like when its kind is not <see cref="None"/>.
/// </para>
/// <para>
/// The kind is the one of the last character of the segment, ignoring the trailing Extend, Format and ZWJ characters
/// (rule WB4), as in the rules of ICU (<c>word.txt</c>). A low line or another ExtendNumLet character takes the kind of
/// the character before it: <c>a_</c> is a letter word, <c>1_</c> a number, and a low line on its own is not a word.
/// </para>
/// <para>
/// <c>UnicodeWordEnumerator.CurrentKind</c> gives the kind of each segment of <c>Unicode.EnumerateWordBoundaries</c>.
/// </para>
/// </remarks>
/// <seealso href="https://www.unicode.org/reports/tr29/" />
public enum UnicodeWordKind
{
    /// <summary>The segment is not a word: spaces, punctuation, symbols, emoji, line breaks (ICU: <c>UBRK_WORD_NONE</c>).</summary>
    None = 0,

    /// <summary>A number: the segment ends with a digit (Word_Break=Numeric), such as <c>42</c>, <c>3.14</c> or <c>a1</c> (ICU: <c>UBRK_WORD_NUMBER</c>).</summary>
    Number = 1,

    /// <summary>
    /// A word of letters, such as <c>hello</c>, <c>can't</c> or <c>1a</c>: the segment ends with a letter (Word_Break
    /// ALetter or Hebrew_Letter, but not a kana or an ideograph), a Hangul syllable, or a letter of a script written
    /// without spaces between words, such as Thai (ICU: <c>UBRK_WORD_LETTER</c>).
    /// </summary>
    Letter = 2,

    /// <summary>
    /// Kana: the segment ends with a Hiragana or a Katakana character. ICU defines <c>UBRK_WORD_KANA</c> for them, but
    /// its rules give them <c>UBRK_WORD_IDEO</c>, the status of the ideographs.
    /// </summary>
    Kana = 3,

    /// <summary>An ideograph, such as a Han character (Ideographic=Yes), or another Han letter such as the ideographic iteration mark U+3005 (ICU: <c>UBRK_WORD_IDEO</c>).</summary>
    Ideographic = 4,
}
