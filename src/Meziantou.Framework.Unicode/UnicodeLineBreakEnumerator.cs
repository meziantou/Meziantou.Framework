using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using LineBreak = Meziantou.Framework.UnicodeLineBreakClass;

namespace Meziantou.Framework;

/// <summary>Enumerates the positions where a line can or must break. See <see cref="Unicode.EnumerateLineBreaks(ReadOnlySpan{char}, UnicodeLineBreakStrictness, UnicodeWordBreakBehavior)"/>.</summary>
/// <remarks>
/// <para>
/// The rules are those of Unicode Standard Annex #14 revision 57 (Unicode 18.0.0). A rule reads the classes of the
/// units around a position; a unit is a character with the combining marks and zero width joiners that follow it
/// (rule LB9). The enumerator keeps the two units before the position, reads at most three units after it, and
/// summarizes what the rules need from further back in a few counters, so the cost is linear.
/// </para>
/// <para>
/// With a <see cref="LineBreakDictionary"/>, a run of Complex_Context characters is segmented piece by piece when the
/// enumeration reaches it, and the enumerator only keeps the word starts of the current piece: the cost stays linear
/// and nothing is allocated.
/// </para>
/// </remarks>
public ref struct UnicodeLineBreakEnumerator
{
    private const LineBreak EndOfText = (LineBreak)0xFF;
    private const LineBreak StartOfText = (LineBreak)0xFE;

    private const byte EastAsianFlag = 1;
    private const byte InitialQuoteFlag = 2;
    private const byte FinalQuoteFlag = 4;
    private const byte UnassignedPictographicFlag = 8;
    private const byte DottedCircleFlag = 16;
    private const byte ComplexContextFlag = 32;

    // The kinds of characters that SkipWord reads: see GetShortcutKind
    internal const int WordCharacter = 1;
    internal const int ClosingMark = 2;
    internal const int OpeningMark = 4;
    internal const int InnerMark = 8;
    internal const int NumericAffix = 16;
    internal const int Space = 32;

    private readonly ReadOnlySpan<char> _text;
    private readonly UnicodeLineBreakStrictness _strictness;
    private readonly UnicodeWordBreakBehavior _wordBreak;

    // The words of letters and digits are read without the rules: false with BreakAll, which breaks inside them, and when
    // the shortcuts are turned off to compare them with the rules.
    private readonly bool _skipsWords;

    // The dictionary that segments the runs of Complex_Context characters, or null when it cannot add a break.
    private readonly LineBreakDictionary? _dictionary;

    // The units after the position, read on demand.
    private Unit _next0;
    private Unit _next1;
    private Unit _next2;
    private int _readAhead;

    // The character that ended the last unit that was read, which rule LB9 looked up: the next unit starts with it.
    // _lookedUpIndex is -1 when there is none.
    private int _lookedUpIndex;
    private int _lookedUpLength;
    private LineBreak _lookedUpClass;
    private byte _lookedUpFlags;

    // The units before the position.
    private Unit _previous;
    private Unit _beforePrevious;

    // The class of the last unit that is not a space (rules of the form "X SP* ×").
    private LineBreak _beforeSpaces;

    // LB15a: the last unit that is not a space is an initial quotation mark in a position that opens a quotation.
    private bool _isAfterOpeningQuote;

    // LB25: 1 after NU (SY | IS)*, 2 after NU (SY | IS)* (CL | CP).
    private int _number;

    // LB30a: the number of regional indicators that end at the previous unit.
    private int _regionalIndicators;

    // The run of Complex_Context characters that the dictionary segments, and the piece of it that is segmented:
    // _wordStarts has a bit for each index of the piece where a word of the dictionary starts, and _boundaries the
    // boundaries after the piece that the dictionary already found.
    private int _runStart;
    private int _runEnd;
    private int _pieceStart;
    private int _pieceEnd;
    private PieceBits _wordStarts;
    private PieceBits _boundaries;
    private int _boundaryLength;

    private int _position;
    private bool _isStarted;
    private bool _isCompleted;
    private UnicodeLineBreak _current;

    internal UnicodeLineBreakEnumerator(ReadOnlySpan<char> text, UnicodeLineBreakStrictness strictness, UnicodeWordBreakBehavior wordBreak, LineBreakDictionary? dictionary = null, bool useShortcuts = true)
    {
        _text = text;
        _strictness = strictness;
        _wordBreak = wordBreak;
        _skipsWords = useShortcuts && wordBreak is not UnicodeWordBreakBehavior.BreakAll;

        // A line already breaks between any two letters, or any two grapheme clusters, in the other modes.
        _dictionary = dictionary is { WordCount: > 0 } && wordBreak is not UnicodeWordBreakBehavior.BreakAll && strictness is not UnicodeLineBreakStrictness.Anywhere ? dictionary : null;
        _previous = new Unit(StartOfText, 0, 0, endsWithJoiner: false);
        _beforePrevious = _previous;
        _beforeSpaces = StartOfText;
        _lookedUpIndex = -1;
    }

    /// <summary>Gets the current break.</summary>
    public readonly UnicodeLineBreak Current => _current;

    /// <summary>Returns this instance, so that the enumerator can be used by a <see langword="foreach"/> statement.</summary>
    /// <returns>This instance.</returns>
    public readonly UnicodeLineBreakEnumerator GetEnumerator() => this;

    /// <summary>Moves to the next break.</summary>
    /// <returns><see langword="true"/> when there is a break; <see langword="false"/> after the break at the end of the text.</returns>
    public bool MoveNext()
    {
        if (_isCompleted || _text.IsEmpty)
            return false;

        if (_strictness is UnicodeLineBreakStrictness.Anywhere)
            return MoveNextAnywhere();

        if (!_isStarted)
        {
            // LB2: there is no break at the start of the text.
            _isStarted = true;
            Consume();
        }

        while (true)
        {
            if (_previous.Class is LineBreak.Alphabetic or LineBreak.HebrewLetter or LineBreak.Numeric && _skipsWords && SkipWord())
                return true;

            var next = Peek(0);
            if (next.Class is EndOfText)
            {
                // LB3
                _isCompleted = true;
                _current = new UnicodeLineBreak(_text.Length, isMandatory: true);
                return true;
            }

            var index = _position;
            var decision = Decide(next);
            if (decision is Decision.NoBreak && _dictionary is not null && next.Has(ComplexContextFlag) && IsWordStart(index))
            {
                decision = Decision.Break;
            }

            Consume();
            if (decision is not Decision.NoBreak)
            {
                _current = new UnicodeLineBreak(index, decision is Decision.MandatoryBreak);
                return true;
            }
        }
    }

    private bool MoveNextAnywhere()
    {
        // Every grapheme cluster boundary is an opportunity. LB4 and LB5 still apply: CR LF is one cluster.
        var length = Unicode.GetGraphemeClusterLength(_text[_position..]);
        var last = (LineBreak)(UnicodeProperties.GetLineBreakTuple(Unicode.DecodeBefore(_text, _position + length, out _)) & 0xFF);
        _position += length;
        _isCompleted = _position >= _text.Length;
        _current = new UnicodeLineBreak(_position, _isCompleted || last is LineBreak.MandatoryBreak or LineBreak.CarriageReturn or LineBreak.LineFeed or LineBreak.NextLine);
        return true;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private Decision Decide(Unit next)
    {
        var p = _previous.Class;
        var n = next.Class;

        // Between letters, digits and ideographs, only LB8a, LB23, LB28, LB28a (with a dotted circle) and LB31 apply.
        if (p is LineBreak.Alphabetic or LineBreak.HebrewLetter or LineBreak.Numeric or LineBreak.Ideographic
            && n is LineBreak.Alphabetic or LineBreak.HebrewLetter or LineBreak.Numeric or LineBreak.Ideographic
            && !_previous.EndsWithJoiner && !_previous.Has(DottedCircleFlag) && !next.Has(DottedCircleFlag))
        {
            return (p is LineBreak.Ideographic || n is LineBreak.Ideographic) && _wordBreak is not UnicodeWordBreakBehavior.KeepAll ? Decision.Break : Decision.NoBreak;
        }

        return DecideByRules(next);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private Decision DecideByRules(Unit next)
    {
        var p = _previous.Class;
        var n = next.Class;

        // LB4, LB5
        if (p is LineBreak.MandatoryBreak or LineBreak.LineFeed or LineBreak.NextLine)
            return Decision.MandatoryBreak;

        if (p is LineBreak.CarriageReturn)
            return n is LineBreak.LineFeed ? Decision.NoBreak : Decision.MandatoryBreak;

        // LB6, LB7
        if (n is LineBreak.MandatoryBreak or LineBreak.CarriageReturn or LineBreak.LineFeed or LineBreak.NextLine or LineBreak.Space or LineBreak.ZWSpace)
            return Decision.NoBreak;

        // LB8
        if (_beforeSpaces is LineBreak.ZWSpace)
            return Decision.Break;

        // LB8a
        if (_previous.EndsWithJoiner)
            return Decision.NoBreak;

        // LB11, LB12
        if (n is LineBreak.WordJoiner || p is LineBreak.WordJoiner or LineBreak.Glue)
            return Decision.NoBreak;

        // LB12a
        if (n is LineBreak.Glue && p is not (LineBreak.Space or LineBreak.Hyphen or LineBreak.UnambiguousHyphen))
            return Decision.NoBreak;

        // LB13
        if (n is LineBreak.ClosePunctuation or LineBreak.CloseParenthesis or LineBreak.Exclamation or LineBreak.BreakSymbols)
            return Decision.NoBreak;

        // LB14, LB15a
        if (_beforeSpaces is LineBreak.OpenPunctuation || _isAfterOpeningQuote)
            return Decision.NoBreak;

        // LB15b
        if (n is LineBreak.Quotation && next.Has(FinalQuoteFlag)
            && Peek(1).Class is EndOfText or LineBreak.Space or LineBreak.Glue or LineBreak.WordJoiner or LineBreak.ClosePunctuation
                or LineBreak.Quotation or LineBreak.CloseParenthesis or LineBreak.Exclamation or LineBreak.InfixNumeric
                or LineBreak.BreakSymbols or LineBreak.MandatoryBreak or LineBreak.CarriageReturn or LineBreak.LineFeed
                or LineBreak.NextLine or LineBreak.ZWSpace)
        {
            return Decision.NoBreak;
        }

        if (n is LineBreak.InfixNumeric)
        {
            // LB15c, LB15d
            return p is LineBreak.Space && Peek(1).Class is LineBreak.Numeric ? Decision.Break : Decision.NoBreak;
        }

        // LB16, LB17
        if ((_beforeSpaces is LineBreak.ClosePunctuation or LineBreak.CloseParenthesis && n is LineBreak.Nonstarter)
            || (_beforeSpaces is LineBreak.BreakBoth && n is LineBreak.BreakBoth))
        {
            return Decision.NoBreak;
        }

        // LB18
        if (p is LineBreak.Space)
            return Decision.Break;

        // LB19, LB19a
        if (n is LineBreak.Quotation)
        {
            if (!next.Has(InitialQuoteFlag) || !_previous.Has(EastAsianFlag))
                return Decision.NoBreak;

            var after = Peek(1);
            if (after.Class is EndOfText || !after.Has(EastAsianFlag))
                return Decision.NoBreak;
        }

        if (p is LineBreak.Quotation
            && (!_previous.Has(FinalQuoteFlag) || !next.Has(EastAsianFlag) || _beforePrevious.Class is StartOfText || !_beforePrevious.Has(EastAsianFlag)))
        {
            return Decision.NoBreak;
        }

        // LB20
        if (n is LineBreak.ContingentBreak || p is LineBreak.ContingentBreak)
            return Decision.Break;

        // LB20a
        if (p is LineBreak.Hyphen or LineBreak.UnambiguousHyphen
            && n is LineBreak.Alphabetic or LineBreak.HebrewLetter
            && _beforePrevious.Class is StartOfText or LineBreak.MandatoryBreak or LineBreak.CarriageReturn or LineBreak.LineFeed
                or LineBreak.NextLine or LineBreak.Space or LineBreak.ZWSpace or LineBreak.ContingentBreak or LineBreak.Glue)
        {
            return Decision.NoBreak;
        }

        // LB21
        if (n is LineBreak.BreakAfter or LineBreak.UnambiguousHyphen or LineBreak.Hyphen or LineBreak.Nonstarter || p is LineBreak.BreakBefore)
            return Decision.NoBreak;

        // LB21a
        if (_beforePrevious.Class is LineBreak.HebrewLetter && p is LineBreak.Hyphen or LineBreak.UnambiguousHyphen && n is not LineBreak.HebrewLetter)
            return Decision.NoBreak;

        // LB21b, LB22
        if ((p is LineBreak.BreakSymbols && n is LineBreak.HebrewLetter) || n is LineBreak.Inseparable)
            return Decision.NoBreak;

        var isPreviousLetter = p is LineBreak.Alphabetic or LineBreak.HebrewLetter;
        var isNextLetter = n is LineBreak.Alphabetic or LineBreak.HebrewLetter;

        // LB23
        if ((isPreviousLetter && n is LineBreak.Numeric) || (p is LineBreak.Numeric && isNextLetter))
            return Decision.NoBreak;

        // LB23a
        if ((p is LineBreak.PrefixNumeric && n is LineBreak.Ideographic or LineBreak.EBase or LineBreak.EModifier)
            || (p is LineBreak.Ideographic or LineBreak.EBase or LineBreak.EModifier && n is LineBreak.PostfixNumeric))
        {
            return Decision.NoBreak;
        }

        // LB24
        if ((p is LineBreak.PrefixNumeric or LineBreak.PostfixNumeric && isNextLetter)
            || (isPreviousLetter && n is LineBreak.PrefixNumeric or LineBreak.PostfixNumeric))
        {
            return Decision.NoBreak;
        }

        // LB25
        if (_number != 0 && n is LineBreak.PostfixNumeric or LineBreak.PrefixNumeric)
            return Decision.NoBreak;

        if (p is LineBreak.PostfixNumeric or LineBreak.PrefixNumeric)
        {
            if (n is LineBreak.Numeric)
                return Decision.NoBreak;

            if (n is LineBreak.OpenPunctuation)
            {
                var after = Peek(1).Class;
                if (after is LineBreak.Numeric || (after is LineBreak.InfixNumeric && Peek(2).Class is LineBreak.Numeric))
                    return Decision.NoBreak;
            }
        }

        if (n is LineBreak.Numeric && (p is LineBreak.Hyphen or LineBreak.InfixNumeric || _number == 1))
            return Decision.NoBreak;

        // LB26
        if ((p is LineBreak.JL && n is LineBreak.JL or LineBreak.JV or LineBreak.H2 or LineBreak.H3)
            || (p is LineBreak.JV or LineBreak.H2 && n is LineBreak.JV or LineBreak.JT)
            || (p is LineBreak.JT or LineBreak.H3 && n is LineBreak.JT))
        {
            return Decision.NoBreak;
        }

        // LB27
        if ((p is LineBreak.JL or LineBreak.JV or LineBreak.JT or LineBreak.H2 or LineBreak.H3 && n is LineBreak.PostfixNumeric)
            || (p is LineBreak.PrefixNumeric && n is LineBreak.JL or LineBreak.JV or LineBreak.JT or LineBreak.H2 or LineBreak.H3))
        {
            return Decision.NoBreak;
        }

        // LB28
        if (isPreviousLetter && isNextLetter)
            return Decision.NoBreak;

        // LB28a
        var isPreviousAksara = p is LineBreak.Aksara or LineBreak.AksaraStart || _previous.Has(DottedCircleFlag);
        var isNextAksara = n is LineBreak.Aksara or LineBreak.AksaraStart || next.Has(DottedCircleFlag);
        if ((p is LineBreak.AksaraPrebase && isNextAksara)
            || (isPreviousAksara && n is LineBreak.ViramaFinal or LineBreak.Virama)
            || (p is LineBreak.Virama && n is not LineBreak.AksaraStart && isNextAksara
                && (_beforePrevious.Class is LineBreak.Aksara or LineBreak.AksaraStart || _beforePrevious.Has(DottedCircleFlag)))
            || (isPreviousAksara && isNextAksara && Peek(1).Class is LineBreak.ViramaFinal))
        {
            return Decision.NoBreak;
        }

        // LB29
        if (p is LineBreak.InfixNumeric && isNextLetter)
            return Decision.NoBreak;

        // LB30
        if ((isPreviousLetter || p is LineBreak.Numeric) && n is LineBreak.OpenPunctuation && !next.Has(EastAsianFlag))
            return Decision.NoBreak;

        if (p is LineBreak.CloseParenthesis && !_previous.Has(EastAsianFlag) && (isNextLetter || n is LineBreak.Numeric))
            return Decision.NoBreak;

        // LB30a
        if (p is LineBreak.RegionalIndicator && n is LineBreak.RegionalIndicator && (_regionalIndicators & 1) != 0)
            return Decision.NoBreak;

        // LB30b
        if (n is LineBreak.EModifier && (p is LineBreak.EBase || _previous.Has(UnassignedPictographicFlag)))
            return Decision.NoBreak;

        // LB31, unless a line must not break inside a word.
        if (_wordBreak is UnicodeWordBreakBehavior.KeepAll && IsWordCharacter(p) && IsWordCharacter(n))
            return Decision.NoBreak;

        return Decision.Break;
    }

    private static bool IsWordCharacter(LineBreak value)
    {
        return value is LineBreak.Alphabetic or LineBreak.HebrewLetter or LineBreak.Numeric or LineBreak.Ideographic
            or LineBreak.H2 or LineBreak.H3 or LineBreak.JL or LineBreak.JV or LineBreak.JT;
    }

    /// <summary>Determines whether a word of the dictionary starts at the index of a Complex_Context letter, after the start of its run.</summary>
    /// <remarks>
    /// Between two letters of a run, with their marks, the rules of the annex only give LB28: no break. That is the
    /// decision a word of the dictionary changes.
    /// </remarks>
    private bool IsWordStart(int index)
    {
        if (index >= _runEnd)
        {
            // The run is the Complex_Context characters around the index, from their first letter: a mark before
            // that letter belongs to the character before it (LB9). The letter at the index starts a unit, and so
            // does a letter before it, which is in the previous unit or is the start of a run that is known.
            var start = index;
            for (var i = index; i > 0;)
            {
                var tuple = UnicodeProperties.GetLineBreakTuple(Unicode.DecodeBefore(_text, i, out var length));
                if ((LineBreak)(tuple & 0xFF) is not LineBreak.ComplexContext)
                    break;

                i -= length;
                if ((tuple & 0x8000) == 0)
                {
                    start = i;
                }
            }

            var end = index;
            while (end < _text.Length)
            {
                var tuple = UnicodeProperties.GetLineBreakTuple(Unicode.DecodeAt(_text, end, out var length));
                if ((LineBreak)(tuple & 0xFF) is not LineBreak.ComplexContext)
                    break;

                end += length;
            }

            _runStart = start;
            _runEnd = end;
            _pieceStart = start;
            _pieceEnd = start;
            _boundaryLength = 0;
        }

        if (index == _runStart)
            return false;

        while (index >= _pieceEnd)
        {
            _pieceStart = _pieceEnd;
            _pieceEnd += _dictionary!.SegmentPiece(_text[_pieceStart.._runEnd], _wordStarts, _boundaries, ref _boundaryLength);
        }

        // A cluster that is longer than a piece has no word.
        var offset = index - _pieceStart;
        return offset < LineBreakDictionary.PieceLength && (_wordStarts[offset >> 6] & (1UL << (offset & 63))) != 0;
    }

    /// <summary>Moves the position over the letters and digits that follow a letter or a digit, and to the next word when only punctuation and spaces separate them.</summary>
    /// <returns><see langword="true"/> when the break before the next word is found: it is <see cref="Current"/>.</returns>
    /// <remarks>
    /// <para>
    /// The rules give the same decision for these characters, whatever is before them:
    /// </para>
    /// <list type="bullet">
    /// <item>
    /// no break between a letter or a digit and an ASCII or Latin character of the classes Alphabetic or Numeric (LB23,
    /// LB25, LB28), an apostrophe between letters or digits (LB19), a comma, a period, a colon or a semicolon before a
    /// letter or a digit (LB15d, LB25, LB29), or a hyphen before a digit (LB21, LB25). What the rules remember after
    /// them only depends on the last two;
    /// </item>
    /// <item>a break between a hyphen that follows a letter or a digit and a letter (LB31), unless the letter before the hyphen is Hebrew (LB21a);</item>
    /// <item>no break between a letter or a digit and a percent sign or a prefix such as a currency symbol (LB24, LB25);</item>
    /// <item>
    /// no break before the marks that close a word (LB13: <c>! ? ) ] } /</c>, LB15d: <c>, . : ;</c>, LB15b and LB19:
    /// <c>" ' ’ »</c>, LB12a: no-break space), nor before the spaces (LB7). The line breaks where the spaces end
    /// (LB18): no rule of the form "X SP* ×" applies to a letter, a digit or these marks;
    /// </item>
    /// <item>
    /// no break between a mark that opens a word and a letter or a digit (LB14: <c>( [ {</c>, LB19: <c>" '</c>, LB20a
    /// and LB25: <c>-</c> after a space, LB24 and LB25: <c>$ + \ %</c>).
    /// </item>
    /// </list>
    /// <para>
    /// A unit is a character on its own when a printable ASCII character or a Latin letter follows it: they are not
    /// combining marks (LB9). Otherwise the last letter is read as usual.
    /// </para>
    /// </remarks>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private bool SkipWord()
    {
        // The letters of the other scripts are read as usual.
        return _position < _text.Length && _text[_position] is < '\u0180' or '\u2019' && SkipLatinWord();
    }

    /// <summary>See <see cref="SkipWord"/>.</summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private bool SkipLatinWord()
    {
        var text = _text;
        var end = _position;
        var kind = GetShortcutKindAt(text, end);
        while (true)
        {
            if (kind is WordCharacter)
            {
                end++;
            }
            else if ((kind & InnerMark) != 0 && GetShortcutKindAt(text, end + 1) is WordCharacter && (text[end] is not '-' || char.IsAsciiDigit(text[end + 1])))
            {
                end += 2;
            }
            else
            {
                break;
            }

            kind = GetShortcutKindAt(text, end);
        }

        var index = end;
        var breakIndex = -1;
        if ((kind & InnerMark) != 0 && text[index] is '-' && (index > _position || _previous.Class is not LineBreak.HebrewLetter))
        {
            // The hyphen is followed by a letter, else it would be part of the word.
            breakIndex = ++index;
            kind = GetShortcutKindAt(text, index);
        }
        else
        {
            if ((kind & NumericAffix) != 0)
            {
                kind = GetShortcutKindAt(text, ++index);
            }

            while ((kind & ClosingMark) != 0)
            {
                kind = GetShortcutKindAt(text, ++index);
            }

            var spaces = index;
            while (kind is Space)
            {
                kind = GetShortcutKindAt(text, ++index);
            }

            if (index > spaces)
            {
                breakIndex = index;
                if ((kind & OpeningMark) != 0)
                {
                    kind = GetShortcutKindAt(text, ++index);
                }
            }
        }

        if (breakIndex >= 0 && kind is WordCharacter && (index + 1 == text.Length || IsUnitEnd(text[index + 1])))
        {
            _beforePrevious = index == breakIndex ? new Unit(LineBreak.Space, 0, index, endsWithJoiner: false) : CreateWordUnit(index - 1);
            _previous = CreateWordUnit(index);
            _beforeSpaces = _previous.Class;
            _isAfterOpeningQuote = false;
            _number = _previous.Class is LineBreak.Numeric ? 1 : 0;
            _regionalIndicators = 0;
            _position = index + 1;
            _readAhead = 0;
            _current = new UnicodeLineBreak(breakIndex, isMandatory: false);
            return true;
        }

        var skipped = end - _position - 1;
        if (skipped <= 0)
            return false;

        end = _position + skipped;
        _beforePrevious = skipped >= 2 ? CreateWordUnit(end - 2) : _previous;
        _previous = CreateWordUnit(end - 1);
        _beforeSpaces = _previous.Class;
        _isAfterOpeningQuote = false;
        _number = _previous.Class is LineBreak.Numeric || (_previous.Class is LineBreak.InfixNumeric && _beforePrevious.Class is LineBreak.Numeric) ? 1 : 0;
        _regionalIndicators = 0;
        _position = end;

        // The units read ahead start at the old position: they are read again.
        _readAhead = 0;
        return false;
    }

    /// <summary>Gets the kind of the character at an index, or 0 at the end of the text: see <see cref="GetShortcutKind"/>.</summary>
    private static int GetShortcutKindAt(ReadOnlySpan<char> text, int index) => (uint)index < (uint)text.Length ? GetShortcutKind(text[index]) : 0;

    /// <summary>Gets what <see cref="SkipWord"/> reads of a character: <see cref="WordCharacter"/>, <see cref="Space"/>, a combination of <see cref="ClosingMark"/>, <see cref="OpeningMark"/>, <see cref="InnerMark"/> and <see cref="NumericAffix"/>, or 0.</summary>
    /// <remarks>
    /// The word characters are of the class Alphabetic, Ambiguous (resolved to Alphabetic) or Numeric, and the rules read
    /// no other property of these characters but the final quotation mark <c>’</c>: <see cref="CreateWordUnit"/> creates their unit.
    /// </remarks>
    internal static int GetShortcutKind(char c)
    {
        if (c < 0x80)
            return AsciiShortcutKinds[c];

        return c switch
        {
            >= '\u00C0' and <= '\u017F' => WordCharacter,
            '\u00A0' or '\u00BB' => ClosingMark,
            '\u2019' => ClosingMark | InnerMark,
            _ => 0,
        };
    }

    // The kinds of the ASCII characters: see GetShortcutKind
    private static ReadOnlySpan<byte> AsciiShortcutKinds =>
    [
        0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0,
        0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0,

        // space ! " # $ % & ' ( ) * + , - . /
        32, 2, 6, 1, 20, 20, 1, 14, 4, 2, 1, 20, 10, 12, 10, 2,

        // 0-9 : ; < = > ?
        1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 10, 10, 1, 1, 1, 2,

        // @ A-Z [ \ ] ^ _
        1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1,
        1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 4, 20, 2, 1, 1,

        // ` a-z { | } ~ DEL
        1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1,
        1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 4, 0, 2, 1, 0,
    ];

    /// <summary>Determines whether a character cannot be part of the unit before it (LB9).</summary>
    private static bool IsUnitEnd(char c) => c is >= ' ' and <= '~' || GetShortcutKind(c) is WordCharacter;

    /// <summary>Gets the class of a word character, an inner mark, an opening mark or a numeric affix: see <see cref="GetShortcutKind"/>.</summary>
    internal static LineBreak GetShortcutClass(char c) => c switch
    {
        >= '0' and <= '9' => LineBreak.Numeric,
        '\'' or '"' or '\u2019' => LineBreak.Quotation,
        '-' => LineBreak.Hyphen,
        '(' or '[' or '{' => LineBreak.OpenPunctuation,
        ',' or '.' or ':' or ';' => LineBreak.InfixNumeric,
        '$' or '+' or '\\' => LineBreak.PrefixNumeric,
        '%' => LineBreak.PostfixNumeric,
        _ => LineBreak.Alphabetic,
    };

    /// <summary>Creates the unit of a word character, an inner mark, an opening mark or a numeric affix that is not followed by a combining mark.</summary>
    private readonly Unit CreateWordUnit(int index)
    {
        var c = _text[index];
        return new Unit(GetShortcutClass(c), c is '\u2019' ? FinalQuoteFlag : (byte)0, index + 1, endsWithJoiner: false);
    }

    /// <summary>Moves the position after the next unit and updates what the rules remember of the text before it.</summary>
    private void Consume()
    {
        var unit = Peek(0);
        var value = unit.Class;
        if (value is not LineBreak.Space)
        {
            // LB15a: an initial quotation mark opens a quotation at the start of the text and after these classes.
            _isAfterOpeningQuote = value is LineBreak.Quotation && unit.Has(InitialQuoteFlag)
                && _previous.Class is StartOfText or LineBreak.MandatoryBreak or LineBreak.CarriageReturn or LineBreak.LineFeed
                    or LineBreak.NextLine or LineBreak.OpenPunctuation or LineBreak.Quotation or LineBreak.Glue
                    or LineBreak.Space or LineBreak.ZWSpace;
            _beforeSpaces = value;
        }

        _number = value switch
        {
            LineBreak.Numeric => 1,
            LineBreak.BreakSymbols or LineBreak.InfixNumeric when _number == 1 => 1,
            LineBreak.ClosePunctuation or LineBreak.CloseParenthesis when _number == 1 => 2,
            _ => 0,
        };

        _regionalIndicators = value is LineBreak.RegionalIndicator ? _regionalIndicators + 1 : 0;
        _beforePrevious = _previous;
        _previous = unit;
        _position = unit.End;

        _next0 = _next1;
        _next1 = _next2;
        _readAhead--;
    }

    /// <summary>Gets a unit after the position: 0 is the next one.</summary>
    private Unit Peek(int offset)
    {
        while (_readAhead <= offset)
        {
            var start = _readAhead switch
            {
                0 => _position,
                1 => _next0.End,
                _ => _next1.End,
            };

            var unit = ReadUnit(start);
            switch (_readAhead)
            {
                case 0:
                    _next0 = unit;
                    break;

                case 1:
                    _next1 = unit;
                    break;

                default:
                    _next2 = unit;
                    break;
            }

            _readAhead++;
        }

        return offset switch
        {
            0 => _next0,
            1 => _next1,
            _ => _next2,
        };
    }

    /// <summary>Reads a character and the combining marks and zero width joiners it carries (LB9), and resolves its class (LB1, LB10).</summary>
    private Unit ReadUnit(int index)
    {
        if (index >= _text.Length)
            return new Unit(EndOfText, 0, _text.Length, endsWithJoiner: false);

        LineBreak value;
        byte flags;
        int length;
        if (index == _lookedUpIndex)
        {
            value = _lookedUpClass;
            flags = _lookedUpFlags;
            length = _lookedUpLength;
        }
        else
        {
            value = Resolve(Unicode.DecodeAt(_text, index, out length), out flags);
        }

        var endsWithJoiner = value is LineBreak.ZWJ;
        index += length;

        if (value is not (LineBreak.MandatoryBreak or LineBreak.CarriageReturn or LineBreak.LineFeed or LineBreak.NextLine or LineBreak.Space or LineBreak.ZWSpace))
        {
            // LB9
            while (index < _text.Length)
            {
                // The printable ASCII characters are not combining marks: most text does not need the lookup.
                if (_text[index] is >= ' ' and <= '~')
                    break;

                var mark = Resolve(Unicode.DecodeAt(_text, index, out length), out var markFlags);
                if (mark is not (LineBreak.CombiningMark or LineBreak.ZWJ))
                {
                    _lookedUpIndex = index;
                    _lookedUpLength = length;
                    _lookedUpClass = mark;
                    _lookedUpFlags = markFlags;
                    break;
                }

                endsWithJoiner = mark is LineBreak.ZWJ;
                index += length;
            }

            if (value is LineBreak.CombiningMark or LineBreak.ZWJ)
            {
                // LB10: a mark that has no base behaves as the letter A.
                value = LineBreak.Alphabetic;
                flags = 0;
            }
        }

        if (_wordBreak is UnicodeWordBreakBehavior.BreakAll && value is LineBreak.Alphabetic or LineBreak.HebrewLetter or LineBreak.Numeric)
        {
            value = LineBreak.Ideographic;
        }

        return new Unit(value, flags, index, endsWithJoiner);
    }

    /// <summary>Applies LB1 to the class of a code point and extracts the properties the rules read with it.</summary>
    private readonly LineBreak Resolve(int codePoint, out byte flags)
    {
        var entry = (uint)codePoint < 0x80 ? ResolvedAsciiLineBreaks[codePoint] : ResolvedLineBreakTuples[UnicodeTables.GetLineBreak(codePoint)];
        flags = (byte)(entry >> 8);
        if (codePoint == 0x25CC)
        {
            flags |= DottedCircleFlag;
        }

        var value = (LineBreak)entry;
        if (value is LineBreak.ConditionalJapaneseStarter)
        {
            value = _strictness is UnicodeLineBreakStrictness.Strict ? LineBreak.Nonstarter : LineBreak.Ideographic;
        }

        return value;
    }

    // The entries of UnicodeTables.LineBreakTuples after LB1: the class, then the flags. Conditional_Japanese_Starter
    // depends on the strictness, and is resolved by Resolve. The array has an entry for every byte, so that a lookup
    // has no bounds check.
    private static readonly ushort[] ResolvedLineBreakTuples = CreateResolvedLineBreakTuples();

    // The entries of the ASCII characters, which need no lookup in the trie.
    private static readonly ushort[] ResolvedAsciiLineBreaks = CreateResolvedAsciiLineBreaks();

    private static ushort[] CreateResolvedLineBreakTuples()
    {
        var tuples = UnicodeTables.LineBreakTuples;
        var result = new ushort[256];
        for (var i = 0; i < tuples.Length / 2; i++)
        {
            // See UnicodeTables.GetLineBreak for the layout of the high byte.
            var properties = tuples[(i * 2) + 1];
            var width = (UnicodeEastAsianWidth)(properties & 0x7);
            var flags = (width is UnicodeEastAsianWidth.Fullwidth or UnicodeEastAsianWidth.Wide or UnicodeEastAsianWidth.Halfwidth ? EastAsianFlag : 0)
                | ((properties & 0x20) != 0 ? InitialQuoteFlag : 0)
                | ((properties & 0x40) != 0 ? FinalQuoteFlag : 0)
                | ((properties & 0x18) == 0x18 ? UnassignedPictographicFlag : 0);

            var value = (LineBreak)tuples[i * 2];
            if (value is LineBreak.ComplexContext)
            {
                flags |= ComplexContextFlag;
            }

            value = value switch
            {
                LineBreak.Ambiguous or LineBreak.Surrogate or LineBreak.Unknown => LineBreak.Alphabetic,
                LineBreak.ComplexContext => (properties & 0x80) != 0 ? LineBreak.CombiningMark : LineBreak.Alphabetic,
                _ => value,
            };

            result[i] = (ushort)((int)value | (flags << 8));
        }

        return result;
    }

    private static ushort[] CreateResolvedAsciiLineBreaks()
    {
        var result = new ushort[0x80];
        for (var c = 0; c < result.Length; c++)
        {
            result[c] = ResolvedLineBreakTuples[UnicodeTables.GetLineBreak(c)];
        }

        return result;
    }

    private enum Decision
    {
        NoBreak,
        Break,
        MandatoryBreak,
    }

    [InlineArray(LineBreakDictionary.PieceLength / 64)]
    private struct PieceBits
    {
        private ulong _element;
    }

    [StructLayout(LayoutKind.Auto)]
    private readonly struct Unit
    {
        private readonly byte _flags;

        public Unit(LineBreak value, byte flags, int end, bool endsWithJoiner)
        {
            Class = value;
            _flags = flags;
            End = end;
            EndsWithJoiner = endsWithJoiner;
        }

        public LineBreak Class { get; }

        public int End { get; }

        public bool EndsWithJoiner { get; }

        public bool Has(byte flag) => (_flags & flag) != 0;
    }
}
