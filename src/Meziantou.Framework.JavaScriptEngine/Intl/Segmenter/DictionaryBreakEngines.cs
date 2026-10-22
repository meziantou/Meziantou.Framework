using System.Globalization;

namespace Meziantou.Framework.JavaScriptEngine.Intl;

/// <summary>
/// A break engine of ICU (<c>LanguageBreakEngine</c>): it finds the word boundaries inside a run of the characters of a
/// script that is written without spaces between words. The engines are ports of <c>icu4c/source/common/dictbe.cpp</c>
/// (ICU 78), with the word lists of ICU (<see cref="WordDictionaries"/>). The text is read as ICU reads its UText: by code
/// point, an unpaired surrogate being a code point.
/// </summary>
internal abstract class DictionaryBreakEngine
{
    /// <summary>Gets whether the engine handles a code point (<c>handles</c>); -1, the end of the text, is never handled.</summary>
    public abstract bool Handles(int codePoint);

    /// <summary>
    /// Finds the breaks of the run of the characters the engine handles that starts at <paramref name="startPosition"/>
    /// and ends at <paramref name="endPosition"/> at the latest (<c>DictionaryBreakEngine::findBreaks</c>): adds them to
    /// <paramref name="foundBreaks"/> in increasing order, without the start and the end of the run.
    /// </summary>
    /// <returns>The number of breaks found, as ICU counts them.</returns>
    public virtual int FindBreaks(ReadOnlySpan<char> text, int startPosition, int endPosition, List<int> foundBreaks, out int position)
    {
        position = startPosition;
        var codePoint = Current32(text, position);
        int current;
        while ((current = position) < endPosition && Handles(codePoint))
        {
            Next32(text, ref position);
            codePoint = Current32(text, position);
        }

        var result = DivideUpDictionaryRange(text, startPosition, current, foundBreaks);
        position = current;
        return result;
    }

    /// <summary>Finds the breaks inside a run of characters the engine handles (<c>divideUpDictionaryRange</c>).</summary>
    protected abstract int DivideUpDictionaryRange(ReadOnlySpan<char> text, int rangeStart, int rangeEnd, List<int> foundBreaks);

    // The iteration of UText over the whole text (utext_current32, utext_next32, utext_previous32, utext_moveIndex32)
    protected static int Current32(ReadOnlySpan<char> text, int position) => position < text.Length ? WordDictionary.DecodeAt(text, position, out _) : -1;

    protected static int Next32(ReadOnlySpan<char> text, ref int position)
    {
        if (position >= text.Length)
            return -1;

        var codePoint = WordDictionary.DecodeAt(text, position, out var length);
        position += length;
        return codePoint;
    }

    protected static int Previous32(ReadOnlySpan<char> text, ref int position)
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

    protected static int MoveIndex32(ReadOnlySpan<char> text, int position, int count)
    {
        for (var i = 0; i < count && position < text.Length; i++)
        {
            Next32(text, ref position);
        }

        return position;
    }
}

/// <summary>
/// The engine of ICU for the characters that no other engine handles (<c>UnhandledEngine</c>): it finds no break, and
/// skips the characters of the scripts it was given so far. A segmentation has its own instance, as an ICU break
/// iterator has.
/// </summary>
internal sealed class UnhandledBreakEngine : DictionaryBreakEngine
{
    private readonly HashSet<UnicodeScript> _scripts = [];

    public override bool Handles(int codePoint) => codePoint >= 0 && _scripts.Contains(UnicodeScripts.GetScript(codePoint));

    /// <summary>Adds the script of a character to the characters the engine skips (<c>handleCharacter</c>).</summary>
    public void HandleCharacter(int codePoint) => _scripts.Add(UnicodeScripts.GetScript(codePoint));

    public override int FindBreaks(ReadOnlySpan<char> text, int startPosition, int endPosition, List<int> foundBreaks, out int position)
    {
        position = startPosition;
        var codePoint = Current32(text, position);
        while (position < endPosition && Handles(codePoint))
        {
            Next32(text, ref position);
            codePoint = Current32(text, position);
        }

        return 0;
    }

    protected override int DivideUpDictionaryRange(ReadOnlySpan<char> text, int rangeStart, int rangeEnd, List<int> foundBreaks) => 0;
}

/// <summary>
/// The dictionary break engines of the scripts of Southeast Asia whose characters have the line breaking class
/// Complex_Context (<c>ThaiBreakEngine</c>, <c>LaoBreakEngine</c>, <c>BurmeseBreakEngine</c> and <c>KhmerBreakEngine</c>).
/// </summary>
/// <remarks>
/// The run is divided into the longest dictionary words that are followed by other dictionary words, with up to three
/// words of lookahead. The characters that do not start a dictionary word are attached to the word before them, up to
/// a plausible word start; a break is never before a combining mark, and Thai attaches the elision and repetition
/// characters to the word before them.
/// </remarks>
internal sealed class ComplexContextBreakEngine : DictionaryBreakEngine
{
    // How many words in a row are "good enough"
    private const int Lookahead = 3;

    // Will not combine a non-word with a preceding dictionary word longer than this
    private const int RootCombineThreshold = 3;

    // Will not combine a non-word that shares at least this much prefix with a dictionary word, with a preceding word
    private const int PrefixCombineThreshold = 3;

    // Minimum number of characters for two words
    private const int MinWordSpan = 4;

    // The Thai elision and repetition characters
    private const int ThaiPaiyannoi = 0x0E2F;
    private const int ThaiMaiyamok = 0x0E46;

    private readonly UnicodeScript _script;
    private readonly WordDictionaryKind _dictionaryKind;

    private ComplexContextBreakEngine(UnicodeScript script, WordDictionaryKind dictionaryKind)
    {
        _script = script;
        _dictionaryKind = dictionaryKind;
    }

    public static ComplexContextBreakEngine Thai { get; } = new(UnicodeScript.Thai, WordDictionaryKind.Thai);

    public static ComplexContextBreakEngine Lao { get; } = new(UnicodeScript.Lao, WordDictionaryKind.Lao);

    public static ComplexContextBreakEngine Burmese { get; } = new(UnicodeScript.Myanmar, WordDictionaryKind.Burmese);

    public static ComplexContextBreakEngine Khmer { get; } = new(UnicodeScript.Khmer, WordDictionaryKind.Khmer);

    // [[:<script>:]&[:LineBreak=SA:]]
    public override bool Handles(int codePoint)
        => codePoint >= 0 && UnicodeProperties.GetLineBreakClass(codePoint) == UnicodeLineBreakClass.ComplexContext && UnicodeScripts.GetScript(codePoint) == _script;

    protected override int DivideUpDictionaryRange(ReadOnlySpan<char> text, int rangeStart, int rangeEnd, List<int> foundBreaks)
    {
        // Not enough characters for two words: Thai counts the code points (and needs one more), the other scripts the code units
        if (_script == UnicodeScript.Thai ? MoveIndex32(text, rangeStart, MinWordSpan) >= rangeEnd : rangeEnd - rangeStart < MinWordSpan)
            return 0;

        var dictionary = WordDictionaries.Default.Get(_dictionaryKind);
        var wordsFound = 0;
        var words = new PossibleWord[Lookahead];
        for (var i = 0; i < words.Length; i++)
        {
            words[i] = new PossibleWord();
        }

        var position = rangeStart;
        int current;
        while ((current = position) < rangeEnd)
        {
            var cpWordLength = 0;
            var cuWordLength = 0;

            // Look for candidate words at the current position
            var candidates = words[wordsFound % Lookahead].Candidates(text, ref position, dictionary, rangeEnd);
            if (candidates == 1)
            {
                // If we found exactly one, use that
                cuWordLength = words[wordsFound % Lookahead].AcceptMarked(ref position);
                cpWordLength = words[wordsFound % Lookahead].MarkedCodePointLength;
                wordsFound++;
            }
            else if (candidates > 1)
            {
                // If there was more than one, see which one can take us forward the most words
                MarkBestCandidate(text, ref position, dictionary, words, wordsFound, rangeEnd);
                cuWordLength = words[wordsFound % Lookahead].AcceptMarked(ref position);
                cpWordLength = words[wordsFound % Lookahead].MarkedCodePointLength;
                wordsFound++;
            }

            // We come here after having either found a word or not. We look ahead to the next word. If it's not a
            // dictionary word, we will combine it with the word we just found (if there is one), but only if the
            // preceding word does not exceed the threshold. The position is now at the end of the word we found.
            if (position < rangeEnd && cpWordLength < RootCombineThreshold)
            {
                // If it is a dictionary word, do nothing. If it isn't, then if there is no preceding word, or the non-word
                // shares less than the minimum threshold of characters with a dictionary word, then scan to resynchronize
                if (words[wordsFound % Lookahead].Candidates(text, ref position, dictionary, rangeEnd) <= 0
                    && (cuWordLength == 0 || words[wordsFound % Lookahead].LongestPrefix < PrefixCombineThreshold))
                {
                    // Look for a plausible word boundary
                    var remaining = rangeEnd - (current + cuWordLength);
                    var chars = 0;
                    while (true)
                    {
                        var pcIndex = position;
                        var pc = Next32(text, ref position);
                        var pcSize = position - pcIndex;
                        chars += pcSize;
                        remaining -= pcSize;
                        if (remaining <= 0)
                            break;

                        var uc = Current32(text, position);
                        if (IsEndOfWord(pc) && IsStartOfWord(uc))
                        {
                            // Maybe. See if it's in the dictionary.
                            var count = words[(wordsFound + 1) % Lookahead].Candidates(text, ref position, dictionary, rangeEnd);
                            position = current + cuWordLength + chars;
                            if (count > 0)
                                break;
                        }
                    }

                    // Bump the word count if there wasn't already one
                    if (cuWordLength <= 0)
                    {
                        wordsFound++;
                    }

                    // Update the length with the passed-over characters
                    cuWordLength += chars;
                }
                else
                {
                    // Back up to where we were for next iteration
                    position = current + cuWordLength;
                }
            }

            // Never stop before a combining mark
            int currentPosition;
            while ((currentPosition = position) < rangeEnd && IsMark(Current32(text, position)))
            {
                Next32(text, ref position);
                cuWordLength += position - currentPosition;
            }

            // Thai: look ahead for possible suffixes if a dictionary word does not follow. We do this in code rather than
            // using a rule so that the heuristic resynch continues to function. For example, one of the suffix characters
            // could be a typo in the middle of a word.
            if (_script == UnicodeScript.Thai && position < rangeEnd && cuWordLength > 0)
            {
                int uc;
                if (words[wordsFound % Lookahead].Candidates(text, ref position, dictionary, rangeEnd) <= 0 && IsThaiSuffix(uc = Current32(text, position)))
                {
                    if (uc == ThaiPaiyannoi)
                    {
                        if (!IsThaiSuffix(Previous32(text, ref position)))
                        {
                            // Skip over previous end and PAIYANNOI
                            Next32(text, ref position);
                            var paiyannoiIndex = position;
                            Next32(text, ref position);
                            cuWordLength += position - paiyannoiIndex;
                            uc = Current32(text, position);
                        }
                        else
                        {
                            // Restore prior position
                            Next32(text, ref position);
                        }
                    }

                    if (uc == ThaiMaiyamok)
                    {
                        if (Previous32(text, ref position) != ThaiMaiyamok)
                        {
                            // Skip over previous end and MAIYAMOK
                            Next32(text, ref position);
                            var maiyamokIndex = position;
                            Next32(text, ref position);
                            cuWordLength += position - maiyamokIndex;
                        }
                        else
                        {
                            // Restore prior position
                            Next32(text, ref position);
                        }
                    }
                }
                else
                {
                    position = current + cuWordLength;
                }
            }

            // Did we find a word on this iteration? If so, push it on the break stack
            if (cuWordLength > 0)
            {
                foundBreaks.Add(current + cuWordLength);
            }
        }

        // Don't return a break for the end of the dictionary range if there is one there
        if (foundBreaks.Count > 0 && foundBreaks[^1] >= rangeEnd)
        {
            foundBreaks.RemoveAt(foundBreaks.Count - 1);
            wordsFound--;
        }

        return wordsFound;
    }

    // The candidates of the word at the current position that are followed by the most dictionary words (the gotos of ICU)
    private static void MarkBestCandidate(ReadOnlySpan<char> text, ref int position, WordDictionary dictionary, PossibleWord[] words, int wordsFound, int rangeEnd)
    {
        // If we're already at the end of the range, we're done
        if (position >= rangeEnd)
            return;

        do
        {
            if (words[(wordsFound + 1) % Lookahead].Candidates(text, ref position, dictionary, rangeEnd) > 0)
            {
                // Followed by another dictionary word; mark first word as a good candidate
                words[wordsFound % Lookahead].MarkCurrent();

                // If we're already at the end of the range, we're done
                if (position >= rangeEnd)
                    return;

                // See if any of the possible second words is followed by a third word
                do
                {
                    // If we find a third word, stop right away
                    if (words[(wordsFound + 2) % Lookahead].Candidates(text, ref position, dictionary, rangeEnd) > 0)
                    {
                        words[wordsFound % Lookahead].MarkCurrent();
                        return;
                    }
                }
                while (words[(wordsFound + 1) % Lookahead].BackUp(ref position));
            }
        }
        while (words[wordsFound % Lookahead].BackUp(ref position));
    }

    // The characters that can end a word (fEndWordSet)
    private bool IsEndOfWord(int codePoint) => _script switch
    {
        // Not MAI HAN-AKAT, SARA E through SARA AI MAIMALAI
        UnicodeScript.Thai => codePoint is not (0x0E31 or (>= 0x0E40 and <= 0x0E44)) && Handles(codePoint),

        // Not the prefix vowels
        UnicodeScript.Lao => codePoint is not (>= 0x0EC0 and <= 0x0EC4) && Handles(codePoint),

        // Not KHMER SIGN COENG, which combines some following characters
        UnicodeScript.Khmer => codePoint != 0x17D2 && Handles(codePoint),
        _ => Handles(codePoint),
    };

    // The characters that can start a word (fBeginWordSet)
    private bool IsStartOfWord(int codePoint) => _script switch
    {
        // KO KAI through HO NOKHUK, SARA E through SARA AI MAIMALAI
        UnicodeScript.Thai => codePoint is (>= 0x0E01 and <= 0x0E2E) or (>= 0x0E40 and <= 0x0E44),

        // The basic consonants, the digraph consonants and the prefix vowels
        UnicodeScript.Lao => codePoint is (>= 0x0E81 and <= 0x0EAE) or (>= 0x0EDC and <= 0x0EDD) or (>= 0x0EC0 and <= 0x0EC4),

        // The basic consonants and the independent vowels
        UnicodeScript.Myanmar => codePoint is >= 0x1000 and <= 0x102A,
        _ => codePoint is >= 0x1780 and <= 0x17B3,
    };

    // The combining marks of the script (fMarkSet), and U+0020
    private bool IsMark(int codePoint)
        => codePoint == ' ' || (Handles(codePoint) && UnicodeProperties.GetGeneralCategory(codePoint) is UnicodeCategory.NonSpacingMark or UnicodeCategory.SpacingCombiningMark or UnicodeCategory.EnclosingMark);

    private static bool IsThaiSuffix(int codePoint) => codePoint is ThaiPaiyannoi or ThaiMaiyamok;

    /// <summary>The candidate words at a position of the text (<c>PossibleWord</c>).</summary>
    private sealed class PossibleWord
    {
        // The maximum number of words in the dictionary that form a nested sequence (POSSIBLE_WORD_LIST_MAX)
        private const int MaxCandidates = 20;

        // The lengths of the candidates, in increasing order
        private readonly int[] _codeUnitLengths = new int[MaxCandidates];
        private readonly int[] _codePointLengths = new int[MaxCandidates];
        private readonly int[] _values = new int[MaxCandidates];
        private int _count;
        private int _offset = -1;
        private int _mark;
        private int _current;

        /// <summary>Gets the longest prefix this candidate location shares with a dictionary word, in code points.</summary>
        public int LongestPrefix { get; private set; }

        /// <summary>Gets the length of the marked word, in code points.</summary>
        public int MarkedCodePointLength => _codePointLengths[_mark];

        /// <summary>Fills the list of candidates if needed, selects the longest, moves the position after it, and returns the number found.</summary>
        public int Candidates(ReadOnlySpan<char> text, ref int position, WordDictionary dictionary, int rangeEnd)
        {
            var start = position;
            if (start != _offset)
            {
                _offset = start;
                _count = dictionary.Matches(text, start, rangeEnd - start, _codeUnitLengths, _codePointLengths, _values, out var prefix);
                LongestPrefix = prefix;
            }

            if (_count > 0)
            {
                position = start + _codeUnitLengths[_count - 1];
            }

            _current = _count - 1;
            _mark = _current;
            return _count;
        }

        /// <summary>Selects the marked candidate, moves the position after it, and returns its length in code units.</summary>
        public int AcceptMarked(ref int position)
        {
            position = _offset + _codeUnitLengths[_mark];
            return _codeUnitLengths[_mark];
        }

        /// <summary>Backs up from the current candidate to the next shorter one, and moves the position after it; returns whether it exists.</summary>
        public bool BackUp(ref int position)
        {
            if (_current > 0)
            {
                _current--;
                position = _offset + _codeUnitLengths[_current];
                return true;
            }

            return false;
        }

        /// <summary>Marks the current candidate as the one we like.</summary>
        public void MarkCurrent() => _mark = _current;
    }
}

/// <summary>
/// The dictionary break engine of ICU for Chinese and Japanese (<c>CjkBreakEngine</c>, without the phrase breaking):
/// the segmentation of the run whose words have the smallest total cost.
/// </summary>
/// <remarks>
/// The run is normalized to NFKC. The cost of a word is its value in the word list; a character that does not start a
/// word of one character is a word of the highest cost (255); and the first character of a run of katakana starts a
/// word that is the whole run, with a cost that depends on its length.
/// </remarks>
internal sealed class CjkBreakEngine : DictionaryBreakEngine
{
    private const int MaxWordSize = 20;
    private const int MaxKatakanaLength = 8;
    private const int MaxKatakanaGroupLength = 20;
    private const uint MaxSnlp = 255;

    // The cost of a run of katakana by length, based on the length frequencies of all the katakana phrases in the dictionary
    private static readonly uint[] KatakanaCosts = [8192, 984, 408, 240, 204, 252, 300, 372, 480];

    private CjkBreakEngine()
    {
    }

    public static CjkBreakEngine Instance { get; } = new();

    // [[:Han:][:Hiragana:][:Katakana:]ーｰﾞﾟ]
    public override bool Handles(int codePoint)
    {
        if (codePoint < 0)
            return false;

        return codePoint is 0x30FC or 0xFF70 or 0xFF9E or 0xFF9F || UnicodeScripts.GetScript(codePoint) is UnicodeScript.Han or UnicodeScript.Hiragana or UnicodeScript.Katakana;
    }

    protected override int DivideUpDictionaryRange(ReadOnlySpan<char> text, int rangeStart, int rangeEnd, List<int> foundBreaks)
    {
        if (rangeStart >= rangeEnd)
            return 0;

        // The text of the run, normalized to NFKC; inputMap[index in inString] is the corresponding index in the text
        // (null when it is the index plus rangeStart)
        ReadOnlySpan<char> inString = text[rangeStart..rangeEnd];
        List<int>? inputMap = null;
        if (!UnicodeNormalization.IsNormalized(inString, NormalizationForm.FormKC))
        {
            var normalizedInput = new StringBuilder(inString.Length);
            var normalizedMap = new List<int>(inString.Length + 1);
            var fragment = new StringBuilder();
            for (var sourceIndex = 0; sourceIndex < inString.Length;)
            {
                // Once per normalization chunk
                fragment.Clear();
                var fragmentStart = sourceIndex;
                var codePoint = WordDictionary.DecodeAt(inString, sourceIndex, out var length);
                while (true)
                {
                    AppendCodePoint(fragment, codePoint);
                    sourceIndex += length;
                    if (sourceIndex == inString.Length)
                        break;

                    codePoint = WordDictionary.DecodeAt(inString, sourceIndex, out length);
                    if (HasNfkcBoundaryBefore(codePoint))
                        break;
                }

                normalizedInput.Append(UnicodeNormalization.Normalize(fragment.ToString(), NormalizationForm.FormKC));

                // Map every position in the normalized chunk to the start of the chunk in the original input
                while (normalizedMap.Count < normalizedInput.Length)
                {
                    normalizedMap.Add(fragmentStart + rangeStart);
                }
            }

            normalizedMap.Add(inString.Length + rangeStart);
            inputMap = normalizedMap;
            inString = normalizedInput.ToString();
        }

        var codePointCount = CountCodePoints(inString);
        if (codePointCount != inString.Length)
        {
            // There are supplementary characters in the input. The dictionary produces boundary positions in terms of code
            // point indexes, not in terms of code unit indexes: the map converts them too.
            var hadExistingMap = inputMap is not null;
            inputMap ??= new List<int>(codePointCount + 1);
            var codePointIndex = 0;
            for (var codeUnitIndex = 0; ; codeUnitIndex = MoveIndex32(inString, codeUnitIndex, 1))
            {
                if (hadExistingMap)
                {
                    inputMap[codePointIndex] = inputMap[codeUnitIndex];
                }
                else
                {
                    inputMap.Add(codeUnitIndex + rangeStart);
                }

                codePointIndex++;
                if (codeUnitIndex == inString.Length)
                    break;
            }
        }

        // bestSnlp[i] is the snlp of the best segmentation of the first i code points in the range to be matched
        var bestSnlp = new uint[codePointCount + 1];
        bestSnlp.AsSpan(1).Fill(uint.MaxValue);

        // prev[i] is the index of the last code point in the previous word in the best segmentation of the first i code points
        var prev = new int[codePointCount + 1];
        prev.AsSpan().Fill(-1);

        var dictionary = WordDictionaries.Default.Get(WordDictionaryKind.ChineseJapanese);
        Span<int> codeUnitLengths = stackalloc int[MaxWordSize + 1];
        Span<int> lengths = stackalloc int[MaxWordSize + 1];
        Span<int> values = stackalloc int[MaxWordSize + 1];

        // Dynamic programming to find the best segmentation: i is the code point index, ix the corresponding code unit index
        var ix = 0;
        var isPreviousKatakana = false;
        for (var i = 0; i < codePointCount; i++, ix = MoveIndex32(inString, ix, 1))
        {
            if (bestSnlp[i] == uint.MaxValue)
                continue;

            // The lengths are in code points
            var count = dictionary.Matches(inString, ix, MaxWordSize, codeUnitLengths[..MaxWordSize], lengths[..MaxWordSize], values[..MaxWordSize], out _);

            // If there are no single character matches found in the dictionary starting with this character, treat
            // character as a 1-character word with the highest value possible, i.e. the least likely to occur. Exclude
            // Korean characters from this treatment, as they should be left together by default.
            var codePoint = WordDictionary.DecodeAt(inString, ix, out _);
            if ((count == 0 || lengths[0] != 1) && !IsHangulSyllable(codePoint))
            {
                values[count] = (int)MaxSnlp;
                lengths[count] = 1;
                count++;
            }

            for (var j = 0; j < count; j++)
            {
                var newSnlp = bestSnlp[i] + (uint)values[j];
                var end = lengths[j] + i;
                if (newSnlp < bestSnlp[end])
                {
                    bestSnlp[end] = newSnlp;
                    prev[end] = i;
                }
            }

            // In Japanese, a katakana word in single character is pretty rare. So we apply the following heuristic to
            // katakana: any continuous run of katakana characters is considered a candidate word with a default cost
            // specified in the katakana cost table according to its length.
            var isKatakana = IsKatakana(codePoint);
            var katakanaRunLength = 1;
            if (!isPreviousKatakana && isKatakana)
            {
                // Find the end of the continuous run of katakana characters
                var j = MoveIndex32(inString, ix, 1);
                while (j < inString.Length && katakanaRunLength < MaxKatakanaGroupLength && IsKatakana(WordDictionary.DecodeAt(inString, j, out _)))
                {
                    j = MoveIndex32(inString, j, 1);
                    katakanaRunLength++;
                }

                if (katakanaRunLength < MaxKatakanaGroupLength)
                {
                    var newSnlp = bestSnlp[i] + GetKatakanaCost(katakanaRunLength);
                    if (newSnlp < bestSnlp[i + katakanaRunLength])
                    {
                        bestSnlp[i + katakanaRunLength] = newSnlp;
                        prev[i + katakanaRunLength] = i;
                    }
                }
            }

            isPreviousKatakana = isKatakana;
        }

        // The boundaries of the best segmentation, from the end (t_boundary)
        var boundaries = new List<int>();
        if (bestSnlp[codePointCount] == uint.MaxValue)
        {
            // No segmentation found, set boundary to end of range
            boundaries.Add(codePointCount);
        }
        else
        {
            for (var i = codePointCount; i > 0; i = prev[i])
            {
                boundaries.Add(i);
            }
        }

        // Add a break for the start of the dictionary range if there is not one there already
        if (foundBreaks.Count == 0 || foundBreaks[^1] < rangeStart)
        {
            boundaries.Add(0);
        }

        // Convert the positions (indices in the normalized input) back to indices in the text, in increasing order
        var previousTextPosition = -1;
        var breakCount = 0;
        for (var i = boundaries.Count - 1; i >= 0; i--)
        {
            var codePointPosition = boundaries[i];
            var textPosition = inputMap is not null ? inputMap[codePointPosition] : codePointPosition + rangeStart;

            // Normalization expanded the input text, the dictionary found a boundary within the expansion, giving two
            // boundaries with the same index in the original text: the second is ignored
            if (textPosition > previousTextPosition && textPosition != rangeStart)
            {
                foundBreaks.Add(textPosition);
                breakCount++;
            }

            previousTextPosition = textPosition;
        }

        if (foundBreaks.Count > 0 && foundBreaks[^1] == rangeEnd)
        {
            foundBreaks.RemoveAt(foundBreaks.Count - 1);
            breakCount--;
        }

        return breakCount;
    }

    private static uint GetKatakanaCost(int wordLength) => wordLength > MaxKatakanaLength ? 8192 : KatakanaCosts[wordLength];

    private static bool IsKatakana(int value) => value is (>= 0x30A1 and <= 0x30FE and not 0x30FB) or (>= 0xFF66 and <= 0xFF9F);

    private static bool IsHangulSyllable(int value) => value is >= 0xAC00 and <= 0xD7A3;

    // Normalizer2::hasBoundaryBefore of NFKC for the characters of the engine: the decomposition of the character starts
    // with a starter (the halfwidth voiced sound marks decompose to combining marks)
    private static bool HasNfkcBoundaryBefore(int codePoint)
    {
        if (UnicodeProperties.GetCanonicalCombiningClass(codePoint) != 0)
            return false;

        if (codePoint < 0x80)
            return true;

        var decomposition = UnicodeNormalization.Normalize(char.ConvertFromUtf32(codePoint), NormalizationForm.FormKD);
        return UnicodeProperties.GetCanonicalCombiningClass(WordDictionary.DecodeAt(decomposition, 0, out _)) == 0;
    }

    private static int CountCodePoints(ReadOnlySpan<char> value)
    {
        var count = 0;
        for (var index = 0; index < value.Length; index = MoveIndex32(value, index, 1))
        {
            count++;
        }

        return count;
    }

    private static void AppendCodePoint(StringBuilder builder, int codePoint)
    {
        if (codePoint <= 0xFFFF)
        {
            builder.Append((char)codePoint);
        }
        else
        {
            builder.Append(char.ConvertFromUtf32(codePoint));
        }
    }
}
