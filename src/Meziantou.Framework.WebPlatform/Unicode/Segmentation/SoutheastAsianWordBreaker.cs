using System.Globalization;

namespace Meziantou.Framework.WebPlatform.Unicode;

/// <summary>
/// The dictionary-based word segmentation of the Southeast Asian scripts that write no space between the words (the characters of
/// the line breaking class SA of Thai, Lao, Myanmar and Khmer), for the lexical word breaking of CSS Text
/// (https://drafts.csswg.org/css-text-4/#lexical-breaking): a port of the dictionary break engines of ICU
/// (<c>ThaiBreakEngine</c>, <c>LaoBreakEngine</c>, <c>BurmeseBreakEngine</c> and <c>KhmerBreakEngine</c> of
/// <c>icu4c/source/common/dictbe.cpp</c>, ICU 78, the line breaking of Chrome) with their word lists (<see cref="WordDictionary"/>).
/// </summary>
/// <remarks>
/// The text is divided into the longest dictionary words that are followed by other dictionary words (up to three words of
/// lookahead); the characters that do not start a dictionary word are attached to the word before them, up to a plausible
/// word start. A word list is loaded on first use (about 70 to 260 KB compressed, 0.5 to 2 MB in memory for Khmer); an instance
/// is immutable and shared by all the threads.
/// </remarks>
internal sealed class SoutheastAsianWordBreaker
{
    // How many words in a row are "good enough"
    private const int Lookahead = 3;

    // Will not combine a non-word with a preceding dictionary word longer than this
    private const int RootCombineThreshold = 3;

    // Will not combine a non-word that shares at least this much prefix with a dictionary word, with a preceding word
    private const int PrefixCombineThreshold = 3;

    // Minimum number of characters for two words
    private const int MinWordSpan = 4;

    // The Thai elision and repetition characters, which follow a word
    private const int ThaiPaiyannoi = 0x0E2F;
    private const int ThaiMaiyamok = 0x0E46;

    // The maximum number of words that form a nested sequence (POSSIBLE_WORD_LIST_MAX)
    private const int MaxCandidates = 20;

    private static readonly Lazy<SoutheastAsianWordBreaker> Thai = new(() => new(UnicodeScript.Thai, "thai"), LazyThreadSafetyMode.ExecutionAndPublication);
    private static readonly Lazy<SoutheastAsianWordBreaker> Lao = new(() => new(UnicodeScript.Lao, "lao"), LazyThreadSafetyMode.ExecutionAndPublication);
    private static readonly Lazy<SoutheastAsianWordBreaker> Burmese = new(() => new(UnicodeScript.Myanmar, "burmese"), LazyThreadSafetyMode.ExecutionAndPublication);
    private static readonly Lazy<SoutheastAsianWordBreaker> Khmer = new(() => new(UnicodeScript.Khmer, "khmer"), LazyThreadSafetyMode.ExecutionAndPublication);

    private readonly UnicodeScript _script;
    private readonly WordDictionary _dictionary;

    private SoutheastAsianWordBreaker(UnicodeScript script, string dictionary)
    {
        _script = script;
        _dictionary = WordDictionary.Load(dictionary);
    }

    /// <summary>
    /// Returns the script of a character whose words are segmented with a dictionary (the characters of the line breaking class
    /// SA of Thai, Lao, Myanmar and Khmer), or <see cref="UnicodeScript.Unknown"/> for the other characters.
    /// </summary>
    public static UnicodeScript GetDictionaryScript(int codePoint)
    {
        if (CharacterProperties.GetLineBreakClass(codePoint) != LineBreakClass.SA)
            return UnicodeScript.Unknown;

        var script = UnicodeScripts.GetScript(codePoint);
        return script is UnicodeScript.Thai or UnicodeScript.Lao or UnicodeScript.Myanmar or UnicodeScript.Khmer ? script : UnicodeScript.Unknown;
    }

    /// <summary>Returns the word breaker of a script of <see cref="GetDictionaryScript"/> (its word list is loaded on first use).</summary>
    public static SoutheastAsianWordBreaker Get(UnicodeScript script) => script switch
    {
        UnicodeScript.Thai => Thai.Value,
        UnicodeScript.Lao => Lao.Value,
        UnicodeScript.Myanmar => Burmese.Value,
        UnicodeScript.Khmer => Khmer.Value,
        _ => throw new ArgumentOutOfRangeException(nameof(script)),
    };

    /// <summary>Whether a character is one of the characters of the engine (the characters of the line breaking class SA of its script).</summary>
    public bool Handles(int codePoint) => CharacterProperties.GetLineBreakClass(codePoint) == LineBreakClass.SA && UnicodeScripts.GetScript(codePoint) == _script;

    /// <summary>
    /// Adds the word boundaries inside a range of the text whose characters are all handled by the engine (<see cref="Handles"/>)
    /// to <paramref name="breaks"/>, in order (<c>divideUpDictionaryRange</c>): the positions where a word ends, without the start
    /// and the end of the range.
    /// </summary>
    public void FindBreaks(ReadOnlySpan<char> text, int rangeStart, int rangeEnd, List<int> breaks)
    {
        // Not enough characters for two words (the Thai engine counts the code points: one more is needed)
        if (rangeEnd - rangeStart < MinWordSpan || (_script == UnicodeScript.Thai && MoveIndex32(text, rangeStart, MinWordSpan) >= rangeEnd))
            return;

        var firstBreak = breaks.Count;
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
            var candidates = words[wordsFound % Lookahead].Candidates(text, ref position, _dictionary, rangeEnd);

            if (candidates == 1)
            {
                // If we found exactly one, use that
                cuWordLength = words[wordsFound % Lookahead].AcceptMarked(ref position);
                cpWordLength = words[wordsFound % Lookahead].MarkedLength;
                wordsFound++;
            }
            else if (candidates > 1)
            {
                // If there was more than one, see which one can take us forward the most words
                FindBest(text, ref position, words, wordsFound, rangeEnd);
                cuWordLength = words[wordsFound % Lookahead].AcceptMarked(ref position);
                cpWordLength = words[wordsFound % Lookahead].MarkedLength;
                wordsFound++;
            }

            // We come here after having either found a word or not. We look ahead to the next word. If it's not a dictionary
            // word, we will combine it with the word we just found (if there is one), but only if the preceding word does not
            // exceed the threshold. The position is now at the end of the word we found.
            if (position < rangeEnd && cpWordLength < RootCombineThreshold)
            {
                // If it is a dictionary word, do nothing. If it isn't, then if there is no preceding word, or the non-word shares
                // less than the minimum threshold of characters with a dictionary word, then scan to resynchronize
                if (words[wordsFound % Lookahead].Candidates(text, ref position, _dictionary, rangeEnd) <= 0
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
                            var count = words[(wordsFound + 1) % Lookahead].Candidates(text, ref position, _dictionary, rangeEnd);
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

            // Thai: look ahead for possible suffixes if a dictionary word does not follow. We do this in code rather than using a
            // rule so that the heuristic resynch continues to function. For example, one of the suffix characters could be a typo
            // in the middle of a word.
            if (_script == UnicodeScript.Thai && position < rangeEnd && cuWordLength > 0)
            {
                int uc;
                if (words[wordsFound % Lookahead].Candidates(text, ref position, _dictionary, rangeEnd) <= 0 && IsThaiSuffix(uc = Current32(text, position)))
                {
                    if (uc == ThaiPaiyannoi)
                    {
                        if (!IsThaiSuffix(Previous32(text, ref position)))
                        {
                            // Skip over previous end and PAIYANNOI
                            Next32(text, ref position);
                            var paiyannoiIndex = position;
                            Next32(text, ref position);
                            cuWordLength += position - paiyannoiIndex; // Add PAIYANNOI to word
                            uc = Current32(text, position); // Fetch next character
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
                            cuWordLength += position - maiyamokIndex; // Add MAIYAMOK to word
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
                breaks.Add(current + cuWordLength);
            }
        }

        // Don't return a break for the end of the dictionary range if there is one there
        if (breaks.Count > firstBreak && breaks[^1] >= rangeEnd)
        {
            breaks.RemoveAt(breaks.Count - 1);
        }
    }

    // The candidates of the word at the current position that are followed by the most dictionary words (the gotos of ICU)
    private void FindBest(ReadOnlySpan<char> text, ref int position, PossibleWord[] words, int wordsFound, int rangeEnd)
    {
        // If we're already at the end of the range, we're done
        if (position >= rangeEnd)
            return;

        do
        {
            if (words[(wordsFound + 1) % Lookahead].Candidates(text, ref position, _dictionary, rangeEnd) > 0)
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
                    if (words[(wordsFound + 2) % Lookahead].Candidates(text, ref position, _dictionary, rangeEnd) > 0)
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
        UnicodeScript.Thai => codePoint is not (0x0E31 or (>= 0x0E40 and <= 0x0E44)) && Handles(codePoint),
        UnicodeScript.Lao => codePoint is not (>= 0x0EC0 and <= 0x0EC4) && Handles(codePoint),
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

    // The iteration of UText over the whole text (utext_current32, utext_next32, utext_previous32, utext_moveIndex32)
    private static int Current32(ReadOnlySpan<char> text, int position) => position < text.Length ? GraphemeClusters.ReadCodePoint(text, position, out _) : -1;

    private static int Next32(ReadOnlySpan<char> text, ref int position)
    {
        if (position >= text.Length)
            return -1;

        var codePoint = GraphemeClusters.ReadCodePoint(text, position, out var length);
        position += length;
        return codePoint;
    }

    private static int Previous32(ReadOnlySpan<char> text, ref int position)
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

    private static int MoveIndex32(ReadOnlySpan<char> text, int position, int count)
    {
        for (var i = 0; i < count && position < text.Length; i++)
        {
            Next32(text, ref position);
        }

        return position;
    }

    /// <summary>The candidate words at a position of the text (<c>PossibleWord</c> of ICU).</summary>
    private sealed class PossibleWord
    {
        private readonly int[] _lengths = new int[MaxCandidates]; // in increasing length order
        private int _count;
        private int _offset = -1;
        private int _mark;
        private int _current;

        /// <summary>The longest prefix this candidate location shares with a dictionary word (in characters).</summary>
        public int LongestPrefix { get; private set; }

        /// <summary>The length of the marked word.</summary>
        public int MarkedLength => _lengths[_mark];

        /// <summary>Fills the list of candidates if needed, selects the longest, and returns the number found.</summary>
        public int Candidates(ReadOnlySpan<char> text, ref int position, WordDictionary dictionary, int rangeEnd)
        {
            var start = position;
            if (start != _offset)
            {
                _offset = start;
                _count = dictionary.Matches(text, start, rangeEnd - start, _lengths, out var prefix);
                LongestPrefix = prefix;
            }

            if (_count > 0)
            {
                position = start + _lengths[_count - 1];
            }

            _current = _count - 1;
            _mark = _current;
            return _count;
        }

        /// <summary>Selects the marked candidate, moves the position after it, and returns its length.</summary>
        public int AcceptMarked(ref int position)
        {
            position = _offset + _lengths[_mark];
            return _lengths[_mark];
        }

        /// <summary>Backs up from the current candidate to the next shorter one, and moves the position after it; returns whether it exists.</summary>
        public bool BackUp(ref int position)
        {
            if (_current > 0)
            {
                _current--;
                position = _offset + _lengths[_current];
                return true;
            }

            return false;
        }

        /// <summary>Marks the current candidate as the one we like.</summary>
        public void MarkCurrent() => _mark = _current;
    }
}
