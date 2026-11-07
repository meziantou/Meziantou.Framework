using System.Collections.Concurrent;
using System.Runtime.InteropServices;

namespace Meziantou.Framework.Json.Internals;

/// <summary>Translates an I-Regexp (RFC 9485) into an equivalent .NET regular expression.</summary>
/// <remarks>
/// <para>
/// RFC 9485 §4 gives I-Regexp the semantics of an XSD regular expression, so everything .NET layers on top of
/// that subset - <c>^</c> and <c>$</c> as anchors, <c>\d</c>, <c>(?:</c>, inline options, backreferences - is not
/// part of the language. Handing a pattern straight to <see cref="System.Text.RegularExpressions.Regex"/> would
/// therefore both accept patterns that are not I-Regexps and change the meaning of ones that are, so the pattern
/// is parsed against the RFC 9485 grammar and re-emitted rather than patched up.
/// </para>
/// <para>
/// An I-Regexp matches a sequence of Unicode scalar values whereas .NET matches a sequence of UTF-16 code units,
/// so every construct that matches one character - <c>.</c>, a character class, <c>\p{...}</c> - is emitted as an
/// alternation of the non-surrogate code units and the surrogate pairs it covers. That keeps a supplementary
/// character one unit of matching instead of two, and keeps a lone surrogate half from ever matching.
/// </para>
/// </remarks>
internal static class IRegexpTranslator
{
    private const int MaxScalarValue = 0x10FFFF;
    private const int FirstHighSurrogate = 0xD800;
    private const int LastHighSurrogate = 0xDBFF;
    private const int FirstLowSurrogate = 0xDC00;
    private const int LastLowSurrogate = 0xDFFF;
    private const int FirstSupplementary = 0x10000;
    private const int LastBmp = 0xFFFF;
    private const int HighSurrogateCount = LastHighSurrogate - FirstHighSurrogate + 1;

    /// <summary>
    /// Maximum group nesting. The translator recurses once per <c>(</c>, so an unbounded depth would overflow the
    /// stack, which cannot be caught and terminates the process. The value matches the nesting depth the JSONPath
    /// parser itself allows.
    /// </summary>
    private const int MaxGroupNestingDepth = 64;

    /// <summary>
    /// A character class that matches nothing, for a set that turned out to be empty. <c>(?!)</c> would read
    /// better but <see cref="System.Text.RegularExpressions.RegexOptions.NonBacktracking"/> rejects lookarounds.
    /// </summary>
    private const string EmptySetPattern = @"[^\u0000-\uFFFF]";

    /// <summary>What <c>.</c> matches: any scalar value but LF and CR (RFC 9485 §5.3).</summary>
    private static readonly ScalarRange[] DotRanges =
    [
        new(0x0000, 0x0009),
        new(0x000B, 0x000C),
        new(0x000E, MaxScalarValue),
    ];

    /// <summary>Scalar ranges of a <c>\p{...}</c> category, which cost a scan of the whole scalar space to build.</summary>
    private static readonly ConcurrentDictionary<(string CharProp, bool Complement), ScalarRange[]> CategoryRanges = new();

    /// <summary>Translates an I-Regexp into a .NET pattern.</summary>
    /// <param name="pattern">The I-Regexp, as written in the JSONPath query.</param>
    /// <param name="anchored">Whether the result must match the whole string (<c>match()</c>) or any substring (<c>search()</c>).</param>
    /// <returns>The .NET pattern.</returns>
    /// <exception cref="FormatException"><paramref name="pattern"/> is not a valid I-Regexp.</exception>
    public static string Translate(string pattern, bool anchored)
    {
        var builder = new StringBuilder(pattern.Length * 4);

        // RFC 9485 §5.4. \A and \z rather than ^ and $, which would also let a final line feed go unmatched.
        if (anchored)
        {
            builder.Append(@"\A(?:");
        }

        new Parser(pattern, builder).ParsePattern();

        if (anchored)
        {
            builder.Append(@")\z");
        }

        return builder.ToString();
    }

    private static int HighSurrogateOf(int scalar) => FirstHighSurrogate + ((scalar - FirstSupplementary) >> 10);

    private static int LowSurrogateOf(int scalar) => FirstLowSurrogate + ((scalar - FirstSupplementary) & 0x3FF);

    /// <summary>Appends a pattern matching exactly one scalar value of <paramref name="ranges"/>.</summary>
    /// <param name="builder">Receives the pattern.</param>
    /// <param name="ranges">The scalar values to match, in any order and possibly overlapping.</param>
    private static void EmitScalarSet(StringBuilder builder, IReadOnlyList<ScalarRange> ranges)
    {
        var normalized = NormalizeScalarRanges(ranges);
        var bmp = BuildBmpClass(normalized);
        var alternatives = BuildSupplementaryAlternatives(normalized);

        if (bmp is null && alternatives.Count is 0)
        {
            builder.Append(EmptySetPattern);
            return;
        }

        if (bmp is not null && alternatives.Count is 0)
        {
            // A single character class is already a quantifiable unit, so it needs no group of its own.
            builder.Append(bmp);
            return;
        }

        builder.Append("(?:");
        if (bmp is not null)
        {
            builder.Append(bmp).Append('|');
        }

        for (var i = 0; i < alternatives.Count; i++)
        {
            if (i > 0)
            {
                builder.Append('|');
            }

            builder.Append(alternatives[i]);
        }

        builder.Append(')');
    }

    /// <summary>Builds the character class for the BMP part of a normalized set, or <see langword="null"/> when it is empty.</summary>
    private static string? BuildBmpClass(List<ScalarRange> normalized)
    {
        var builder = new StringBuilder();
        foreach (var range in normalized)
        {
            if (range.Low > LastBmp)
            {
                break;
            }

            AppendClassRange(builder, range.Low, Math.Min(range.High, LastBmp));
        }

        return builder.Length is 0 ? null : $"[{builder}]";
    }

    /// <summary>
    /// Builds one alternative per run of high surrogates that share the same low surrogates. Emitting a range at a
    /// time instead would produce hundreds of alternatives for a category such as <c>\P{Lu}</c>, and the
    /// non-backtracking engine takes a long time to build an automaton out of those.
    /// </summary>
    private static List<string> BuildSupplementaryAlternatives(List<ScalarRange> normalized)
    {
        var alternatives = new List<string>();
        List<ScalarRange>?[]? lowSurrogates = null;

        foreach (var range in normalized)
        {
            if (range.High < FirstSupplementary)
            {
                continue;
            }

            lowSurrogates ??= new List<ScalarRange>?[HighSurrogateCount];

            var low = Math.Max(range.Low, FirstSupplementary);
            var firstHigh = HighSurrogateOf(low);
            var lastHigh = HighSurrogateOf(range.High);
            for (var high = firstHigh; high <= lastHigh; high++)
            {
                var lowStart = high == firstHigh ? LowSurrogateOf(low) : FirstLowSurrogate;
                var lowEnd = high == lastHigh ? LowSurrogateOf(range.High) : LastLowSurrogate;
                (lowSurrogates[high - FirstHighSurrogate] ??= []).Add(new ScalarRange(lowStart, lowEnd));
            }
        }

        if (lowSurrogates is null)
        {
            return alternatives;
        }

        string? runClass = null;
        var runStart = 0;
        for (var index = 0; index <= HighSurrogateCount; index++)
        {
            var current = index < HighSurrogateCount && lowSurrogates[index] is { } lows ? BuildClass(lows) : null;
            if (string.Equals(current, runClass, StringComparison.Ordinal))
            {
                continue;
            }

            if (runClass is not null)
            {
                alternatives.Add(BuildHighSurrogateClass(runStart, index - 1) + runClass);
            }

            runClass = current;
            runStart = index;
        }

        return alternatives;
    }

    private static string BuildHighSurrogateClass(int firstIndex, int lastIndex)
    {
        var builder = new StringBuilder();
        if (firstIndex == lastIndex)
        {
            AppendEscapedChar(builder, FirstHighSurrogate + firstIndex);
            return builder.ToString();
        }

        AppendClassRange(builder, FirstHighSurrogate + firstIndex, FirstHighSurrogate + lastIndex);
        return $"[{builder}]";
    }

    private static string BuildClass(List<ScalarRange> ranges)
    {
        var builder = new StringBuilder("[");
        foreach (var range in MergeScalarRanges(ranges))
        {
            AppendClassRange(builder, range.Low, range.High);
        }

        return builder.Append(']').ToString();
    }

    private static void AppendClassRange(StringBuilder builder, int low, int high)
    {
        AppendEscapedChar(builder, low);
        if (high == low)
        {
            return;
        }

        // A two-value range reads better spelled out than as a range.
        if (high > low + 1)
        {
            builder.Append('-');
        }

        AppendEscapedChar(builder, high);
    }

    private static void AppendEscapedChar(StringBuilder builder, int value)
    {
        if (value is (>= '0' and <= '9') or (>= 'A' and <= 'Z') or (>= 'a' and <= 'z'))
        {
            builder.Append((char)value);
            return;
        }

        builder.Append(CultureInfo.InvariantCulture, $@"\u{value:X4}");
    }

    /// <summary>Drops the surrogate code points, which are not scalar values, then sorts and merges.</summary>
    private static List<ScalarRange> NormalizeScalarRanges(IReadOnlyList<ScalarRange> ranges)
    {
        var scalars = new List<ScalarRange>(ranges.Count);
        foreach (var range in ranges)
        {
            AddScalarRange(scalars, range.Low, range.High);
        }

        return MergeScalarRanges(scalars);
    }

    private static List<ScalarRange> MergeScalarRanges(List<ScalarRange> ranges)
    {
        ranges.Sort(static (x, y) => x.Low.CompareTo(y.Low));

        var merged = new List<ScalarRange>(ranges.Count);
        foreach (var range in ranges)
        {
            if (merged.Count > 0 && range.Low <= merged[^1].High + 1)
            {
                if (range.High > merged[^1].High)
                {
                    merged[^1] = new ScalarRange(merged[^1].Low, range.High);
                }

                continue;
            }

            merged.Add(range);
        }

        return merged;
    }

    private static void AddScalarRange(List<ScalarRange> ranges, int low, int high)
    {
        if (low > LastLowSurrogate || high < FirstHighSurrogate)
        {
            ranges.Add(new ScalarRange(low, high));
            return;
        }

        if (low < FirstHighSurrogate)
        {
            ranges.Add(new ScalarRange(low, FirstHighSurrogate - 1));
        }

        if (high > LastLowSurrogate)
        {
            ranges.Add(new ScalarRange(LastLowSurrogate + 1, high));
        }
    }

    private static List<ScalarRange> ComplementScalarRanges(List<ScalarRange> ranges)
    {
        var normalized = NormalizeScalarRanges(ranges);
        var complement = new List<ScalarRange>(normalized.Count + 1);
        var next = 0;
        foreach (var range in normalized)
        {
            if (range.Low > next)
            {
                AddScalarRange(complement, next, range.Low - 1);
            }

            next = range.High + 1;
        }

        if (next <= MaxScalarValue)
        {
            AddScalarRange(complement, next, MaxScalarValue);
        }

        return complement;
    }

    private static ScalarRange[] GetCategoryRanges(string charProp, bool complement)
    {
        // The mask is resolved first so an unknown property never reaches the cache: a pattern taken from the
        // document could otherwise grow it without bound.
        var mask = GetCategoryMask(charProp);
        return CategoryRanges.GetOrAdd((charProp, complement), key => BuildCategoryRanges(mask, key.Complement));
    }

    private static ScalarRange[] BuildCategoryRanges(int mask, bool complement)
    {
        var ranges = new List<ScalarRange>();
        var start = -1;
        for (var scalar = 0; scalar <= MaxScalarValue; scalar++)
        {
            if (scalar is FirstHighSurrogate)
            {
                if (start >= 0)
                {
                    ranges.Add(new ScalarRange(start, FirstHighSurrogate - 1));
                    start = -1;
                }

                scalar = LastLowSurrogate;
                continue;
            }

            var member = (mask & (1 << (int)CharUnicodeInfo.GetUnicodeCategory(scalar))) is not 0;
            if (member != complement)
            {
                if (start < 0)
                {
                    start = scalar;
                }
            }
            else if (start >= 0)
            {
                ranges.Add(new ScalarRange(start, scalar - 1));
                start = -1;
            }
        }

        if (start >= 0)
        {
            ranges.Add(new ScalarRange(start, MaxScalarValue));
        }

        return [.. ranges];
    }

    /// <summary>Maps an <c>IsCategory</c> to the <see cref="UnicodeCategory"/> values it stands for.</summary>
    /// <param name="charProp">The text between <c>\p{</c> and <c>}</c>.</param>
    /// <returns>A bit mask of <see cref="UnicodeCategory"/> values.</returns>
    /// <exception cref="FormatException"><paramref name="charProp"/> is not an <c>IsCategory</c>.</exception>
    private static int GetCategoryMask(string charProp)
    {
        return charProp switch
        {
            "L" => Mask(UnicodeCategory.UppercaseLetter, UnicodeCategory.LowercaseLetter, UnicodeCategory.TitlecaseLetter, UnicodeCategory.ModifierLetter, UnicodeCategory.OtherLetter),
            "Lu" => Mask(UnicodeCategory.UppercaseLetter),
            "Ll" => Mask(UnicodeCategory.LowercaseLetter),
            "Lt" => Mask(UnicodeCategory.TitlecaseLetter),
            "Lm" => Mask(UnicodeCategory.ModifierLetter),
            "Lo" => Mask(UnicodeCategory.OtherLetter),
            "M" => Mask(UnicodeCategory.NonSpacingMark, UnicodeCategory.SpacingCombiningMark, UnicodeCategory.EnclosingMark),
            "Mn" => Mask(UnicodeCategory.NonSpacingMark),
            "Mc" => Mask(UnicodeCategory.SpacingCombiningMark),
            "Me" => Mask(UnicodeCategory.EnclosingMark),
            "N" => Mask(UnicodeCategory.DecimalDigitNumber, UnicodeCategory.LetterNumber, UnicodeCategory.OtherNumber),
            "Nd" => Mask(UnicodeCategory.DecimalDigitNumber),
            "Nl" => Mask(UnicodeCategory.LetterNumber),
            "No" => Mask(UnicodeCategory.OtherNumber),
            "P" => Mask(UnicodeCategory.ConnectorPunctuation, UnicodeCategory.DashPunctuation, UnicodeCategory.OpenPunctuation, UnicodeCategory.ClosePunctuation, UnicodeCategory.InitialQuotePunctuation, UnicodeCategory.FinalQuotePunctuation, UnicodeCategory.OtherPunctuation),
            "Pc" => Mask(UnicodeCategory.ConnectorPunctuation),
            "Pd" => Mask(UnicodeCategory.DashPunctuation),
            "Pe" => Mask(UnicodeCategory.ClosePunctuation),
            "Pf" => Mask(UnicodeCategory.FinalQuotePunctuation),
            "Pi" => Mask(UnicodeCategory.InitialQuotePunctuation),
            "Po" => Mask(UnicodeCategory.OtherPunctuation),
            "Ps" => Mask(UnicodeCategory.OpenPunctuation),
            "Z" => Mask(UnicodeCategory.SpaceSeparator, UnicodeCategory.LineSeparator, UnicodeCategory.ParagraphSeparator),
            "Zl" => Mask(UnicodeCategory.LineSeparator),
            "Zp" => Mask(UnicodeCategory.ParagraphSeparator),
            "Zs" => Mask(UnicodeCategory.SpaceSeparator),
            "S" => Mask(UnicodeCategory.MathSymbol, UnicodeCategory.CurrencySymbol, UnicodeCategory.ModifierSymbol, UnicodeCategory.OtherSymbol),
            "Sc" => Mask(UnicodeCategory.CurrencySymbol),
            "Sk" => Mask(UnicodeCategory.ModifierSymbol),
            "Sm" => Mask(UnicodeCategory.MathSymbol),
            "So" => Mask(UnicodeCategory.OtherSymbol),
            "C" => Mask(UnicodeCategory.Control, UnicodeCategory.Format, UnicodeCategory.Surrogate, UnicodeCategory.PrivateUse, UnicodeCategory.OtherNotAssigned),
            "Cc" => Mask(UnicodeCategory.Control),
            "Cf" => Mask(UnicodeCategory.Format),
            "Cn" => Mask(UnicodeCategory.OtherNotAssigned),
            "Co" => Mask(UnicodeCategory.PrivateUse),
            _ => throw new FormatException($"'{charProp}' is not a Unicode category an I-Regexp can name."),
        };

        static int Mask(params ReadOnlySpan<UnicodeCategory> categories)
        {
            var mask = 0;
            foreach (var category in categories)
            {
                mask |= 1 << (int)category;
            }

            return mask;
        }
    }

    [StructLayout(LayoutKind.Auto)]
    private readonly struct ScalarRange
    {
        public ScalarRange(int low, int high)
        {
            Low = low;
            High = high;
        }

        public int Low { get; }

        public int High { get; }
    }

    /// <summary>A recursive-descent parser over the RFC 9485 §3 grammar that emits the .NET pattern as it goes.</summary>
    private sealed class Parser
    {
        private readonly string _pattern;
        private readonly StringBuilder _builder;
        private int _position;

        public Parser(string pattern, StringBuilder builder)
        {
            _pattern = pattern;
            _builder = builder;
        }

        public void ParsePattern()
        {
            ParseAlternation(depth: 0);
            if (_position < _pattern.Length)
            {
                throw Invalid($"'{_pattern[_position]}' is not allowed here");
            }
        }

        // i-regexp = branch *( "|" branch )
        private void ParseAlternation(int depth)
        {
            ParseBranch(depth);
            while (Peek() is '|')
            {
                _position++;
                _builder.Append('|');
                ParseBranch(depth);
            }
        }

        // branch = *piece
        private void ParseBranch(int depth)
        {
            while (Peek() is not (null or '|' or ')'))
            {
                ParseAtom(depth);
                ParseQuantifier();
            }
        }

        // atom = NormalChar / charClass / ( "(" i-regexp ")" )
        private void ParseAtom(int depth)
        {
            var ch = _pattern[_position];
            switch (ch)
            {
                case '(':
                    if (depth >= MaxGroupNestingDepth)
                    {
                        throw Invalid($"group nesting exceeds the maximum depth of {MaxGroupNestingDepth}");
                    }

                    _position++;
                    _builder.Append("(?:");
                    ParseAlternation(depth + 1);
                    Expect(')');
                    _builder.Append(')');
                    break;

                case '.':
                    _position++;
                    EmitScalarSet(_builder, DotRanges);
                    break;

                case '\\':
                    ParseEscapedAtom();
                    break;

                case '[':
                    ParseCharClassExpression();
                    break;

                // The characters NormalChar leaves out. "(" and "[" are handled above and the rest can only
                // appear escaped, or - for a quantifier - after an atom.
                case ')' or '*' or '+' or '?' or ']' or '{' or '|' or '}':
                    throw Invalid($"'{ch}' is not allowed here");

                default:
                    EmitLiteral(ReadScalarValue());
                    break;
            }
        }

        // charClass = SingleCharEsc / charClassEsc, for the escapes that start an atom
        private void ParseEscapedAtom()
        {
            if (PeekAt(1) is not { } escaped)
            {
                throw Invalid("a trailing '\\' is not an escape");
            }

            if (escaped is 'p' or 'P')
            {
                EmitScalarSet(_builder, ReadCategoryEscape());
                return;
            }

            _position += 2;
            EmitLiteral(TranslateSingleCharEscape(escaped));
        }

        // catEsc = "\p{" charProp "}" / complEsc = "\P{" charProp "}"
        private ScalarRange[] ReadCategoryEscape()
        {
            var complement = _pattern[_position + 1] is 'P';
            _position += 2;
            Expect('{');

            var start = _position;
            while (Peek() is not (null or '}'))
            {
                _position++;
            }

            if (Peek() is null)
            {
                throw Invalid("the category escape is not closed by '}'");
            }

            var charProp = _pattern[start.._position];
            _position++;
            return GetCategoryRanges(charProp, complement);
        }

        // charClassExpr = "[" [ "^" ] ( "-" / CCE1 ) *CCE1 [ "-" ] "]"
        private void ParseCharClassExpression()
        {
            _position++;
            var negated = Peek() is '^';
            if (negated)
            {
                _position++;
            }

            var ranges = new List<ScalarRange>();
            var items = 0;
            while (true)
            {
                if (Peek() is not { } ch)
                {
                    throw Invalid("the character class is not closed by ']'");
                }

                if (ch is ']')
                {
                    if (items is 0)
                    {
                        throw Invalid("an empty character class matches nothing");
                    }

                    _position++;
                    break;
                }

                if (ch is '-')
                {
                    // A "-" stands for itself only as the first or as the last item of the class.
                    if (items is not 0 && PeekAt(1) is not ']')
                    {
                        throw Invalid("'-' is not allowed here");
                    }

                    _position++;
                    ranges.Add(new ScalarRange('-', '-'));
                    items++;
                    continue;
                }

                if (ch is '\\' && PeekAt(1) is 'p' or 'P')
                {
                    ranges.AddRange(ReadCategoryEscape());
                    items++;
                    continue;
                }

                var low = ReadCharClassChar();
                var high = low;
                if (Peek() is '-' && PeekAt(1) is not (null or ']'))
                {
                    _position++;
                    high = ReadCharClassChar();
                    if (high < low)
                    {
                        throw Invalid("the range ends below where it starts");
                    }
                }

                ranges.Add(new ScalarRange(low, high));
                items++;
            }

            EmitScalarSet(_builder, negated ? ComplementScalarRanges(ranges) : ranges);
        }

        // CCchar = ( %x00-2C / %x2E-5A / %x5E-D7FF / %xE000-10FFFF ) / SingleCharEsc
        private int ReadCharClassChar()
        {
            var ch = _pattern[_position];
            if (ch is '\\')
            {
                if (PeekAt(1) is not { } escaped)
                {
                    throw Invalid("a trailing '\\' is not an escape");
                }

                _position += 2;
                return TranslateSingleCharEscape(escaped);
            }

            if (ch is '[')
            {
                throw Invalid("'[' has to be escaped inside a character class");
            }

            // CCchar leaves "-" out: unescaped, it only stands for itself as the first or the last item of a
            // class, never as the end of a range.
            if (ch is '-')
            {
                throw Invalid("'-' has to be escaped to end a range");
            }

            return ReadScalarValue();
        }

        // quantifier = ( "*" / "+" / "?" ) / range-quantifier
        private void ParseQuantifier()
        {
            switch (Peek())
            {
                case '*' or '+' or '?':
                    _builder.Append(_pattern[_position]);
                    _position++;
                    break;

                case '{':
                    ParseRangeQuantifier();
                    break;
            }
        }

        // range-quantifier = "{" QuantExact [ "," [ QuantExact ] ] "}"
        private void ParseRangeQuantifier()
        {
            _position++;
            var min = ReadQuantExact();
            int? max = min;
            if (Peek() is ',')
            {
                _position++;
                max = Peek() is '}' ? null : ReadQuantExact();
            }

            Expect('}');

            if (max < min)
            {
                throw Invalid($"the quantifier {{{min},{max}}} has a maximum below its minimum");
            }

            _builder.Append('{').Append(min);
            if (max is null)
            {
                _builder.Append(',');
            }
            else if (max.GetValueOrDefault() != min)
            {
                _builder.Append(',').Append(max.GetValueOrDefault());
            }

            _builder.Append('}');
        }

        // QuantExact = 1*%x30-39
        private int ReadQuantExact()
        {
            var start = _position;
            while (Peek() is >= '0' and <= '9')
            {
                _position++;
            }

            if (_position == start)
            {
                throw Invalid("the quantifier needs a number");
            }

            if (!int.TryParse(_pattern.AsSpan(start, _position - start), NumberStyles.None, CultureInfo.InvariantCulture, out var value))
            {
                throw Invalid("the quantifier is out of range");
            }

            return value;
        }

        /// <summary>Reads one scalar value, which the pattern holds as either one char or one surrogate pair.</summary>
        private int ReadScalarValue()
        {
            var ch = _pattern[_position];
            if (char.IsHighSurrogate(ch) && PeekAt(1) is { } next && char.IsLowSurrogate(next))
            {
                _position += 2;
                return char.ConvertToUtf32(ch, next);
            }

            if (char.IsSurrogate(ch))
            {
                // The grammar skips the surrogate code points: an unpaired one is not a scalar value.
                throw Invalid("an unpaired surrogate is not a Unicode scalar value");
            }

            _position++;
            return ch;
        }

        // SingleCharEsc = "\" ( %x28-2B / "-" / "." / "?" / %x5B-5E / "n" / "r" / "t" / %x7B-7D )
        private char TranslateSingleCharEscape(char escaped)
        {
            return escaped switch
            {
                '(' or ')' or '*' or '+' or '-' or '.' or '?' or '[' or '\\' or ']' or '^' or '{' or '|' or '}' => escaped,
                'n' => '\n',
                'r' => '\r',
                't' => '\t',
                _ => throw Invalid($"'\\{escaped}' is not an I-Regexp escape"),
            };
        }

        private void EmitLiteral(int scalar)
        {
            if (scalar <= LastBmp)
            {
                AppendEscapedChar(_builder, scalar);
                return;
            }

            // The pair has to stay one unit so that a quantifier applies to the whole scalar value.
            _builder.Append("(?:");
            AppendEscapedChar(_builder, HighSurrogateOf(scalar));
            AppendEscapedChar(_builder, LowSurrogateOf(scalar));
            _builder.Append(')');
        }

        private void Expect(char expected)
        {
            if (Peek() != expected)
            {
                throw Invalid($"'{expected}' is missing");
            }

            _position++;
        }

        private char? Peek() => PeekAt(0);

        private char? PeekAt(int offset) => _position + offset < _pattern.Length ? _pattern[_position + offset] : null;

        private FormatException Invalid(string reason) => new($"The I-Regexp is not valid at position {_position}: {reason}.");
    }
}
