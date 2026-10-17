using System.Buffers;

namespace Meziantou.Framework.WebPlatform.Encodings;

/// <summary>
/// Guesses the legacy encoding of a byte stream that does not declare one: a port of chardetng 1.0.0, the detector of
/// Firefox (https://github.com/hsivonen/chardetng, copyright Mozilla Foundation, Apache-2.0 or MIT license: its notice is
/// <c>Encodings/chardetng-COPYRIGHT.txt</c>, distributed with the package; its data is <see cref="EncodingDetectorData"/>).
/// <para>
/// Each candidate encoding scores the bytes and is disqualified by the bytes that are not valid for it. The single-byte
/// encodings map each byte to a class of characters (a letter of the languages of the encoding, with its case, a
/// space-like character, a digit...) and add the score of each pair of adjacent classes, which comes from the frequency
/// of the pair in the languages (<see cref="SingleByteCandidate"/>). The multi-byte encodings decode the bytes and score
/// the characters by kind: kana, common ideographs, hangul, private use (<see cref="MultiByteCandidate"/>). The guess is
/// the candidate that has the best score.
/// </para>
/// <para>
/// The differences with chardetng: the top-level domain of the URL is not used (it is the generic one for chardetng);
/// there is no ISO-2022-JP candidate (the browsers do not detect this encoding, and chardetng only guesses it on demand),
/// so the escape character is an ASCII character as any other; and nothing is guessed (null) where chardetng falls back
/// to windows-1252. The multi-byte candidates use the decoders of this library, whose errors are the ones of the
/// decoders of chardetng for the sequences it scores. The inside of the long runs of ASCII bytes is not scored, which
/// does not change the scores (see <see cref="FeedCandidates"/>).
/// </para>
/// </summary>
internal sealed partial class LegacyEncodingDetector
{
    private const int LatinAdjacencyPenalty = -50;
    private const int ImplausibilityPenalty = -220;
    private const int OrdinalBonus = 300;

    // Must match the ISO-8859-2 score of " Š "
    private const int CopyrightBonus = 222;
    private const int ImplausibleLatinCaseTransitionPenalty = -180;
    private const int NonLatinCapitalizationBonus = 40;
    private const int NonLatinAllCapsPenalty = -40;
    private const int NonLatinMixedCasePenalty = -20;

    // The index of the candidates that only take part in the guess in a special way
    private const int Utf8Index = 0;
    private const int VisualIndex = 1;
    private const int FirstNormalIndex = 2;
    private const int LogicalIndex = 15;

    // The order is the one of chardetng: the first candidate wins when the scores are equal
    private readonly Candidate[] _candidates =
    [
        new Utf8Candidate(),
        new SingleByteCandidate(SingleByteKind.Visual, SingleByteTable.Iso8859_8),
        new GbkCandidate(),
        new EucJPCandidate(),
        new EucKRCandidate(),
        new ShiftJisCandidate(),
        new Big5Candidate(),
        new SingleByteCandidate(SingleByteKind.Latin, SingleByteTable.Windows1252),
        new SingleByteCandidate(SingleByteKind.NonLatinCased, SingleByteTable.Windows1251),
        new SingleByteCandidate(SingleByteKind.Latin, SingleByteTable.Windows1250),
        new SingleByteCandidate(SingleByteKind.Latin, SingleByteTable.Iso8859_2),
        new SingleByteCandidate(SingleByteKind.ArabicFrench, SingleByteTable.Windows1256),
        new SingleByteCandidate(SingleByteKind.Latin, SingleByteTable.Windows1252Icelandic),
        new SingleByteCandidate(SingleByteKind.Latin, SingleByteTable.Windows1254),
        new SingleByteCandidate(SingleByteKind.Caseless, SingleByteTable.Windows874),
        new SingleByteCandidate(SingleByteKind.Logical, SingleByteTable.Windows1255),
        new SingleByteCandidate(SingleByteKind.NonLatinCased, SingleByteTable.Windows1253),
        new SingleByteCandidate(SingleByteKind.NonLatinCased, SingleByteTable.Iso8859_7),
        new SingleByteCandidate(SingleByteKind.Latin, SingleByteTable.Windows1257),
        new SingleByteCandidate(SingleByteKind.Latin, SingleByteTable.Iso8859_13),
        new SingleByteCandidate(SingleByteKind.NonLatinCased, SingleByteTable.Koi8U),
        new SingleByteCandidate(SingleByteKind.NonLatinCased, SingleByteTable.Ibm866),
        new SingleByteCandidate(SingleByteKind.Caseless, SingleByteTable.Iso8859_6),
        new SingleByteCandidate(SingleByteKind.Latin, SingleByteTable.Windows1258),
        new SingleByteCandidate(SingleByteKind.Latin, SingleByteTable.Iso8859_4),
        new SingleByteCandidate(SingleByteKind.NonLatinCased, SingleByteTable.Iso8859_5),
    ];

    // The last two ASCII bytes before the first non-ASCII byte: the context of the Spanish "n.º"
    private byte _beforeNonAscii1;
    private byte _beforeNonAscii2;
    private int _beforeNonAsciiCount;
    private bool _closed;

    /// <summary>Gets whether the bytes fed so far have a non-ASCII byte.</summary>
    public bool NonAsciiSeen { get; private set; }

    /// <summary>
    /// Feeds the next bytes of the stream. <paramref name="last"/> is true for the last bytes: the end of the stream ends
    /// a word, and an incomplete byte sequence disqualifies its encoding.
    /// </summary>
    public void Feed(ReadOnlySpan<byte> buffer, bool last)
    {
        if (_closed)
            throw new InvalidOperationException("The end of the stream has been fed");

        _closed = last;
        if (!NonAsciiSeen)
        {
            // The ASCII bytes before the first non-ASCII byte have no score: only the last two are fed
            var start = buffer.IndexOfAnyExceptInRange((byte)0x00, (byte)0x7F);
            if (start < 0)
            {
                foreach (var b in buffer.Length > 2 ? buffer[^2..] : buffer)
                {
                    _beforeNonAscii1 = _beforeNonAscii2;
                    _beforeNonAscii2 = b;
                    _beforeNonAsciiCount = Math.Min(_beforeNonAsciiCount + 1, 2);
                }

                return;
            }

            NonAsciiSeen = true;
            if (start < 2)
            {
                // As chardetng, the bytes kept from the previous buffers are the context, even when this buffer starts with an
                // ASCII byte
                Span<byte> before = [_beforeNonAscii1, _beforeNonAscii2];
                FeedCandidates(before[(2 - _beforeNonAsciiCount)..], last: false);
                start = 0;
            }
            else
            {
                start -= 2;
            }

            buffer = buffer[start..];
        }

        FeedCandidates(buffer, last);
    }

    // The ASCII bytes that end a word for all the candidates and reset their state: the ones that are not a letter, a digit
    // (a digit can start an ordinal number) or a full stop ("n.º")
    private static readonly SearchValues<byte> AsciiSeparators = SearchValues.Create(GetAsciiSeparators());

    private static byte[] GetAsciiSeparators()
    {
        var separators = new List<byte>();
        for (byte b = 0; b < 0x80; b++)
        {
            if (!char.IsAsciiLetterOrDigit((char)b) && b != '.')
            {
                separators.Add(b);
            }
        }

        return [.. separators];
    }

    /// <summary>
    /// Feeds the bytes to the candidates, except the inside of the long runs of ASCII bytes (the markup of a document):
    /// the ASCII bytes have no score, and after an ASCII separator the state of a candidate only depends on this
    /// separator, so the bytes between the first and the last separators of a run do not change anything. The first
    /// separator is after the first bytes of the run, which can be the end of a multi-byte sequence.
    /// </summary>
    private void FeedCandidates(ReadOnlySpan<byte> buffer, bool last)
    {
        const int MinimumRunLength = 32;
        const int MaximumSequenceLength = 4;
        var position = 0;
        while (buffer.Length - position >= MinimumRunLength)
        {
            // The next run of ASCII bytes
            var runStart = buffer[position..].IndexOfAnyInRange((byte)0x00, (byte)0x7F);
            if (runStart < 0)
                break;

            runStart += position;
            var runLength = buffer[runStart..].IndexOfAnyInRange((byte)0x80, (byte)0xFF);
            var runEnd = runLength < 0 ? buffer.Length : runStart + runLength;
            position = runEnd;
            if (runEnd - runStart < MinimumRunLength)
                continue;

            var run = buffer[(runStart + MaximumSequenceLength)..runEnd];
            var first = run.IndexOfAny(AsciiSeparators);
            var lastSeparator = run.LastIndexOfAny(AsciiSeparators);
            if (first < 0 || lastSeparator - first < MinimumRunLength / 2)
                continue;

            // The last separator is fed again: the state of the candidates depends on it
            FeedAllCandidates(buffer[..(runStart + MaximumSequenceLength + first + 1)], last: false);
            buffer = buffer[(runStart + MaximumSequenceLength + lastSeparator)..];
            position = runEnd - (runStart + MaximumSequenceLength + lastSeparator);
        }

        FeedAllCandidates(buffer, last);
    }

    private void FeedAllCandidates(ReadOnlySpan<byte> buffer, bool last)
    {
        foreach (var candidate in _candidates)
        {
            if (!candidate.IsDisqualified && !candidate.Feed(buffer, last))
            {
                candidate.IsDisqualified = true;
            }
        }
    }

    /// <summary>Gets the score of each candidate, null for the ones that cannot be the guess (for the tests).</summary>
    internal IEnumerable<(WebEncodingId Encoding, long? Score)> GetScores() => _candidates.Select(candidate => (candidate.Encoding, candidate.GetScore()));

    /// <summary>The stream is known not to be valid UTF-8 (the bytes that are not fed are checked by the caller).</summary>
    public void DisqualifyUtf8() => _candidates[Utf8Index].IsDisqualified = true;

    /// <summary>
    /// Guesses the encoding of the bytes fed so far, or returns null when nothing indicates an encoding: there is no
    /// non-ASCII byte, the bytes are valid UTF-8 and <paramref name="allowUtf8"/> is false, or no candidate has a score.
    /// </summary>
    public WebEncodingId? Guess(bool allowUtf8)
    {
        if (!NonAsciiSeen)
            return null;

        if (!_candidates[Utf8Index].IsDisqualified)
            return allowUtf8 ? WebEncodingId.Utf8 : null;

        WebEncodingId? encoding = null;
        var max = 0L;
        for (var i = FirstNormalIndex; i < _candidates.Length; i++)
        {
            if (_candidates[i].GetScore() is { } score && score > max)
            {
                max = score;
                encoding = _candidates[i].Encoding;
            }
        }

        // Visual Hebrew (ISO-8859-8) has the letters of logical Hebrew (windows-1255) in the reverse order: the place of the
        // punctuation, before or after the words, tells them apart
        var visual = (SingleByteCandidate)_candidates[VisualIndex];
        if (visual.GetScore() is { } visualScore &&
            (visualScore > max || encoding == WebEncodingId.Windows1255) &&
            visual.PlausiblePunctuation > ((SingleByteCandidate)_candidates[LogicalIndex]).PlausiblePunctuation)
        {
            encoding = WebEncodingId.Iso8859_8;
        }

        return encoding;
    }

    private abstract class Candidate
    {
        protected long Score { get; set; }

        public bool IsDisqualified { get; set; }

        public abstract WebEncodingId Encoding { get; }

        /// <summary>Scores the bytes. Returns false when the bytes are not valid for the encoding.</summary>
        public abstract bool Feed(ReadOnlySpan<byte> buffer, bool last);

        /// <summary>Gets the score, or null when the candidate cannot be the guess.</summary>
        public virtual long? GetScore() => IsDisqualified ? null : Score;
    }

    private sealed class Utf8Candidate : Candidate
    {
        private readonly Utf8Decoder _decoder = new(fatal: true);

        public override WebEncodingId Encoding => WebEncodingId.Utf8;

        public override bool Feed(ReadOnlySpan<byte> buffer, bool last)
        {
            Span<char> output = stackalloc char[512];
            while (true)
            {
                var status = _decoder.Decode(buffer, output, flush: last, out var consumed, out _);
                if (status == DecodeStatus.Error)
                    return false;

                if (status != DecodeStatus.OutputFull)
                    return true;

                buffer = buffer[consumed..];
            }
        }
    }
}
