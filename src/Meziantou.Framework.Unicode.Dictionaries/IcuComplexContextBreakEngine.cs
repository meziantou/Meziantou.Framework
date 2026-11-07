using System.Globalization;

namespace Meziantou.Framework;

/// <summary>
/// The dictionary break engines of ICU for the scripts of Southeast Asia whose characters have the line breaking class
/// Complex_Context (<c>ThaiBreakEngine</c>, <c>LaoBreakEngine</c>, <c>BurmeseBreakEngine</c> and <c>KhmerBreakEngine</c>
/// of <c>icu4c/source/common/dictbe.cpp</c>).
/// </summary>
/// <remarks>
/// The run is divided into the longest dictionary words that are followed by other dictionary words, with up to three
/// words of lookahead. The characters that do not start a dictionary word are attached to the word before them, up to
/// a plausible word start; a break is never before a combining mark, and Thai attaches the elision and repetition
/// characters to the word before them.
/// </remarks>
internal sealed class IcuComplexContextBreakEngine : IcuDictionaryBreakEngine
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

    private IcuComplexContextBreakEngine(UnicodeScript script, IcuWordDictionaryKind dictionaryKind)
        : base(dictionaryKind)
    {
        _script = script;
    }

    public static IcuComplexContextBreakEngine ThaiEngine { get; } = new(UnicodeScript.Thai, IcuWordDictionaryKind.Thai);

    public static IcuComplexContextBreakEngine LaoEngine { get; } = new(UnicodeScript.Lao, IcuWordDictionaryKind.Lao);

    public static IcuComplexContextBreakEngine BurmeseEngine { get; } = new(UnicodeScript.Myanmar, IcuWordDictionaryKind.Burmese);

    public static IcuComplexContextBreakEngine KhmerEngine { get; } = new(UnicodeScript.Khmer, IcuWordDictionaryKind.Khmer);

    // [[:<script>:]&[:LineBreak=SA:]]
    public override bool Handles(int codePoint)
        => (uint)codePoint <= 0x10FFFF && UnicodeProperties.GetLineBreakClass(codePoint) == UnicodeLineBreakClass.ComplexContext && UnicodeScripts.GetScript(codePoint) == _script;

    private protected override int FindBreaksCore(ReadOnlySpan<char> text, Span<int> breakPositions)
    {
        var rangeEnd = text.Length;

        // Not enough characters for two words: Thai counts the code points (and needs one more), the other scripts the code units
        if (_script == UnicodeScript.Thai ? MoveIndex32(text, 0, MinWordSpan) >= rangeEnd : rangeEnd < MinWordSpan)
            return 0;

        var dictionary = IcuWordDictionary.Get(DictionaryKind);
        var breakCount = 0;
        var wordsFound = 0;
        var words = new PossibleWord[Lookahead];
        for (var i = 0; i < words.Length; i++)
        {
            words[i] = new PossibleWord();
        }

        var position = 0;
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

            // Did we find a word on this iteration? If so, push it on the break stack. Don't return a break for the end of
            // the dictionary range: the breaks increase, and the loop ends after a break at the end of the range.
            if (cuWordLength > 0 && current + cuWordLength < rangeEnd)
            {
                AddBreak(breakPositions, ref breakCount, current + cuWordLength);
            }
        }

        return breakCount;
    }

    // The candidates of the word at the current position that are followed by the most dictionary words (the gotos of ICU)
    private static void MarkBestCandidate(ReadOnlySpan<char> text, ref int position, IcuWordDictionary dictionary, PossibleWord[] words, int wordsFound, int rangeEnd)
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
        public int Candidates(ReadOnlySpan<char> text, ref int position, IcuWordDictionary dictionary, int rangeEnd)
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
