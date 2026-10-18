namespace Meziantou.Framework.WebPlatform.Unicode;

/// <summary>
/// The phrase segmentation of Japanese (the phrase boundaries of <c>word-break: auto-phrase</c> and
/// <c>word-space-transform: auto-phrase</c>, https://drafts.csswg.org/css-text-4/#word-break-property): a port of the machine
/// learning break engine of ICU (<c>MlBreakEngine</c> of <c>icu4c/source/common/mlbe.cpp</c>, ICU 78, used by Chrome for
/// <c>lw=phrase</c>) with its model (<c>jaml.txt</c>, a model of BudouX, https://github.com/google/budoux, in
/// <c>JapanesePhraseBreaker.Model.g.cs</c>).
/// </summary>
/// <remarks>
/// A boundary is evaluated from the characters around it (one, two or three characters at each position from three
/// characters before it to three characters after it): it is a phrase boundary when the sum of the scores of the features is
/// greater than half of the sum of all the scores of the model. As in ICU, the model is applied to the runs of kanji and kana
/// (<see cref="IsPhraseCharacter"/>); unlike ICU, the text is not normalized (NFKC) before.
/// </remarks>
internal static partial class JapanesePhraseBreaker
{
    private const int FeatureCount = 13;

    // The indexes of the features in the tables (ModelIndex)
    private const int UnigramStart = 0;
    private const int BigramStart = 6;
    private const int TrigramStart = 9;

    // -(sum of the scores) / 2
    private static readonly int BaseScore = ComputeBaseScore();

    /// <summary>
    /// Whether the characters are segmented into phrases: the kanji, the hiragana and the katakana, and the prolonged sound marks
    /// and the halfwidth (semi-)voiced sound marks (the <c>cjSet</c> of the CJK break engine of ICU).
    /// </summary>
    public static bool IsPhraseCharacter(int codePoint)
    {
        if (codePoint < 0x2E80)
            return false;

        if (codePoint is 0x30FC or 0xFF70 or 0xFF9E or 0xFF9F)
            return true;

        return UnicodeScripts.GetScript(codePoint) is UnicodeScript.Han or UnicodeScript.Hiragana or UnicodeScript.Katakana;
    }

    /// <summary>
    /// Finds the phrase boundaries of a run of characters of <see cref="IsPhraseCharacter"/> (their code points): sets
    /// <c>boundaries[i]</c> when there is a boundary before <c>codePoints[i]</c> (<c>divideUpRange</c> of ICU, without the
    /// boundaries at the start and at the end of the run).
    /// </summary>
    public static void FindBoundaries(ReadOnlySpan<int> codePoints, Span<bool> boundaries)
    {
        boundaries[..codePoints.Length].Clear();
        for (var i = 1; i < codePoints.Length; i++)
        {
            boundaries[i] = Evaluate(codePoints, i) > 0;
        }
    }

    // The score of a boundary before codePoints[i] (evaluateBreakpoint): the unigrams from i - 3 to i + 2, the bigrams from
    // (i - 2, i - 1) to (i, i + 1), and the trigrams from (i - 3 .. i - 1) to (i .. i + 2)
    private static int Evaluate(ReadOnlySpan<int> codePoints, int i)
    {
        var score = BaseScore;
        for (var k = 0; k < 6; k++)
        {
            var index = i - 3 + k;
            if (index >= 0 && index < codePoints.Length)
            {
                score += GetScore(UnigramStart + k, (uint)codePoints[index]);
            }
        }

        for (var k = 0; k < 3; k++)
        {
            var index = i - 2 + k;
            if (index >= 0 && index + 1 < codePoints.Length)
            {
                score += GetScore(BigramStart + k, ((ulong)(uint)codePoints[index] << 21) | (uint)codePoints[index + 1]);
            }
        }

        for (var k = 0; k < 4; k++)
        {
            var index = i - 3 + k;
            if (index >= 0 && index + 2 < codePoints.Length)
            {
                score += GetScore(TrigramStart + k, ((ulong)(uint)codePoints[index] << 42) | ((ulong)(uint)codePoints[index + 1] << 21) | (uint)codePoints[index + 2]);
            }
        }

        return score;
    }

    private static int GetScore(int feature, ulong key)
    {
        var start = FeatureStarts[feature];
        var index = Keys[start..FeatureStarts[feature + 1]].BinarySearch(key);
        return index >= 0 ? Values[start + index] : 0;
    }

    private static int ComputeBaseScore()
    {
        var sum = 0;
        foreach (var value in Values[..FeatureStarts[FeatureCount]])
        {
            sum += value;
        }

        // (loadMLModel: the negative sum is divided by 2, rounded toward zero)
        return -sum / 2;
    }
}
