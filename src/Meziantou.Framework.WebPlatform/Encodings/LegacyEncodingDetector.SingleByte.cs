namespace Meziantou.Framework.WebPlatform.Encodings;

internal sealed partial class LegacyEncodingDetector
{
    /// <summary>The class of the ASCII letters in the tables of the encodings of the non-Latin scripts.</summary>
    private const int LatinLetter = 1;

    /// <summary>The class of the ASCII digits.</summary>
    private const int AsciiDigit = 100;

    /// <summary>The class of the zero-width non-joiner of windows-1256, which is space-like.</summary>
    private const int Windows1256Zwnj = 2;

    /// <summary>The class of the bytes that are not mapped: they disqualify the encoding.</summary>
    private const int Unmapped = 255;

    /// <summary>How a single-byte encoding scores the bytes: the scripts differ by their case and their direction.</summary>
    private enum SingleByteKind : byte
    {
        /// <summary>The Latin script: the pairs of ASCII letters have no score, the changes of case inside the words are penalized.</summary>
        Latin,

        /// <summary>Cyrillic and Greek: the case of each word gives a bonus or a penalty.</summary>
        NonLatinCased,

        /// <summary>Thai, and Arabic in ISO-8859-6.</summary>
        Caseless,

        /// <summary>windows-1256: Arabic, and the letters of French.</summary>
        ArabicFrench,

        /// <summary>Hebrew in the logical order (windows-1255): the punctuation follows the words.</summary>
        Logical,

        /// <summary>Hebrew in the visual order (ISO-8859-8): the punctuation precedes the words.</summary>
        Visual,
    }

    /// <summary>The tables of the single-byte encodings (windows-1252 has two: Icelandic has its own frequencies).</summary>
    private enum SingleByteTable : byte
    {
        Windows1258,
        Windows1250,
        Iso8859_2,
        Windows1251,
        Koi8U,
        Iso8859_5,
        Ibm866,
        Windows1252,
        Windows1252Icelandic,
        Windows1253,
        Iso8859_7,
        Windows1254,
        Windows1255,
        Iso8859_8,
        Windows1256,
        Iso8859_6,
        Windows1257,
        Iso8859_13,
        Iso8859_4,
        Windows874,
    }

    private enum CaseState : byte
    {
        Space,
        Upper,
        Lower,
        UpperLower,
        AllCaps,
        Mix,
    }

    /// <summary>
    /// The state of the sequences of windows-1252 that the pairs of classes cannot score without breaking the detection of
    /// Romanian: the ordinal indicators of Spanish and Italian (" º ", " ª ", ".ª ", "n.º1", " Mª ", " 3º ", " Xª ", " Nº1"...)
    /// and the copyright sign between spaces.
    /// </summary>
    private enum OrdinalState : byte
    {
        Other,
        Space,
        PeriodAfterN,
        OrdinalExpectingSpace,
        OrdinalExpectingSpaceUndoImplausibility,
        OrdinalExpectingSpaceOrDigit,
        OrdinalExpectingSpaceOrDigitUndoImplausibility,
        UpperN,
        LowerN,
        FeminineAbbreviationStartLetter,
        Digit,
        Roman,
        Copyright,
    }

    private sealed class SingleByteCandidate : Candidate
    {
        private readonly SingleByteKind _kind;
        private readonly SingleByteTable _table;

        // The number of classes of the letters: the ASCII ones (with the space-like class 0), then the non-ASCII ones. The
        // classes above them (the punctuation and the symbols) have no score in the table.
        private readonly int _asciiClasses;
        private readonly int _nonAsciiClasses;
        private readonly bool _isWindows1256;
        private int _previous;
        private CaseState _caseState;
        private bool _previousIsAscii = true;
        private int _previousNonAsciiCount;
        private long _currentWordLength;
        private long _longestWord;
        private bool _previousIsA0;
        private bool _previousIsPunctuation;
        private OrdinalState _ordinalState = OrdinalState.Space;

        public SingleByteCandidate(SingleByteKind kind, SingleByteTable table)
        {
            _kind = kind;
            _table = table;
            _isWindows1256 = table == SingleByteTable.Windows1256;
            (Encoding, _asciiClasses, _nonAsciiClasses) = table switch
            {
                SingleByteTable.Windows1258 => (WebEncodingId.Windows1258, EncodingDetectorData.VietnameseAsciiClassCount, EncodingDetectorData.VietnameseNonAsciiClassCount),
                SingleByteTable.Windows1250 => (WebEncodingId.Windows1250, EncodingDetectorData.CentralAsciiClassCount, EncodingDetectorData.CentralNonAsciiClassCount),
                SingleByteTable.Iso8859_2 => (WebEncodingId.Iso8859_2, EncodingDetectorData.CentralAsciiClassCount, EncodingDetectorData.CentralNonAsciiClassCount),
                SingleByteTable.Windows1251 => (WebEncodingId.Windows1251, EncodingDetectorData.CyrillicAsciiClassCount, EncodingDetectorData.CyrillicNonAsciiClassCount),
                SingleByteTable.Koi8U => (WebEncodingId.Koi8U, EncodingDetectorData.CyrillicAsciiClassCount, EncodingDetectorData.CyrillicNonAsciiClassCount),
                SingleByteTable.Iso8859_5 => (WebEncodingId.Iso8859_5, EncodingDetectorData.CyrillicAsciiClassCount, EncodingDetectorData.CyrillicNonAsciiClassCount),
                SingleByteTable.Ibm866 => (WebEncodingId.Ibm866, EncodingDetectorData.CyrillicAsciiClassCount, EncodingDetectorData.CyrillicNonAsciiClassCount),
                SingleByteTable.Windows1252 => (WebEncodingId.Windows1252, EncodingDetectorData.WesternAsciiClassCount, EncodingDetectorData.WesternNonAsciiClassCount),
                SingleByteTable.Windows1252Icelandic => (WebEncodingId.Windows1252, EncodingDetectorData.IcelandicAsciiClassCount, EncodingDetectorData.IcelandicNonAsciiClassCount),
                SingleByteTable.Windows1253 => (WebEncodingId.Windows1253, EncodingDetectorData.GreekAsciiClassCount, EncodingDetectorData.GreekNonAsciiClassCount),
                SingleByteTable.Iso8859_7 => (WebEncodingId.Iso8859_7, EncodingDetectorData.GreekAsciiClassCount, EncodingDetectorData.GreekNonAsciiClassCount),
                SingleByteTable.Windows1254 => (WebEncodingId.Windows1254, EncodingDetectorData.TurkishAsciiClassCount, EncodingDetectorData.TurkishNonAsciiClassCount),
                SingleByteTable.Windows1255 => (WebEncodingId.Windows1255, EncodingDetectorData.HebrewAsciiClassCount, EncodingDetectorData.HebrewNonAsciiClassCount),
                SingleByteTable.Iso8859_8 => (WebEncodingId.Iso8859_8, EncodingDetectorData.HebrewAsciiClassCount, EncodingDetectorData.HebrewNonAsciiClassCount),
                SingleByteTable.Windows1256 => (WebEncodingId.Windows1256, EncodingDetectorData.ArabicAsciiClassCount, EncodingDetectorData.ArabicNonAsciiClassCount),
                SingleByteTable.Iso8859_6 => (WebEncodingId.Iso8859_6, EncodingDetectorData.ArabicAsciiClassCount, EncodingDetectorData.ArabicNonAsciiClassCount),
                SingleByteTable.Windows1257 => (WebEncodingId.Windows1257, EncodingDetectorData.BalticAsciiClassCount, EncodingDetectorData.BalticNonAsciiClassCount),
                SingleByteTable.Iso8859_13 => (WebEncodingId.Iso8859_13, EncodingDetectorData.BalticAsciiClassCount, EncodingDetectorData.BalticNonAsciiClassCount),
                SingleByteTable.Iso8859_4 => (WebEncodingId.Iso8859_4, EncodingDetectorData.BalticAsciiClassCount, EncodingDetectorData.BalticNonAsciiClassCount),
                _ => (WebEncodingId.Windows874, EncodingDetectorData.ThaiAsciiClassCount, EncodingDetectorData.ThaiNonAsciiClassCount),
            };
        }

        public override WebEncodingId Encoding { get; }

        /// <summary>
        /// Gets the number of punctuation characters on the plausible side of a Hebrew word: after the word for the logical
        /// order, before the word for the visual order.
        /// </summary>
        public long PlausiblePunctuation { get; private set; }

        // The encodings of the non-Latin scripts need a word of two letters: a lone letter is more likely a symbol of
        // another encoding
        public override long? GetScore() => _kind != SingleByteKind.Latin && _longestWord < 2 ? null : base.GetScore();

        /// <summary>The classes of the ASCII bytes: the bit 7 is the upper case.</summary>
        private ReadOnlySpan<byte> AsciiClasses => _table switch
        {
            SingleByteTable.Windows1254 => EncodingDetectorData.TurkishAscii,
            SingleByteTable.Windows1258 or SingleByteTable.Windows1250 or SingleByteTable.Iso8859_2 or SingleByteTable.Windows1252 or SingleByteTable.Windows1252Icelandic
                or SingleByteTable.Windows1257 or SingleByteTable.Iso8859_13 or SingleByteTable.Iso8859_4 => EncodingDetectorData.LatinAscii,
            _ => EncodingDetectorData.NonLatinAscii,
        };

        /// <summary>The classes of the bytes 0x80 to 0xFF.</summary>
        private ReadOnlySpan<byte> NonAsciiClasses => _table switch
        {
            SingleByteTable.Windows1258 => EncodingDetectorData.Windows1258,
            SingleByteTable.Windows1250 => EncodingDetectorData.Windows1250,
            SingleByteTable.Iso8859_2 => EncodingDetectorData.Iso8859_2,
            SingleByteTable.Windows1251 => EncodingDetectorData.Windows1251,
            SingleByteTable.Koi8U => EncodingDetectorData.Koi8U,
            SingleByteTable.Iso8859_5 => EncodingDetectorData.Iso8859_5,
            SingleByteTable.Ibm866 => EncodingDetectorData.Ibm866,
            SingleByteTable.Windows1252 => EncodingDetectorData.Windows1252,
            SingleByteTable.Windows1252Icelandic => EncodingDetectorData.Windows1252Icelandic,
            SingleByteTable.Windows1253 => EncodingDetectorData.Windows1253,
            SingleByteTable.Iso8859_7 => EncodingDetectorData.Iso8859_7,
            SingleByteTable.Windows1254 => EncodingDetectorData.Windows1254,
            SingleByteTable.Windows1255 => EncodingDetectorData.Windows1255,
            SingleByteTable.Iso8859_8 => EncodingDetectorData.Iso8859_8,
            SingleByteTable.Windows1256 => EncodingDetectorData.Windows1256,
            SingleByteTable.Iso8859_6 => EncodingDetectorData.Iso8859_6,
            SingleByteTable.Windows1257 => EncodingDetectorData.Windows1257,
            SingleByteTable.Iso8859_13 => EncodingDetectorData.Iso8859_13,
            SingleByteTable.Iso8859_4 => EncodingDetectorData.Iso8859_4,
            _ => EncodingDetectorData.Windows874,
        };

        /// <summary>The scores of the pairs of classes of the letters (see <see cref="GetPairIndex"/>): 255 is an implausible pair.</summary>
        private ReadOnlySpan<byte> PairScores => _table switch
        {
            SingleByteTable.Windows1258 => EncodingDetectorData.Vietnamese,
            SingleByteTable.Windows1250 or SingleByteTable.Iso8859_2 => EncodingDetectorData.Central,
            SingleByteTable.Windows1251 or SingleByteTable.Koi8U or SingleByteTable.Iso8859_5 or SingleByteTable.Ibm866 => EncodingDetectorData.Cyrillic,
            SingleByteTable.Windows1252 => EncodingDetectorData.Western,
            SingleByteTable.Windows1252Icelandic => EncodingDetectorData.Icelandic,
            SingleByteTable.Windows1253 or SingleByteTable.Iso8859_7 => EncodingDetectorData.Greek,
            SingleByteTable.Windows1254 => EncodingDetectorData.Turkish,
            SingleByteTable.Windows1255 or SingleByteTable.Iso8859_8 => EncodingDetectorData.Hebrew,
            SingleByteTable.Windows1256 or SingleByteTable.Iso8859_6 => EncodingDetectorData.Arabic,
            SingleByteTable.Windows1257 or SingleByteTable.Iso8859_13 or SingleByteTable.Iso8859_4 => EncodingDetectorData.Baltic,
            _ => EncodingDetectorData.Thai,
        };

        public override bool Feed(ReadOnlySpan<byte> buffer, bool last)
        {
            var asciiClasses = AsciiClasses;
            var nonAsciiClasses = NonAsciiClasses;
            var pairScores = PairScores;
            var isValid = _kind == SingleByteKind.Latin ? FeedLatin(buffer, asciiClasses, nonAsciiClasses, pairScores) : FeedNonLatin(buffer, asciiClasses, nonAsciiClasses, pairScores);

            // The end of the stream is space-like
            if (isValid && last)
                return _kind == SingleByteKind.Latin ? FeedLatin(" "u8, asciiClasses, nonAsciiClasses, pairScores) : FeedNonLatin(" "u8, asciiClasses, nonAsciiClasses, pairScores);

            return isValid;
        }

        private bool FeedLatin(ReadOnlySpan<byte> buffer, ReadOnlySpan<byte> asciiClasses, ReadOnlySpan<byte> nonAsciiClasses, ReadOnlySpan<byte> pairScores)
        {
            var score = 0L;
            foreach (var b in buffer)
            {
                var isAscii = b < 0x80;
                int classWithCase = isAscii ? asciiClasses[b] : nonAsciiClasses[b & 0x7F];
                if (classWithCase == Unmapped)
                    return false;

                var current = classWithCase & 0x7F;
                var isAsciiPair = _previousNonAsciiCount == 0 && isAscii;

                // The words of the Latin script do not have many non-ASCII letters in a row
                score += _previousNonAsciiCount switch
                {
                    <= 2 => 0,
                    3 => -5,
                    4 => -20,
                    _ => -200,
                };

                // The pairs of letters whose case is implausible are penalized when one of the letters is not ASCII: a lower
                // case letter after two upper case letters (which tells windows-1250 from windows-1252: the byte 0x9F), an
                // upper case letter after a lower case letter
                if (!IsLatinAlphabetic(current))
                {
                    _caseState = CaseState.Space;
                }
                else if ((classWithCase & 0x80) == 0)
                {
                    if (_caseState == CaseState.AllCaps && !isAsciiPair)
                    {
                        score += ImplausibleLatinCaseTransitionPenalty;
                    }

                    _caseState = CaseState.Lower;
                }
                else if (_caseState == CaseState.Space)
                {
                    _caseState = CaseState.Upper;
                }
                else if (_caseState is CaseState.Upper or CaseState.AllCaps)
                {
                    _caseState = CaseState.AllCaps;
                }
                else
                {
                    if (!isAsciiPair)
                    {
                        score += ImplausibleLatinCaseTransitionPenalty;
                    }

                    _caseState = CaseState.Upper;
                }

                // A space-like character, which can be non-ASCII, next to an ASCII character has no score either: the ASCII
                // "i" and "I" of windows-1254 next to an apostrophe of windows-125x would make English ("I’m") Turkish
                var isAsciiLikePair = isAsciiPair || (isAscii && _previous == 0) || (current == 0 && _previousNonAsciiCount == 0);
                if (!isAsciiLikePair)
                {
                    score += GetPairScore(pairScores, current, _previous);
                }

                if (_table == SingleByteTable.Windows1252)
                {
                    score += AdvanceOrdinalState(b, current);
                }

                _previousNonAsciiCount = isAscii ? 0 : _previousNonAsciiCount + 1;
                _previous = current;
            }

            Score += score;
            return true;
        }

        private int AdvanceOrdinalState(byte b, int current)
        {
            const byte FeminineOrdinal = 0xAA;
            const byte MasculineOrdinal = 0xBA;
            var isSpace = current == 0;
            var isDigit = current == AsciiDigit;

            // The small Roman numerals: the classes of the letters i, v and x
            var isRoman = current is 9 or 22 or 24;
            switch (_ordinalState)
            {
                case OrdinalState.Other:
                    if (isSpace)
                    {
                        _ordinalState = OrdinalState.Space;
                    }

                    return 0;

                case OrdinalState.Space:
                    if (!isSpace)
                    {
                        _ordinalState = b switch
                        {
                            FeminineOrdinal or MasculineOrdinal => OrdinalState.OrdinalExpectingSpace,
                            (byte)'M' or (byte)'D' or (byte)'S' => OrdinalState.FeminineAbbreviationStartLetter,

                            // "número" or "Nuestra"
                            (byte)'N' => OrdinalState.UpperN,
                            (byte)'n' => OrdinalState.LowerN,
                            _ when isDigit => OrdinalState.Digit,
                            _ when isRoman => OrdinalState.Roman,
                            0xA9 => OrdinalState.Copyright,
                            _ => OrdinalState.Other,
                        };
                    }

                    return 0;

                case OrdinalState.OrdinalExpectingSpace:
                    _ordinalState = isSpace ? OrdinalState.Space : OrdinalState.Other;
                    return isSpace ? OrdinalBonus : 0;

                case OrdinalState.OrdinalExpectingSpaceUndoImplausibility:
                    _ordinalState = isSpace ? OrdinalState.Space : OrdinalState.Other;
                    return isSpace ? OrdinalBonus - ImplausibilityPenalty : 0;

                case OrdinalState.OrdinalExpectingSpaceOrDigit:
                    _ordinalState = isSpace ? OrdinalState.Space : OrdinalState.Other;
                    return isSpace || isDigit ? OrdinalBonus : 0;

                case OrdinalState.OrdinalExpectingSpaceOrDigitUndoImplausibility:
                    _ordinalState = isSpace ? OrdinalState.Space : OrdinalState.Other;
                    return isSpace || isDigit ? OrdinalBonus - ImplausibilityPenalty : 0;

                case OrdinalState.UpperN:
                    _ordinalState = b switch
                    {
                        FeminineOrdinal => OrdinalState.OrdinalExpectingSpaceUndoImplausibility,
                        MasculineOrdinal => OrdinalState.OrdinalExpectingSpaceOrDigitUndoImplausibility,
                        (byte)'.' => OrdinalState.PeriodAfterN,
                        _ => isSpace ? OrdinalState.Space : OrdinalState.Other,
                    };
                    return 0;

                case OrdinalState.LowerN:
                    _ordinalState = b switch
                    {
                        MasculineOrdinal => OrdinalState.OrdinalExpectingSpaceOrDigitUndoImplausibility,
                        (byte)'.' => OrdinalState.PeriodAfterN,
                        _ => isSpace ? OrdinalState.Space : OrdinalState.Other,
                    };
                    return 0;

                case OrdinalState.FeminineAbbreviationStartLetter:
                    _ordinalState = b == FeminineOrdinal ? OrdinalState.OrdinalExpectingSpaceUndoImplausibility : isSpace ? OrdinalState.Space : OrdinalState.Other;
                    return 0;

                case OrdinalState.Digit:
                    if (b is FeminineOrdinal or MasculineOrdinal)
                    {
                        _ordinalState = OrdinalState.OrdinalExpectingSpace;
                    }
                    else if (!isDigit)
                    {
                        _ordinalState = isSpace ? OrdinalState.Space : OrdinalState.Other;
                    }

                    return 0;

                case OrdinalState.Roman:
                    if (b is FeminineOrdinal or MasculineOrdinal)
                    {
                        _ordinalState = OrdinalState.OrdinalExpectingSpaceUndoImplausibility;
                    }
                    else if (!isRoman)
                    {
                        _ordinalState = isSpace ? OrdinalState.Space : OrdinalState.Other;
                    }

                    return 0;

                case OrdinalState.PeriodAfterN:
                    _ordinalState = b == MasculineOrdinal ? OrdinalState.OrdinalExpectingSpaceOrDigit : isSpace ? OrdinalState.Space : OrdinalState.Other;
                    return 0;

                default:
                    _ordinalState = isSpace ? OrdinalState.Space : OrdinalState.Other;
                    return isSpace ? CopyrightBonus : 0;
            }
        }

        private bool FeedNonLatin(ReadOnlySpan<byte> buffer, ReadOnlySpan<byte> asciiClasses, ReadOnlySpan<byte> nonAsciiClasses, ReadOnlySpan<byte> pairScores)
        {
            var score = 0L;
            foreach (var b in buffer)
            {
                var isAscii = b < 0x80;
                int classWithCase = isAscii ? asciiClasses[b] : nonAsciiClasses[b & 0x7F];
                if (classWithCase == Unmapped)
                    return false;

                var current = classWithCase & 0x7F;
                var isAsciiPair = _previousIsAscii && isAscii;
                var isAlphabetic = IsNonLatinAlphabetic(current);
                if (_kind == SingleByteKind.NonLatinCased)
                {
                    score += AdvanceNonLatinCase(current, isAlphabetic, isUpperCase: (classWithCase & 0x80) != 0);
                }
                else if (_kind == SingleByteKind.ArabicFrench)
                {
                    // The case of the letters of French is scored as the one of the Latin script
                    if (current != LatinLetter)
                    {
                        _caseState = CaseState.Space;
                    }
                    else if ((classWithCase & 0x80) == 0)
                    {
                        if (_caseState == CaseState.AllCaps && !isAsciiPair)
                        {
                            score += ImplausibleLatinCaseTransitionPenalty;
                        }

                        _caseState = CaseState.Lower;
                    }
                    else if (_caseState == CaseState.Space)
                    {
                        _caseState = CaseState.Upper;
                    }
                    else if (_caseState is CaseState.Upper or CaseState.AllCaps)
                    {
                        _caseState = CaseState.AllCaps;
                    }
                    else
                    {
                        if (!isAsciiPair)
                        {
                            score += ImplausibleLatinCaseTransitionPenalty;
                        }

                        _caseState = CaseState.Upper;
                    }
                }

                if (isAlphabetic)
                {
                    _currentWordLength++;
                }
                else
                {
                    _longestWord = Math.Max(_longestWord, _currentWordLength);
                    _currentWordLength = 0;
                }

                var isA0 = b == 0xA0;
                if (!isAsciiPair)
                {
                    // 0xA0 is the no-break space of many encodings and a letter of IBM866: it has no score next to itself or to a
                    // space-like byte
                    if (!(_table == SingleByteTable.Ibm866 && ((isA0 && (_previousIsA0 || _previous == 0)) || (current == 0 && _previousIsA0))))
                    {
                        score += GetPairScore(pairScores, current, _previous);
                    }

                    var previousIsAlphabetic = IsNonLatinAlphabetic(_previous);
                    if (_kind == SingleByteKind.Logical && current == 0 && previousIsAlphabetic && IsAsciiPunctuation(b))
                    {
                        PlausiblePunctuation++;
                    }
                    else if (_kind == SingleByteKind.Visual && isAlphabetic && _previousIsPunctuation)
                    {
                        PlausiblePunctuation++;
                    }

                    // An ASCII letter next to a letter of the script
                    if ((_previous == LatinLetter && isAlphabetic) || (current == LatinLetter && previousIsAlphabetic))
                    {
                        score += LatinAdjacencyPenalty;
                    }
                }

                _previousIsAscii = isAscii;
                _previous = current;
                _previousIsA0 = isA0;
                _previousIsPunctuation = current == 0 && IsAsciiPunctuation(b);
            }

            Score += score;
            return true;
        }

        /// <summary>
        /// Scores the case of the words of Cyrillic and Greek, which tells them apart (the letters of KOI8-U have the opposite
        /// case of the same bytes of the other encodings): a bonus for a capitalized word, a penalty for the other mixes of
        /// cases and for the upper case words of KOI8-U (lower case Greek is more likely). The ASCII letters make the word a mix.
        /// </summary>
        private long AdvanceNonLatinCase(int current, bool isAlphabetic, bool isUpperCase)
        {
            var score = 0L;
            if (current == LatinLetter)
            {
                _caseState = CaseState.Mix;
            }
            else if (!isAlphabetic)
            {
                score = _caseState switch
                {
                    CaseState.UpperLower => NonLatinCapitalizationBonus,
                    CaseState.AllCaps when _table == SingleByteTable.Koi8U => NonLatinAllCapsPenalty,
                    CaseState.Mix => NonLatinMixedCasePenalty * _currentWordLength,
                    _ => 0,
                };
                _caseState = CaseState.Space;
            }
            else if (!isUpperCase)
            {
                _caseState = _caseState switch
                {
                    CaseState.Space => CaseState.Lower,
                    CaseState.Upper => CaseState.UpperLower,
                    CaseState.AllCaps => CaseState.Mix,
                    _ => _caseState,
                };
            }
            else
            {
                _caseState = _caseState switch
                {
                    CaseState.Space => CaseState.Upper,
                    CaseState.Upper => CaseState.AllCaps,
                    CaseState.Lower or CaseState.UpperLower => CaseState.Mix,
                    _ => _caseState,
                };
            }

            return score;
        }

        private static bool IsAsciiPunctuation(byte b) => b is (byte)'.' or (byte)',' or (byte)':' or (byte)';' or (byte)'?' or (byte)'!';

        private bool IsLatinAlphabetic(int caselessClass) => caselessClass > 0 && caselessClass < _asciiClasses + _nonAsciiClasses;

        private bool IsNonLatinAlphabetic(int caselessClass) => caselessClass > (_isWindows1256 ? Windows1256Zwnj : LatinLetter) && caselessClass < _asciiClasses + _nonAsciiClasses;

        /// <summary>
        /// The index of a pair of classes of letters in the table of the scores, which has no entry for the pairs of ASCII
        /// classes: the rows of the ASCII classes only have the columns of the non-ASCII classes.
        /// </summary>
        private int GetPairIndex(int previous, int current)
        {
            if ((previous == 0 && current == 0) || (previous < _asciiClasses && current < _asciiClasses))
                return -1;

            if (current >= _asciiClasses)
                return (_asciiClasses * _nonAsciiClasses) + ((_asciiClasses + _nonAsciiClasses) * (current - _asciiClasses)) + previous;

            return (current * _nonAsciiClasses) + previous - _asciiClasses;
        }

        private int GetPairScore(ReadOnlySpan<byte> pairScores, int current, int previous)
        {
            var storedBoundary = _asciiClasses + _nonAsciiClasses;
            if (current < storedBoundary && previous < storedBoundary)
            {
                var index = GetPairIndex(previous, current);
                if (index < 0)
                    return 0;

                var score = pairScores[index];
                return score == 255 ? ImplausibilityPenalty : score;
            }

            if (current >= storedBoundary && previous >= storedBoundary)
                return current == AsciiDigit || previous == AsciiDigit ? 0 : ImplausibilityPenalty;

            // A letter next to a character that is not a letter: the class of the latter tells on which side of which
            // letters it is plausible
            var currentIsLetter = current < storedBoundary;
            var letter = currentIsLetter ? current : previous;
            if (letter == 0 || letter == AsciiDigit || (_isWindows1256 && letter == Windows1256Zwnj))
                return 0;

            return ((currentIsLetter ? previous : current) - storedBoundary) switch
            {
                // Plausible next to a letter on either side
                0 => 0,

                // Implausible next to a letter on either side
                1 => ImplausibilityPenalty,

                // Implausible before a letter
                2 => currentIsLetter ? ImplausibilityPenalty : 0,

                // Implausible after a letter
                3 => currentIsLetter ? 0 : ImplausibilityPenalty,

                // Plausible next to a non-ASCII letter on either side
                4 => letter < _asciiClasses ? ImplausibilityPenalty : 0,

                // Plausible next to an ASCII letter on either side
                5 => letter < _asciiClasses ? 0 : ImplausibilityPenalty,

                // The ASCII digits
                _ => 0,
            };
        }
    }
}
