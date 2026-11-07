using System.Runtime.InteropServices;

namespace Meziantou.Framework.JavaScriptEngine.Intl;

/// <summary>
/// The word segmentation of <c>Intl.Segmenter</c>, as the word break iterator of ICU computes it (ICU 78, the root rules
/// <c>icu4c/source/data/brkitr/rules/word.txt</c>, as V8 uses them).
/// </summary>
/// <remarks>
/// <para>
/// The rules are the ones of UAX #29 with the tailorings of ICU: the letters of the scripts written without spaces between
/// words (the characters of the line breaking class Complex_Context, such as Thai) are letters (ALetter); the Han,
/// Hiragana and Katakana characters (kana and kanji) are not letters, a run of them is one segment, as a run of Hangul
/// syllables is; neither run extends over an Extend or a Format character. The Han characters are not Extend.
/// </para>
/// <para>
/// A segment of the rules that contains characters of these scripts is then divided by the dictionary break engines of
/// ICU (<see cref="IcuDictionaryBreakEngine"/>, of Meziantou.Framework.Unicode.Dictionaries): Chinese and Japanese, Thai,
/// Lao, Khmer and Burmese. The parts of a segment all have its kind. Hangul and the Complex_Context scripts that have no
/// word list are not divided.
/// </para>
/// <para>
/// The kind of a segment is the rule status of ICU: a number (100), a word of letters (200), kana or kanji (400), or
/// none (0). It is the kind of the last character that is not Extend, Format or ZWJ, except that Hangul syllables, an
/// ExtendNumLet character, the single quote after a Hebrew letter and the Han characters that are not ideographs (such
/// as U+3005) give a word only when they follow a character they join and no Extend or Format character follows them.
/// </para>
/// </remarks>
internal static class WordBreaker
{
    private static readonly WordProperties[] AsciiProperties = CreateAsciiProperties();

    /// <summary>Adds the end of each segment of a text and its kind (<see cref="UnicodeWordKind"/>) to the lists.</summary>
    public static void Segment(string text, List<int> boundaries, List<byte> kinds)
    {
        var engines = new EngineStack();
        var foundBreaks = new List<int>();
        var start = 0;
        while (start < text.Length)
        {
            var end = start + GetRuleSegmentLength(text.AsSpan(start), out var kind, out var hasDictionaryCharacters);
            if (hasDictionaryCharacters && end - start > 1)
            {
                foundBreaks.Clear();
                if (PopulateDictionary(text, start, end, foundBreaks, engines) > 0 && foundBreaks.Count > 0)
                {
                    // RuleBasedBreakIterator::DictionaryCache: the boundaries of the engines, and the end of the segment
                    if (foundBreaks[^1] < end)
                    {
                        foundBreaks.Add(end);
                    }

                    foreach (var position in foundBreaks)
                    {
                        if (position > start)
                        {
                            boundaries.Add(position);
                            kinds.Add((byte)kind);
                        }
                    }

                    start = end;
                    continue;
                }
            }

            boundaries.Add(end);
            kinds.Add((byte)kind);
            start = end;
        }
    }

    /// <summary>
    /// Divides a segment of the rules with the dictionary break engines (<c>DictionaryCache::populateDictionary</c>): each
    /// run of dictionary characters is given to the engine of its first character.
    /// </summary>
    /// <returns>The number of breaks found, as ICU counts them; the breaks are in <paramref name="foundBreaks"/>.</returns>
    private static int PopulateDictionary(string text, int start, int end, List<int> foundBreaks, EngineStack engines)
    {
        var foundBreakCount = 0;
        var position = start;
        int[]? runBreaks = null;
        while (true)
        {
            var codePoint = -1;
            while (position < end)
            {
                codePoint = DecodeAt(text, position, out var length);
                if (GetProperties(codePoint).HasFlag(WordFlags.Dictionary))
                    break;

                position += length;
            }

            if (position >= end)
                break;

            // The run of the characters the engine handles (DictionaryBreakEngine::findBreaks): the unhandled engine skips it
            var engine = engines.GetEngine(codePoint);
            var runStart = position;
            while (position < end && engines.Handles(engine, DecodeAt(text, position, out var length)))
            {
                position += length;
            }

            if (engine is not null)
            {
                if (runBreaks is null || runBreaks.Length < position - runStart)
                {
                    runBreaks = new int[Math.Max(position - runStart, 64)];
                }

                var count = engine.FindBreaks(text.AsSpan(runStart, position - runStart), runBreaks);
                for (var i = 0; i < count; i++)
                {
                    foundBreaks.Add(runStart + runBreaks[i]);
                }

                foundBreakCount += count;
            }
        }

        return foundBreakCount;
    }

    /// <summary>Gets the length of the first segment of a text delimited by the rules, its kind, and whether it has dictionary characters.</summary>
    private static int GetRuleSegmentLength(ReadOnlySpan<char> text, out UnicodeWordKind kind, out bool hasDictionaryCharacters)
    {
        var actualPrevious = GetProperties(DecodeAt(text, 0, out var index));
        hasDictionaryCharacters = actualPrevious.HasFlag(WordFlags.Dictionary);

        // The last character that is not Extend, Format or ZWJ (none when the segment starts with them: ^$ExFm+), the one
        // before it, and whether the characters are adjacent
        var hasBase = !IsExtendOrFormat(actualPrevious.Class);
        var current = actualPrevious;
        var previous = default(WordProperties);
        var hasPrevious = false;
        var isPreviousAdjacent = false;
        var isAdjacent = true;

        // ^$Regional_Indicator $ExFm* $Regional_Indicator: the number of regional indicators of the segment
        var regionalIndicators = current.Class is WordClass.RegionalIndicator ? 1 : 0;

        while (index < text.Length)
        {
            var next = GetProperties(DecodeAt(text, index, out var length));
            if (actualPrevious.Class is WordClass.CR && next.Class is WordClass.LF)
            {
                // $CR $LF
            }
            else if (actualPrevious.Class is WordClass.CR or WordClass.LF or WordClass.Newline || next.Class is WordClass.CR or WordClass.LF or WordClass.Newline)
            {
                break;
            }
            else if ((actualPrevious.Class is WordClass.ZWJ && next.HasFlag(WordFlags.ExtendedPictographic))
                || (actualPrevious.Class is WordClass.WSegSpace && next.Class is WordClass.WSegSpace))
            {
                // $ZWJ $Extended_Pict, $WSegSpace $WSegSpace
            }
            else if (IsExtendOrFormat(next.Class))
            {
                // [^$CR $LF $Newline $ExFm] $ExFm*, ^$ExFm+
                actualPrevious = next;
                hasDictionaryCharacters |= next.HasFlag(WordFlags.Dictionary);
                isAdjacent = false;
                index += length;
                continue;
            }
            else if (!hasBase || !IsJoined(previous, hasPrevious, current, isAdjacent, next, text, index + length, regionalIndicators))
            {
                break;
            }

            if (hasBase)
            {
                previous = current;
                hasPrevious = true;
                isPreviousAdjacent = isAdjacent;
            }

            hasBase = true;
            current = next;
            isAdjacent = true;
            regionalIndicators = next.Class is WordClass.RegionalIndicator ? regionalIndicators + 1 : 0;
            actualPrevious = next;
            hasDictionaryCharacters |= next.HasFlag(WordFlags.Dictionary);
            index += length;
        }

        kind = hasBase ? GetKind(previous, hasPrevious && isPreviousAdjacent, current, isAdjacent) : UnicodeWordKind.None;
        return index;
    }

    /// <summary>Gets whether the rules join a character to the segment.</summary>
    private static bool IsJoined(WordProperties previous, bool hasPrevious, WordProperties current, bool isAdjacent, WordProperties next, ReadOnlySpan<char> text, int afterNext, int regionalIndicators)
    {
        var previousClass = hasPrevious ? previous.Class : WordClass.Other;
        var currentClass = current.Class;
        var nextClass = next.Class;
        if (IsLetter(currentClass))
        {
            // ($ALetterPlus | $Hebrew_Letter) $ExFm* ($ALetterPlus | $Hebrew_Letter), ... $Numeric
            if (IsLetter(nextClass) || nextClass is WordClass.Numeric)
                return true;

            // ($ALetterPlus | $Hebrew_Letter) $ExFm* ($MidLetter | $MidNumLet | $Single_Quote) $ExFm* ($ALetterPlus | $Hebrew_Letter)
            if (nextClass is WordClass.MidLetter or WordClass.MidNumLet or WordClass.SingleQuote && IsLetter(PeekClass(text, afterNext)))
                return true;

            if (currentClass is WordClass.HebrewLetter)
            {
                // $Hebrew_Letter $ExFm* $Single_Quote
                if (nextClass is WordClass.SingleQuote)
                    return true;

                // $Hebrew_Letter $ExFm* $Double_Quote $ExFm* $Hebrew_Letter
                if (nextClass is WordClass.DoubleQuote && PeekClass(text, afterNext) is WordClass.HebrewLetter)
                    return true;
            }
        }

        if (IsLetter(previousClass) && currentClass is WordClass.MidLetter or WordClass.MidNumLet or WordClass.SingleQuote && IsLetter(nextClass))
            return true;

        if (previousClass is WordClass.HebrewLetter && currentClass is WordClass.DoubleQuote && nextClass is WordClass.HebrewLetter)
            return true;

        if (currentClass is WordClass.Numeric)
        {
            // $Numeric $ExFm* $Numeric, ... ($ALetterPlus | $Hebrew_Letter)
            if (nextClass is WordClass.Numeric || IsLetter(nextClass))
                return true;

            // $Numeric $ExFm* ($MidNum | $MidNumLet | $Single_Quote) $ExFm* $Numeric
            if (nextClass is WordClass.MidNum or WordClass.MidNumLet or WordClass.SingleQuote && PeekClass(text, afterNext) is WordClass.Numeric)
                return true;
        }

        if (previousClass is WordClass.Numeric && currentClass is WordClass.MidNum or WordClass.MidNumLet or WordClass.SingleQuote && nextClass is WordClass.Numeric)
            return true;

        // $Katakana $ExFm* $Katakana
        if (currentClass is WordClass.Katakana && nextClass is WordClass.Katakana)
            return true;

        // (13a)
        if (nextClass is WordClass.ExtendNumLet && (IsLetter(currentClass) || currentClass is WordClass.Numeric or WordClass.Katakana or WordClass.ExtendNumLet))
            return true;

        // (13b)
        if (currentClass is WordClass.ExtendNumLet && (IsLetter(nextClass) || nextClass is WordClass.Numeric or WordClass.Katakana))
            return true;

        // ^$Regional_Indicator $ExFm* $Regional_Indicator
        if (currentClass is WordClass.RegionalIndicator && nextClass is WordClass.RegionalIndicator && (regionalIndicators & 1) != 0)
            return true;

        // $HangulSyllable $HangulSyllable, $KanaKanji $KanaKanji: without Extend or Format characters between them
        if (isAdjacent)
        {
            if (currentClass is WordClass.HangulSyllable && nextClass is WordClass.HangulSyllable)
                return true;

            if (current.HasFlag(WordFlags.KanaKanji) && next.HasFlag(WordFlags.KanaKanji))
                return true;
        }

        return false;
    }

    /// <summary>Gets the rule status of a segment from its last two characters that are not Extend, Format or ZWJ.</summary>
    /// <param name="previous">The character before the last one in the segment (Other when there is none).</param>
    /// <param name="isPreviousAdjacent">Whether there is a character before the last one in the segment, without Extend or Format character between them.</param>
    /// <param name="last">The last character.</param>
    /// <param name="isLastAtEnd">Whether the last character ends the segment: no Extend or Format character follows it.</param>
    private static UnicodeWordKind GetKind(WordProperties previous, bool isPreviousAdjacent, WordProperties last, bool isLastAtEnd)
    {
        switch (last.Class)
        {
            case WordClass.Numeric:
                // $Numeric $ExFm* {100}
                return UnicodeWordKind.Number;

            case WordClass.ALetter or WordClass.HebrewLetter:
                // $ALetterPlus $ExFm* {200}, $Hebrew_Letter $ExFm* {200}
                return UnicodeWordKind.Letter;

            case WordClass.Katakana:
                // $Katakana $ExFm* {400}
                return UnicodeWordKind.Ideographic;

            case WordClass.HangulSyllable:
                // $HangulSyllable {200}, $HangulSyllable $HangulSyllable {200}
                return isLastAtEnd ? UnicodeWordKind.Letter : UnicodeWordKind.None;

            case WordClass.ExtendNumLet:
                // (13a): the status of the rule of the character before it, 200 after another ExtendNumLet
                if (!isLastAtEnd)
                    return UnicodeWordKind.None;

                return previous.Class switch
                {
                    WordClass.ALetter or WordClass.HebrewLetter or WordClass.ExtendNumLet => UnicodeWordKind.Letter,
                    WordClass.Numeric => UnicodeWordKind.Number,
                    WordClass.Katakana => UnicodeWordKind.Ideographic,
                    _ => UnicodeWordKind.None,
                };

            case WordClass.SingleQuote:
                // $Hebrew_Letter $ExFm* $Single_Quote {200}
                return isLastAtEnd && previous.Class is WordClass.HebrewLetter ? UnicodeWordKind.Letter : UnicodeWordKind.None;
        }

        // $Hiragana $ExFm* {400}, $Ideographic $ExFm* {400}
        if (last.HasFlag(WordFlags.Hiragana) || last.HasFlag(WordFlags.Ideographic))
            return UnicodeWordKind.Ideographic;

        // $KanaKanji $KanaKanji {400}
        if (last.HasFlag(WordFlags.KanaKanji) && isLastAtEnd && isPreviousAdjacent && previous.HasFlag(WordFlags.KanaKanji))
            return UnicodeWordKind.Ideographic;

        return UnicodeWordKind.None;
    }

    /// <summary>Gets the class of the next character that is not Extend, Format or ZWJ, or Other at the end of the text.</summary>
    private static WordClass PeekClass(ReadOnlySpan<char> text, int index)
    {
        while (index < text.Length)
        {
            var value = GetProperties(DecodeAt(text, index, out var length)).Class;
            if (!IsExtendOrFormat(value))
                return value;

            index += length;
        }

        return WordClass.Other;
    }

    /// <summary>Decodes the code point at an index: an unpaired surrogate is returned as is, as UText does.</summary>
    private static int DecodeAt(ReadOnlySpan<char> text, int index, out int length)
    {
        var value = text[index];
        if (char.IsHighSurrogate(value) && index + 1 < text.Length && char.IsLowSurrogate(text[index + 1]))
        {
            length = 2;
            return char.ConvertToUtf32(value, text[index + 1]);
        }

        length = 1;
        return value;
    }

    private static bool IsLetter(WordClass value) => value is WordClass.ALetter or WordClass.HebrewLetter;

    // $ExFm = [$Extend $Format $ZWJ]
    private static bool IsExtendOrFormat(WordClass value) => value is WordClass.Extend or WordClass.Format or WordClass.ZWJ;

    private static WordProperties GetProperties(int codePoint)
    {
        if (codePoint < AsciiProperties.Length)
            return AsciiProperties[codePoint];

        return ComputeProperties(codePoint);
    }

    private static WordProperties[] CreateAsciiProperties()
    {
        var result = new WordProperties[128];
        for (var i = 0; i < result.Length; i++)
        {
            result[i] = ComputeProperties(i);
        }

        return result;
    }

    /// <summary>Gets the class of a code point in the rules of ICU, and the sets it belongs to.</summary>
    private static WordProperties ComputeProperties(int codePoint)
    {
        var wordBreak = UnicodeProperties.GetWordBreak(codePoint);
        var script = UnicodeScripts.GetScript(codePoint);
        var flags = WordFlags.None;

        // $KanaKanji = [$Han $Hiragana $Katakana]: the Han and Hiragana scripts, and Word_Break=Katakana
        if (script is UnicodeScript.Han or UnicodeScript.Hiragana || wordBreak is UnicodeWordBreak.Katakana)
        {
            flags |= WordFlags.KanaKanji | WordFlags.Dictionary;
        }

        if (script is UnicodeScript.Hiragana)
        {
            flags |= WordFlags.Hiragana;
        }

        if (UnicodeProperties.HasBinaryProperty(codePoint, UnicodeBinaryProperty.Ideographic))
        {
            flags |= WordFlags.Ideographic;
        }

        if (UnicodeEmoji.IsExtendedPictographic(codePoint))
        {
            flags |= WordFlags.ExtendedPictographic;
        }

        // $dictionary = [$ComplexContext $dictionaryCJK]
        var isComplexContext = UnicodeProperties.GetLineBreakClass(codePoint) == UnicodeLineBreakClass.ComplexContext;
        var isHangulSyllable = codePoint is >= 0xAC00 and <= 0xD7A3;
        if (isComplexContext || isHangulSyllable)
        {
            flags |= WordFlags.Dictionary;
        }

        var wordClass = wordBreak switch
        {
            UnicodeWordBreak.CR => WordClass.CR,
            UnicodeWordBreak.LF => WordClass.LF,
            UnicodeWordBreak.Newline => WordClass.Newline,

            // $Extend = [\p{Word_Break = Extend}-$Han]
            UnicodeWordBreak.Extend => script is UnicodeScript.Han ? WordClass.Other : WordClass.Extend,
            UnicodeWordBreak.Format => WordClass.Format,
            UnicodeWordBreak.ZWJ => WordClass.ZWJ,
            _ when isHangulSyllable => WordClass.HangulSyllable,
            UnicodeWordBreak.Katakana => WordClass.Katakana,

            // $ALetterPlus = [$ALetter-$dictionaryCJK [$ComplexContext-$Extend-$Control]]
            UnicodeWordBreak.ALetter => (flags & WordFlags.KanaKanji) != 0 ? WordClass.Other : WordClass.ALetter,
            UnicodeWordBreak.Other when isComplexContext && UnicodeProperties.GetGraphemeClusterBreak(codePoint) != UnicodeGraphemeClusterBreak.Control => WordClass.ALetter,
            UnicodeWordBreak.HebrewLetter => WordClass.HebrewLetter,
            UnicodeWordBreak.SingleQuote => WordClass.SingleQuote,
            UnicodeWordBreak.DoubleQuote => WordClass.DoubleQuote,
            UnicodeWordBreak.MidNumLet => WordClass.MidNumLet,
            UnicodeWordBreak.MidLetter => WordClass.MidLetter,
            UnicodeWordBreak.MidNum => WordClass.MidNum,
            UnicodeWordBreak.Numeric => WordClass.Numeric,
            UnicodeWordBreak.ExtendNumLet => WordClass.ExtendNumLet,
            UnicodeWordBreak.RegionalIndicator => WordClass.RegionalIndicator,
            UnicodeWordBreak.WSegSpace => WordClass.WSegSpace,
            _ => WordClass.Other,
        };

        return new WordProperties(wordClass, flags);
    }

    private enum WordClass : byte
    {
        Other,
        CR,
        LF,
        Newline,
        Extend,
        Format,
        ZWJ,
        RegionalIndicator,
        WSegSpace,
        Katakana,
        HebrewLetter,
        ALetter,
        SingleQuote,
        DoubleQuote,
        MidNumLet,
        MidLetter,
        MidNum,
        Numeric,
        ExtendNumLet,
        HangulSyllable,
    }

    [Flags]
    private enum WordFlags : byte
    {
        None = 0,

        // [$Han $Hiragana $Katakana]
        KanaKanji = 1,

        // A character of a script whose segments the dictionary break engines divide
        Dictionary = 2,
        ExtendedPictographic = 4,

        // The Hiragana script
        Hiragana = 8,

        // The Ideographic property
        Ideographic = 16,
    }

    [StructLayout(LayoutKind.Auto)]
    private readonly record struct WordProperties(WordClass Class, WordFlags Flags)
    {
        public bool HasFlag(WordFlags flag) => (Flags & flag) != 0;
    }

    /// <summary>
    /// The break engines a segmentation used, as an ICU break iterator keeps them (<c>getLanguageBreakEngine</c>): the
    /// last engine that handles a character is used. The factory of ICU keeps the engines it created for the whole process,
    /// so an engine of a word list handles all its characters, except the characters of a script that the unhandled engine
    /// of the segmentation took first (such as U+30FC, of the Common script, after a Common character no engine handles).
    /// </summary>
    private sealed class EngineStack
    {
        // The engines in the order of their creation; null is the unhandled engine of the segmentation (UnhandledEngine),
        // which finds no break and skips the characters of the scripts it was given so far
        private readonly List<IcuDictionaryBreakEngine?> _engines = [];
        private HashSet<UnicodeScript>? _unhandledScripts;

        /// <summary>Gets the engine of a character, or null for the unhandled engine.</summary>
        public IcuDictionaryBreakEngine? GetEngine(int codePoint)
        {
            for (var i = _engines.Count - 1; i >= 0; i--)
            {
                if (Handles(_engines[i], codePoint))
                    return _engines[i];
            }

            var engine = IcuDictionaryBreakEngine.GetEngine(codePoint);
            if (engine is not null)
            {
                _engines.Add(engine);
                return engine;
            }

            // Put it last so that scripts for which we have an engine get tried first
            if (_unhandledScripts is null)
            {
                _unhandledScripts = [];
                _engines.Insert(0, item: null);
            }

            // handleCharacter
            _unhandledScripts.Add(UnicodeScripts.GetScript(codePoint));
            return null;
        }

        /// <summary>Gets whether an engine (null for the unhandled engine) handles a character.</summary>
        public bool Handles(IcuDictionaryBreakEngine? engine, int codePoint)
        {
            if (engine is not null)
                return engine.Handles(codePoint);

            return codePoint >= 0 && _unhandledScripts is not null && _unhandledScripts.Contains(UnicodeScripts.GetScript(codePoint));
        }
    }
}
