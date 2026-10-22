namespace Meziantou.Framework;

/// <summary>
/// The dictionary break engine of ICU for Chinese and Japanese (<c>CjkBreakEngine</c> of <c>icu4c/source/common/dictbe.cpp</c>,
/// without the phrase breaking): the segmentation of the run whose words have the smallest total cost.
/// </summary>
/// <remarks>
/// The run is normalized to NFKC. The cost of a word is its value in the word list; a character that does not start a
/// word of one character is a word of the highest cost (255); and the first character of a run of katakana starts a
/// word that is the whole run, with a cost that depends on its length.
/// </remarks>
internal sealed class IcuCjkBreakEngine : IcuDictionaryBreakEngine
{
    private const int MaxWordSize = 20;
    private const int MaxKatakanaLength = 8;
    private const int MaxKatakanaGroupLength = 20;
    private const uint MaxSnlp = 255;

    private IcuCjkBreakEngine()
        : base(IcuWordDictionaryKind.ChineseJapanese)
    {
    }

    public static IcuCjkBreakEngine Instance { get; } = new();

    // The cost of a run of katakana by length, based on the length frequencies of all the katakana phrases in the dictionary
    private static ReadOnlySpan<uint> KatakanaCosts => [8192, 984, 408, 240, 204, 252, 300, 372, 480];

    // [[:Han:][:Hiragana:][:Katakana:]ーｰﾞﾟ]
    public override bool Handles(int codePoint)
    {
        if ((uint)codePoint > 0x10FFFF)
            return false;

        return codePoint is 0x30FC or 0xFF70 or 0xFF9E or 0xFF9F || UnicodeScripts.GetScript(codePoint) is UnicodeScript.Han or UnicodeScript.Hiragana or UnicodeScript.Katakana;
    }

    private protected override int FindBreaksCore(ReadOnlySpan<char> text, Span<int> breakPositions)
    {
        // The text of the run, normalized to NFKC; inputMap[index in inString] is the corresponding index in the text
        // (null when it is the same index)
        var inString = text;
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
                var codePoint = IcuWordDictionary.DecodeAt(inString, sourceIndex, out var length);
                while (true)
                {
                    AppendCodePoint(fragment, codePoint);
                    sourceIndex += length;
                    if (sourceIndex == inString.Length)
                        break;

                    codePoint = IcuWordDictionary.DecodeAt(inString, sourceIndex, out length);
                    if (HasNfkcBoundaryBefore(codePoint))
                        break;
                }

                normalizedInput.Append(UnicodeNormalization.Normalize(fragment.ToString(), NormalizationForm.FormKC));

                // Map every position in the normalized chunk to the start of the chunk in the original input
                while (normalizedMap.Count < normalizedInput.Length)
                {
                    normalizedMap.Add(fragmentStart);
                }
            }

            normalizedMap.Add(inString.Length);
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
                    inputMap.Add(codeUnitIndex);
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

        var dictionary = IcuWordDictionary.Get(DictionaryKind);
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
            var codePoint = IcuWordDictionary.DecodeAt(inString, ix, out _);
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
                while (j < inString.Length && katakanaRunLength < MaxKatakanaGroupLength && IsKatakana(IcuWordDictionary.DecodeAt(inString, j, out _)))
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

        // The boundaries of the best segmentation, from the end (t_boundary), with the start of the range
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

        boundaries.Add(0);

        // Convert the positions (indices in the normalized input) back to indices in the text, in increasing order, without
        // the start and the end of the range
        var previousTextPosition = -1;
        var breakCount = 0;
        for (var i = boundaries.Count - 1; i >= 0; i--)
        {
            var codePointPosition = boundaries[i];
            var textPosition = inputMap is not null ? inputMap[codePointPosition] : codePointPosition;

            // Normalization expanded the input text, the dictionary found a boundary within the expansion, giving two
            // boundaries with the same index in the original text: the second is ignored
            if (textPosition > previousTextPosition && textPosition != 0 && textPosition != text.Length)
            {
                AddBreak(breakPositions, ref breakCount, textPosition);
            }

            previousTextPosition = textPosition;
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

        // (an unpaired surrogate has no decomposition)
        if (codePoint is < 0x80 or (>= 0xD800 and <= 0xDFFF))
            return true;

        var decomposition = UnicodeNormalization.Normalize(char.ConvertFromUtf32(codePoint), NormalizationForm.FormKD);
        return UnicodeProperties.GetCanonicalCombiningClass(IcuWordDictionary.DecodeAt(decomposition, 0, out _)) == 0;
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
