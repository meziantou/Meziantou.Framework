namespace Meziantou.Framework.WebPlatform.Encodings;

internal sealed partial class LegacyEncodingDetector
{
    // The scores of the characters of the multi-byte encodings, calibrated by chardetng relative to each other and to the
    // single-byte encodings
    private const int CjkBaseScore = 41;
    private const int CjkSecondaryBaseScore = 20;
    private const int CjkLatinAdjacencyPenalty = -CjkBaseScore;
    private const int CjPunctuationScore = CjkBaseScore / 2;
    private const int CjkOtherScore = CjkSecondaryBaseScore / 4;
    private const int HalfWidthKatakanaScore = 1;
    private const int HalfWidthKatakanaVoicingScore = 10;

    private const int GbkLevel1Score = CjkBaseScore;
    private const int GbkLevel2Score = CjkSecondaryBaseScore;
    private const int GbkNonEucScore = CjkSecondaryBaseScore / 4;
    private const int GbkPuaPenalty = -(CjkBaseScore * 10);
    private const int GbkSingleByteExtensionPenalty = GbkPuaPenalty * 4;

    private const int ShiftJisKanaScore = 20;
    private const int ShiftJisLevel1KanjiScore = CjkBaseScore;
    private const int ShiftJisLevel2KanjiScore = CjkSecondaryBaseScore;
    private const int ShiftJisInitialHalfWidthKatakanaPenalty = -75;
    private const int ShiftJisPuaPenalty = -(CjkBaseScore * 10);
    private const int ShiftJisExtensionPenalty = ShiftJisPuaPenalty * 2;

    private const int EucJPKanaScore = CjkBaseScore + (CjkBaseScore / 3);
    private const int EucJPNearObsoleteKanaScore = CjkBaseScore - 1;
    private const int EucJPLevel1KanjiScore = CjkBaseScore;
    private const int EucJPLevel2KanjiScore = CjkSecondaryBaseScore;
    private const int EucJPOtherKanjiScore = CjkSecondaryBaseScore / 4;
    private const int EucJPInitialKanaPenalty = -((CjkBaseScore / 3) + 1);

    // More severe than the one of Shift_JIS, not to detect EUC-KR as EUC-JP
    private const int EucJPExtensionPenalty = -(CjkBaseScore * 50);

    private const int Big5Level1HanziScore = CjkBaseScore;
    private const int Big5OtherHanziScore = CjkSecondaryBaseScore;

    // More severe than the other private use penalties, not to detect EUC-KR as Big5
    private const int Big5PuaPenalty = -(CjkBaseScore * 30);
    private const int Big5SingleByteExtensionPenalty = -(CjkBaseScore * 40);

    private const int EucKREucHangulScore = CjkBaseScore + 1;
    private const int EucKRNonEucHangulScore = CjkSecondaryBaseScore / 5;
    private const int EucKRHanjaScore = CjkSecondaryBaseScore / 2;
    private const int EucKRHanjaAfterHangulPenalty = -(CjkBaseScore * 10);
    private const int EucKRLongWordPenalty = -6;

    // One less than GBK: GBK wins when the scores are equal
    private const int EucKRPuaPenalty = GbkPuaPenalty - 1;
    private const int EucKRMacKoreanPenalty = EucKRPuaPenalty * 2;

    /// <summary>The kind of the previous character: an ASCII letter next to an ideograph or a syllable is penalized.</summary>
    private enum PreviousCharacter : byte
    {
        Other,
        AsciiLetter,

        /// <summary>An ideograph, a kana, or a hangul syllable.</summary>
        Cjk,

        /// <summary>A hanja (EUC-KR).</summary>
        Hanja,
    }

    /// <summary>Whether the previous half-width katakana can be followed by a voicing mark.</summary>
    private enum HalfWidthKatakana : byte
    {
        DakutenForbidden,
        DakutenAllowed,
        DakutenOrHandakutenAllowed,
    }

    /// <summary>
    /// A multi-byte encoding: each byte is given to the handler of the decoder of the Encoding Standard, and the characters
    /// and the errors are scored. As for chardetng, a byte that ends an invalid sequence is not processed again (the
    /// decoder would restore an ASCII byte), and the sequences of the extensions of an encoding that the decoder does not
    /// support (the user-defined characters, the encodings of Mac OS) are penalized rather than disqualifying.
    /// </summary>
    private abstract class MultiByteCandidate(WebDecoder decoder) : Candidate
    {
        private bool _hasPendingBytes;

        // A character whose lead byte is also a common byte of windows-1252 text (a letter of the Vietnamese or of the
        // Central European encodings...) and that follows a character that is not CJK: its score is only counted if the
        // next character is not an ASCII one
        private long? _pendingScore;

        protected PreviousCharacter Previous { get; set; }

        protected byte PreviousByte { get; private set; }

        protected byte PreviousPreviousByte { get; private set; }

        public sealed override bool Feed(ReadOnlySpan<byte> buffer, bool last)
        {
            var score = 0L;
            foreach (var b in buffer)
            {
                var hadPendingBytes = _hasPendingBytes;
                var result = decoder.HandleSingle(b, out var codePoint, out _, out var restoredCount);
                _hasPendingBytes = result == WebDecoder.HandlerResult.Continue;
                if (result is WebDecoder.HandlerResult.CodePoint or WebDecoder.HandlerResult.TwoCodePoints)
                {
                    ScoreCharacter(codePoint, isSequence: result == WebDecoder.HandlerResult.TwoCodePoints, b, ref score);
                }
                else if (result == WebDecoder.HandlerResult.Error)
                {
                    // The error is a lone byte when the decoder had no pending byte, or when it restores the bytes that follow
                    // the invalid one
                    if (!ScoreError(b, isLoneByte: !hadPendingBytes || restoredCount > 0, ref score))
                        return false;
                }

                PreviousPreviousByte = PreviousByte;
                PreviousByte = b;
            }

            // An incomplete sequence at the end of the stream
            if (last && decoder.HandleSingle(-1, out _, out _, out _) == WebDecoder.HandlerResult.Error)
                return false;

            Score += score;
            return true;
        }

        /// <summary>Scores a decoded character: <paramref name="isSequence"/> is true for the first of two code points.</summary>
        protected abstract void ScoreCharacter(int codePoint, bool isSequence, byte b, ref long score);

        /// <summary>Scores an invalid byte sequence. Returns false when it disqualifies the encoding.</summary>
        protected abstract bool ScoreError(byte b, bool isLoneByte, ref long score);

        protected void ResetDecoder()
        {
            decoder.Reset();
            _hasPendingBytes = false;
        }

        protected static bool IsAsciiLetter(int value) => value is (>= 'a' and <= 'z') or (>= 'A' and <= 'Z');

        // The ideographs, with the compatibility ideographs
        protected static bool IsIdeograph(int codePoint) => codePoint is (>= 0x3400 and < 0xA000) or (>= 0xF900 and < 0xFB00);

        protected static bool IsPrivateUse(int codePoint) => codePoint is >= 0xE000 and < 0xF900;

        // The space, the comma, the full stop and the parentheses that Korean text does not use
        protected static bool IsCjPunctuation(int codePoint) => codePoint is 0x3000 or 0x3001 or 0x3002 or 0xFF08 or 0xFF09;

        // The exclamation mark, the comma, the semicolon and the question mark that Japanese text does not use
        protected static bool IsChinesePunctuation(int codePoint) => IsCjPunctuation(codePoint) || codePoint is 0xFF01 or 0xFF0C or 0xFF1B or 0xFF1F;

        // The lead bytes of Shift_JIS and Big5 that are common in the text of the single-byte encodings
        protected static bool IsProblematicLead(byte b) => b is (>= 0x91 and <= 0x97) or 0x9A or 0x8A or 0x9B or 0x8B or 0x9E or 0x8E or 0xB0;

        // The ones of GBK and EUC-KR
        protected static bool IsMoreProblematicLead(byte b) => IsProblematicLead(b) || b is 0x82 or 0x84 or 0x85 or 0xA0;

        /// <summary>The bonus of the most frequent characters of a language: from 8 for the first ones to 1.</summary>
        protected static int GetFrequencyScore(int codePoint, ReadOnlySpan<ushort> frequentCharacters)
        {
            var index = codePoint <= char.MaxValue ? frequentCharacters.IndexOf((ushort)codePoint) : -1;
            return index < 0 ? 0 : (frequentCharacters.Length - index) / 16;
        }

        protected void CommitPendingScore(ref long score)
        {
            score += _pendingScore.GetValueOrDefault();
            _pendingScore = null;
        }

        protected void DiscardPendingScore() => _pendingScore = null;

        /// <summary>Returns the score, or makes it pending (see <see cref="_pendingScore"/>) and returns 0.</summary>
        protected long GetScoreOrSetPending(long score, bool isProblematicLead)
        {
            if (Previous is PreviousCharacter.Cjk || !isProblematicLead)
                return score;

            _pendingScore = score;
            return 0;
        }

        /// <summary>A CJK character: it is penalized after an ASCII letter.</summary>
        protected void SetPreviousCjk(ref long score)
        {
            if (Previous == PreviousCharacter.AsciiLetter)
            {
                score += CjkLatinAdjacencyPenalty;
            }

            Previous = PreviousCharacter.Cjk;
        }

        /// <summary>An ASCII letter: it is penalized after a CJK character, and the pending score is discarded.</summary>
        protected void SetPreviousAsciiLetter(ref long score)
        {
            DiscardPendingScore();
            if (Previous is PreviousCharacter.Cjk or PreviousCharacter.Hanja)
            {
                score += CjkLatinAdjacencyPenalty;
            }

            Previous = PreviousCharacter.AsciiLetter;
        }

        /// <summary>Scores a half-width katakana (U+FF61 to U+FF9F): a voicing mark is implausible after a character that it cannot follow.</summary>
        protected static int GetHalfWidthKatakanaScore(int codePoint, HalfWidthKatakana previousState, out HalfWidthKatakana state)
        {
            state = HalfWidthKatakana.DakutenForbidden;
            if (codePoint is (>= 0xFF76 and <= 0xFF84) or 0xFF73)
            {
                state = HalfWidthKatakana.DakutenAllowed;
            }
            else if (codePoint is >= 0xFF8A and <= 0xFF8E)
            {
                state = HalfWidthKatakana.DakutenOrHandakutenAllowed;
            }
            else if (codePoint == 0xFF9E)
            {
                return HalfWidthKatakanaScore + (previousState == HalfWidthKatakana.DakutenForbidden ? ImplausibilityPenalty : HalfWidthKatakanaVoicingScore);
            }
            else if (codePoint == 0xFF9F)
            {
                return HalfWidthKatakanaScore + (previousState != HalfWidthKatakana.DakutenOrHandakutenAllowed ? ImplausibilityPenalty : HalfWidthKatakanaVoicingScore);
            }

            return HalfWidthKatakanaScore;
        }
    }

    private sealed class GbkCandidate() : MultiByteCandidate(new Gb18030Decoder(fatal: true))
    {
        public override WebEncodingId Encoding => WebEncodingId.Gbk;

        protected override void ScoreCharacter(int codePoint, bool isSequence, byte b, ref long score)
        {
            if (codePoint > char.MaxValue)
            {
                CommitPendingScore(ref score);
                if (codePoint >= 0xF0000)
                {
                    // The private use planes
                    score += GbkPuaPenalty;
                    Previous = PreviousCharacter.Other;
                }
                else if (codePoint < 0x30000)
                {
                    // The ideographs of the supplementary planes
                    score += GbkNonEucScore;
                    SetPreviousCjk(ref score);
                }
                else
                {
                    score += CjkOtherScore;
                    Previous = PreviousCharacter.Other;
                }
            }
            else if (IsAsciiLetter(codePoint))
            {
                SetPreviousAsciiLetter(ref score);
            }
            else if (codePoint == 0x20AC)
            {
                // The euro sign is the byte 0x80
                DiscardPendingScore();
                Previous = PreviousCharacter.Other;
            }
            else if (codePoint is >= 0x4E00 and <= 0x9FA5)
            {
                CommitPendingScore(ref score);
                if (b is >= 0xA1 and <= 0xFE)
                {
                    // The lead byte tells the level of the ideographs of GB2312; the others are extensions of GBK
                    score += PreviousByte switch
                    {
                        >= 0xA1 and <= 0xD7 => GbkLevel1Score + GetFrequencyScore(codePoint, EncodingDetectorData.FrequentSimplified),
                        >= 0xD8 and <= 0xFE => GbkLevel2Score,
                        _ => GbkNonEucScore,
                    };
                }
                else
                {
                    score += GetScoreOrSetPending(GbkNonEucScore, IsMoreProblematicLead(PreviousByte));
                }

                SetPreviousCjk(ref score);
            }
            else if (IsIdeograph(codePoint))
            {
                CommitPendingScore(ref score);
                SetPreviousCjk(ref score);
            }
            else if (IsPrivateUse(codePoint))
            {
                CommitPendingScore(ref score);

                // The ideographs that GB18030-2005 maps to the private use area
                if (codePoint is (>= 0xE78D and <= 0xE796) or (>= 0xE816 and <= 0xE818) or 0xE81E or 0xE826 or 0xE82B or 0xE82C or 0xE831 or 0xE832 or 0xE83B or 0xE843 or 0xE854 or 0xE855 or 0xE864)
                {
                    score += GbkNonEucScore;
                    SetPreviousCjk(ref score);
                }
                else
                {
                    score += GbkPuaPenalty;
                    Previous = PreviousCharacter.Other;
                }
            }
            else
            {
                if (codePoint < 0x80)
                {
                    DiscardPendingScore();
                }
                else
                {
                    CommitPendingScore(ref score);
                    score += IsChinesePunctuation(codePoint) ? CjPunctuationScore : CjkOtherScore;
                }

                Previous = PreviousCharacter.Other;
            }
        }

        protected override bool ScoreError(byte b, bool isLoneByte, ref long score)
        {
            // The single bytes of the Simplified Chinese encoding of Mac OS: the ones that are lead bytes of GBK, followed by
            // an ASCII byte or by 0xFF, and 0xFF
            if (PreviousByte is 0xA0 or 0xFE or 0xFD && (b < 0x80 || b == 0xFF))
            {
                score += GbkSingleByteExtensionPenalty;
                if (IsAsciiLetter(b))
                {
                    Previous = PreviousCharacter.AsciiLetter;
                }
                else
                {
                    if (b == 0xFF)
                    {
                        score += GbkSingleByteExtensionPenalty;
                    }

                    Previous = PreviousCharacter.Other;
                }
            }
            else if (isLoneByte && b == 0xFF)
            {
                score += GbkSingleByteExtensionPenalty;
                Previous = PreviousCharacter.Other;
            }
            else
            {
                return false;
            }

            DiscardPendingScore();
            ResetDecoder();
            return true;
        }
    }

    private sealed class ShiftJisCandidate() : MultiByteCandidate(new ShiftJisDecoder(fatal: true))
    {
        private bool _halfWidthKatakanaSeen;
        private HalfWidthKatakana _halfWidthKatakanaState;

        public override WebEncodingId Encoding => WebEncodingId.ShiftJis;

        protected override void ScoreCharacter(int codePoint, bool isSequence, byte b, ref long score)
        {
            var halfWidthKatakanaState = _halfWidthKatakanaState;
            _halfWidthKatakanaState = HalfWidthKatakana.DakutenForbidden;
            if (IsAsciiLetter(codePoint))
            {
                SetPreviousAsciiLetter(ref score);
            }
            else if (codePoint is >= 0xFF61 and <= 0xFF9F)
            {
                if (!_halfWidthKatakanaSeen)
                {
                    // A single byte per character: the inputs that are as short as a title are not detected as Shift_JIS
                    _halfWidthKatakanaSeen = true;
                    score += ShiftJisInitialHalfWidthKatakanaPenalty;
                }

                DiscardPendingScore();
                score += GetHalfWidthKatakanaScore(codePoint, halfWidthKatakanaState, out _halfWidthKatakanaState);
                SetPreviousCjk(ref score);
            }
            else if (codePoint is >= 0x3040 and < 0x3100)
            {
                CommitPendingScore(ref score);
                score += ShiftJisKanaScore;
                SetPreviousCjk(ref score);
            }
            else if (IsIdeograph(codePoint))
            {
                CommitPendingScore(ref score);

                // The lead byte tells the level of the kanji of JIS X 0208
                var isLevel1 = PreviousByte < 0x98 || (PreviousByte == 0x98 && b < 0x73);
                score += GetScoreOrSetPending(
                    isLevel1 ? ShiftJisLevel1KanjiScore + GetFrequencyScore(codePoint, EncodingDetectorData.FrequentKanji) : ShiftJisLevel2KanjiScore,
                    IsProblematicLead(PreviousByte));
                SetPreviousCjk(ref score);
            }
            else if (IsPrivateUse(codePoint))
            {
                CommitPendingScore(ref score);
                score += ShiftJisPuaPenalty;
                Previous = PreviousCharacter.Other;
            }
            else
            {
                if (codePoint < 0x80)
                {
                    DiscardPendingScore();
                }
                else if (codePoint == 0x80)
                {
                    // The byte 0x80 is a control character for the decoder: it is the euro sign of windows-1252
                    DiscardPendingScore();
                    score += ImplausibilityPenalty;
                }
                else
                {
                    CommitPendingScore(ref score);
                    score += IsCjPunctuation(codePoint) ? CjPunctuationScore : CjkOtherScore;
                }

                Previous = PreviousCharacter.Other;
            }
        }

        protected override bool ScoreError(byte b, bool isLoneByte, ref long score)
        {
            var lead = PreviousByte;
            if (lead is (>= 0x81 and <= 0x9F) or (>= 0xE0 and <= 0xFC) && b is (>= 0x40 and <= 0x7E) or (>= 0x80 and <= 0xFC) &&
                !((lead == 0x82 && b >= 0xFA) || (lead == 0x84 && (b is (>= 0xDD and <= 0xE4) or >= 0xFB)) || (lead == 0x86 && b is >= 0xF2 and <= 0xFA) || (lead == 0x87 && b is >= 0x77 and <= 0x7D) || (lead == 0xFC && b >= 0xF5)))
            {
                // A character of Shift_JIS-2004 or of the Japanese encoding of Mac OS: approximately a symbol before the lead
                // byte 0x87, else a kanji
                CommitPendingScore(ref score);
                score += ShiftJisExtensionPenalty;
                if (lead < 0x87)
                {
                    Previous = PreviousCharacter.Other;
                }
                else
                {
                    SetPreviousCjk(ref score);
                }

                return true;
            }

            if (isLoneByte && (b == 0xA0 || b >= 0xFD))
            {
                // The single bytes of the Japanese encoding of Mac OS
                DiscardPendingScore();
                score += ShiftJisExtensionPenalty;
                Previous = PreviousCharacter.Other;
                return true;
            }

            return false;
        }
    }

    private sealed class EucJPCandidate() : MultiByteCandidate(new EucJPDecoder(fatal: true))
    {
        private bool _nonAsciiSeen;
        private HalfWidthKatakana _halfWidthKatakanaState;

        public override WebEncodingId Encoding => WebEncodingId.EucJP;

        protected override void ScoreCharacter(int codePoint, bool isSequence, byte b, ref long score)
        {
            var halfWidthKatakanaState = _halfWidthKatakanaState;
            _halfWidthKatakanaState = HalfWidthKatakana.DakutenForbidden;
            if (!_nonAsciiSeen && codePoint >= 0x80)
            {
                _nonAsciiSeen = true;
                if (codePoint is >= 0x3040 and < 0x3100)
                {
                    // An initial kana has no advantage over an initial ideograph of Big5
                    score += EucJPInitialKanaPenalty;
                }
            }

            if (IsAsciiLetter(codePoint))
            {
                SetPreviousAsciiLetter(ref score);
            }
            else if (codePoint is >= 0xFF61 and <= 0xFF9F)
            {
                score += GetHalfWidthKatakanaScore(codePoint, halfWidthKatakanaState, out _halfWidthKatakanaState);
                if (Previous == PreviousCharacter.AsciiLetter)
                {
                    score += CjkLatinAdjacencyPenalty;
                }

                Previous = PreviousCharacter.Other;
            }
            else if (codePoint is (>= 0x3041 and <= 0x3093) or (>= 0x30A1 and <= 0x30F6))
            {
                // The kana "wi" and "we" are nearly obsolete: they have no advantage over the ideographs of Big5
                score += codePoint is 0x3090 or 0x3091 or 0x30F0 or 0x30F1 ? EucJPNearObsoleteKanaScore : EucJPKanaScore;
                SetPreviousCjk(ref score);
            }
            else if (IsIdeograph(codePoint))
            {
                // The lead byte 0x8F is the one of JIS X 0212; else the lead byte tells the level of the kanji of JIS X 0208
                if (PreviousPreviousByte == 0x8F)
                {
                    score += EucJPOtherKanjiScore;
                }
                else if (PreviousByte < 0xD0)
                {
                    score += EucJPLevel1KanjiScore + GetFrequencyScore(codePoint, EncodingDetectorData.FrequentKanji);
                }
                else
                {
                    score += EucJPLevel2KanjiScore;
                }

                SetPreviousCjk(ref score);
            }
            else
            {
                if (codePoint >= 0x80)
                {
                    score += IsCjPunctuation(codePoint) ? CjPunctuationScore : CjkOtherScore;
                }

                Previous = PreviousCharacter.Other;
            }
        }

        protected override bool ScoreError(byte b, bool isLoneByte, ref long score)
        {
            // A pair of bytes that the encoding does not map (an extension), except the unmapped pairs that are common in the
            // other encodings
            var lead = PreviousByte;
            if (b is >= 0xA1 and <= 0xFE && lead is >= 0xA1 and <= 0xFE &&
                (PreviousPreviousByte == 0x8F
                    ? lead is not (0xA2 or 0xA6 or 0xA7 or 0xA9 or 0xAA or 0xAB or 0xED) && !(lead == 0xFE && b >= 0xF7)
                    : !(lead == 0xA8 && b is >= 0xDF and <= 0xE6) && !(lead == 0xAC && b is >= 0xF4 and <= 0xFC) && !(lead == 0xAD && b is >= 0xD8 and <= 0xDE)))
            {
                score += EucJPExtensionPenalty;
                SetPreviousCjk(ref score);
                return true;
            }

            return false;
        }
    }

    private sealed class Big5Candidate() : MultiByteCandidate(new Big5Decoder(fatal: true))
    {
        public override WebEncodingId Encoding => WebEncodingId.Big5;

        protected override void ScoreCharacter(int codePoint, bool isSequence, byte b, ref long score)
        {
            if (isSequence)
            {
                // A Latin letter with a combining mark
                CommitPendingScore(ref score);
                score += CjkOtherScore;
                Previous = PreviousCharacter.Other;
            }
            else if (codePoint > char.MaxValue)
            {
                // The ideographs of the supplementary planes
                CommitPendingScore(ref score);
                score += GetScoreOrSetPending(Big5OtherHanziScore, IsProblematicLead(PreviousByte));
                SetPreviousCjk(ref score);
            }
            else if (IsAsciiLetter(codePoint))
            {
                SetPreviousAsciiLetter(ref score);
            }
            else if (IsIdeograph(codePoint))
            {
                CommitPendingScore(ref score);

                // The lead byte tells the level of the ideographs
                score += GetScoreOrSetPending(PreviousByte is >= 0xA4 and <= 0xC6 ? Big5Level1HanziScore : Big5OtherHanziScore, IsProblematicLead(PreviousByte));
                SetPreviousCjk(ref score);
            }
            else
            {
                if (codePoint < 0x80)
                {
                    DiscardPendingScore();
                }
                else
                {
                    CommitPendingScore(ref score);
                    score += IsChinesePunctuation(codePoint) ? CjPunctuationScore : CjkOtherScore;
                }

                Previous = PreviousCharacter.Other;
            }
        }

        protected override bool ScoreError(byte b, bool isLoneByte, ref long score)
        {
            if (PreviousByte is >= 0x81 and <= 0xFE && b is (>= 0x40 and <= 0x7E) or (>= 0xA1 and <= 0xFE))
            {
                // A pair of bytes that the encoding does not map: a user-defined character, or a character of an extension
                // (Big5-UAO...), which is assumed to be an ideograph
                CommitPendingScore(ref score);
                score += Big5PuaPenalty;
                SetPreviousCjk(ref score);
            }
            else if (PreviousByte is 0xA0 or 0xFD or 0xFE && (b < 0x80 || b == 0xFF))
            {
                // The single bytes of the Traditional Chinese encoding of Mac OS, as for GBK
                DiscardPendingScore();
                score += Big5SingleByteExtensionPenalty;
                if (IsAsciiLetter(b))
                {
                    Previous = PreviousCharacter.AsciiLetter;
                }
                else
                {
                    if (b == 0xFF)
                    {
                        score += Big5SingleByteExtensionPenalty;
                    }

                    Previous = PreviousCharacter.Other;
                }
            }
            else if (isLoneByte && b == 0xFF)
            {
                DiscardPendingScore();
                score += Big5SingleByteExtensionPenalty;
                Previous = PreviousCharacter.Other;
            }
            else
            {
                return false;
            }

            return true;
        }
    }

    private sealed class EucKRCandidate() : MultiByteCandidate(new EucKRDecoder(fatal: true))
    {
        private long _currentWordLength;

        public override WebEncodingId Encoding => WebEncodingId.EucKR;

        // The bytes of the characters of KS X 1001; the other ones are the extension of windows-949
        private static bool IsEucByte(byte b) => b is >= 0xA1 and <= 0xFE;

        protected override void ScoreCharacter(int codePoint, bool isSequence, byte b, ref long score)
        {
            if (IsAsciiLetter(codePoint))
            {
                SetPreviousAsciiLetter(ref score);
                _currentWordLength = 0;
            }
            else if (codePoint is >= 0xAC00 and <= 0xD7A3)
            {
                CommitPendingScore(ref score);
                if (IsEucByte(PreviousByte) && IsEucByte(b))
                {
                    score += EucKREucHangulScore + GetFrequencyScore(codePoint, EncodingDetectorData.FrequentHangul);
                }
                else
                {
                    score += GetScoreOrSetPending(EucKRNonEucHangulScore, IsMoreProblematicLead(PreviousByte));
                }

                SetPreviousCjk(ref score);
                AddWordCharacter(ref score);
            }
            else if (codePoint is (>= 0x4E00 and < 0xAC00) or (>= 0xF900 and <= 0xFA0B))
            {
                CommitPendingScore(ref score);
                score += EucKRHanjaScore;
                SetPreviousHanja(ref score);
            }
            else
            {
                if (codePoint >= 0x80)
                {
                    CommitPendingScore(ref score);
                    score += CjkOtherScore;
                }
                else
                {
                    DiscardPendingScore();
                }

                Previous = PreviousCharacter.Other;
                _currentWordLength = 0;
            }
        }

        // Korean text has spaces between the words, unlike the text of the other encodings decoded as EUC-KR
        private void AddWordCharacter(ref long score)
        {
            _currentWordLength++;
            if (_currentWordLength > 5)
            {
                score += EucKRLongWordPenalty;
            }
        }

        // A hanja does not follow a hangul syllable in the same word
        private void SetPreviousHanja(ref long score)
        {
            score += Previous switch
            {
                PreviousCharacter.AsciiLetter => CjkLatinAdjacencyPenalty,
                PreviousCharacter.Cjk => EucKRHanjaAfterHangulPenalty,
                _ => 0,
            };
            Previous = PreviousCharacter.Hanja;
            AddWordCharacter(ref score);
        }

        protected override bool ScoreError(byte b, bool isLoneByte, ref long score)
        {
            var lead = PreviousByte;
            if (lead is 0xC9 or 0xFE && IsEucByte(b))
            {
                // The user-defined characters of windows-949, which are assumed to be hanja
                CommitPendingScore(ref score);
                score += EucKRPuaPenalty;
                SetPreviousHanja(ref score);
            }
            else if (lead is 0xA1 or (>= 0xA3 and <= 0xA8) or (>= 0xAA and <= 0xAD) && b is >= 0x7B and <= 0x7D)
            {
                // The symbols of the Korean encoding of Mac OS that are not in windows-949
                CommitPendingScore(ref score);
                score += EucKRMacKoreanPenalty;
                Previous = PreviousCharacter.Other;
                _currentWordLength = 0;
            }
            else if (lead is >= 0x81 and <= 0x84 && (b <= 0x80 || b == 0xFF))
            {
                // The single bytes of the Korean encoding of Mac OS that are lead bytes of windows-949, followed by an ASCII
                // byte or by another single byte of the encoding
                DiscardPendingScore();
                score += EucKRMacKoreanPenalty;
                if (IsAsciiLetter(b))
                {
                    Previous = PreviousCharacter.AsciiLetter;
                }
                else
                {
                    if (b is 0x80 or 0xFF)
                    {
                        score += EucKRMacKoreanPenalty;
                    }

                    Previous = PreviousCharacter.Other;
                }

                _currentWordLength = 0;
            }
            else if (isLoneByte && b is 0x80 or 0xFF)
            {
                DiscardPendingScore();
                score += EucKRMacKoreanPenalty;
                Previous = PreviousCharacter.Other;
                _currentWordLength = 0;
            }
            else
            {
                return false;
            }

            return true;
        }
    }
}
