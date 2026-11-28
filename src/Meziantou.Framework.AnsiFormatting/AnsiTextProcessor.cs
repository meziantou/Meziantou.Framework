using System.Buffers;

namespace Meziantou.Framework;

public static class AnsiTextProcessor
{
    private const char EscapeCharacter = '\u001b';
    private const char BellCharacter = '\u0007';
    // Sequences shorter than this are processed using a stack-allocated buffer to avoid renting an array.
    private const int StackAllocThreshold = 512;
    // The values of SGR sequences shorter than this are parsed into a stack-allocated buffer.
    private const int SgrParametersStackAllocThreshold = 32;
    // The longest SGR parameter is 38:2:<color-space>:r:g:b
    private const int MaxSgrSubParameters = 6;

    // AnsiColor is immutable, so the parser shares one instance per indexed color.
    private static readonly AnsiColor?[] IndexedColors = new AnsiColor?[256];

    public static string RemoveAnsiSequences(string value)
    {
        ArgumentNullException.ThrowIfNull(value);

        var firstEscapeIndex = value.AsSpan().IndexOf(EscapeCharacter);
        if (firstEscapeIndex < 0)
            return value;

        return RemoveAnsiSequencesCore(value.AsSpan(), firstEscapeIndex, originalIfUnchanged: value);
    }

    public static string RemoveAnsiSequences(ReadOnlySpan<char> value)
    {
        var firstEscapeIndex = value.IndexOf(EscapeCharacter);
        if (firstEscapeIndex < 0)
            return value.ToString();

        return RemoveAnsiSequencesCore(value, firstEscapeIndex, originalIfUnchanged: null);
    }

    public static bool ContainsAnsiSequences(string value)
    {
        ArgumentNullException.ThrowIfNull(value);

        return ContainsAnsiSequences(value.AsSpan());
    }

    public static bool ContainsAnsiSequences(ReadOnlySpan<char> value)
    {
        var searchFrom = 0;
        var oscTerminatorMissing = false;
        while (true)
        {
            var relativeIndex = value[searchFrom..].IndexOf(EscapeCharacter);
            if (relativeIndex < 0)
                return false;

            var escapeIndex = searchFrom + relativeIndex;
            if (escapeIndex + 1 >= value.Length)
                return false;

            // Only a sequence that reaches its terminator is one RemoveAnsiSequences would strip.
            switch (value[escapeIndex + 1])
            {
                case '[':
                    // '[' and ']' end a CSI sequence: when this one has no terminator, no later sequence can start.
                    return FindCsiTerminator(value, escapeIndex + 2) >= 0;

                case ']' when !oscTerminatorMissing:
                    if (FindOscTerminator(value, escapeIndex + 2) >= 0)
                        return true;

                    // A later OSC sequence would search a part of the text this search already went through.
                    oscTerminatorMissing = true;
                    break;
            }

            searchFrom = escapeIndex + 1;
        }
    }

    public static AnsiText ParseTextWithAnsiStyles(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        var runs = new List<AnsiTextRun>();
        var firstEscapeIndex = text.AsSpan().IndexOf(EscapeCharacter);
        if (firstEscapeIndex < 0)
        {
            AddRun(runs, AnsiStyle.None, 0, text.Length);
            return new AnsiText(text, runs);
        }

        return ParseTextWithAnsiStylesCore(text, firstEscapeIndex, runs);
    }

    private static AnsiText ParseTextWithAnsiStylesCore(string text, int firstEscapeIndex, List<AnsiTextRun> runs)
    {
        char[]? rented = null;
        var buffer = text.Length <= StackAllocThreshold
            ? stackalloc char[StackAllocThreshold]
            : (rented = ArrayPool<char>.Shared.Rent(text.Length));

        try
        {
            var value = text.AsSpan();
            var currentState = default(SgrState);
            var currentStyle = AnsiStyle.None;
            var currentRunStart = 0;
            var written = 0;
            var copiedFrom = 0;
            var searchFrom = firstEscapeIndex;
            var oscTerminatorMissing = false;

            while (true)
            {
                var relativeIndex = value[searchFrom..].IndexOf(EscapeCharacter);
                if (relativeIndex < 0)
                    break;

                var escapeIndex = searchFrom + relativeIndex;
                if (escapeIndex + 1 >= value.Length)
                    break;

                int terminator;
                var introducer = value[escapeIndex + 1];
                if (introducer is '[')
                {
                    terminator = FindCsiTerminator(value, escapeIndex + 2);

                    // '[' and ']' end a CSI sequence: when this one has no terminator, no later sequence can start.
                    if (terminator < 0)
                        break;
                }
                else if (introducer is ']' && !oscTerminatorMissing)
                {
                    // OSC sequences carry no styling, so they are consumed and dropped from the text.
                    terminator = FindOscTerminator(value, escapeIndex + 2);
                    if (terminator < 0)
                    {
                        // A later OSC sequence would search a part of the text this search already went through.
                        oscTerminatorMissing = true;
                        searchFrom = escapeIndex + 1;
                        continue;
                    }
                }
                else
                {
                    // Lone ESC, or a sequence cut off before its terminator: kept as-is.
                    searchFrom = escapeIndex + 1;
                    continue;
                }

                var segment = value[copiedFrom..escapeIndex];
                segment.CopyTo(buffer[written..]);
                written += segment.Length;
                copiedFrom = terminator + 1;
                searchFrom = terminator + 1;

                if (introducer is '[' && value[terminator] is 'm')
                {
                    var updatedState = currentState;
                    ApplySgr(ref updatedState, value[(escapeIndex + 2)..terminator]);
                    if (updatedState != currentState)
                    {
                        AddRun(runs, currentStyle, currentRunStart, written);
                        currentRunStart = written;
                        currentStyle = CreateStyle(updatedState, currentState, currentStyle);
                        currentState = updatedState;
                    }
                }
            }

            string result;
            if (copiedFrom is 0)
            {
                written = text.Length;
                result = text;
            }
            else
            {
                var tail = value[copiedFrom..];
                tail.CopyTo(buffer[written..]);
                written += tail.Length;
                result = new string(buffer[..written]);
            }

            AddRun(runs, currentStyle, currentRunStart, written);
            return new AnsiText(result, runs);
        }
        finally
        {
            if (rented is not null)
                ArrayPool<char>.Shared.Return(rented);
        }
    }

    private static string RemoveAnsiSequencesCore(ReadOnlySpan<char> value, int firstEscapeIndex, string? originalIfUnchanged)
    {
        char[]? rented = null;
        var buffer = value.Length <= StackAllocThreshold
            ? stackalloc char[StackAllocThreshold]
            : (rented = ArrayPool<char>.Shared.Rent(value.Length));

        try
        {
            var written = 0;
            var copiedFrom = 0;
            var searchFrom = firstEscapeIndex;
            var removedAny = false;

            while (true)
            {
                var relativeIndex = value[searchFrom..].IndexOf(EscapeCharacter);
                if (relativeIndex < 0)
                    break;

                var escapeIndex = searchFrom + relativeIndex;

                // Removable sequences are CSI (ESC '[' ... <terminator in 0x40-0x7E>)
                // and OSC (ESC ']' ... BEL or ST).
                if (escapeIndex + 1 < value.Length && value[escapeIndex + 1] is '[' or ']')
                {
                    var terminator = FindSequenceEnd(value, escapeIndex);
                    if (terminator >= 0)
                    {
                        var segment = value[copiedFrom..escapeIndex];
                        segment.CopyTo(buffer[written..]);
                        written += segment.Length;

                        copiedFrom = terminator + 1;
                        searchFrom = terminator + 1;
                        removedAny = true;
                        continue;
                    }

                    // Incomplete sequence with no terminator: the rest of the string is kept as-is.
                    break;
                }

                // Lone ESC (not the start of a CSI or OSC sequence): kept as-is.
                searchFrom = escapeIndex + 1;
            }

            if (!removedAny)
                return originalIfUnchanged ?? value.ToString();

            var tail = value[copiedFrom..];
            tail.CopyTo(buffer[written..]);
            written += tail.Length;

            return new string(buffer[..written]);
        }
        finally
        {
            if (rented is not null)
                ArrayPool<char>.Shared.Return(rented);
        }
    }

    private static void ApplySgr(ref SgrState state, ReadOnlySpan<char> sequenceContent)
    {
        // ESC[m, which carries no parameter at all, resets the style like ESC[0m.
        if (sequenceContent.IsEmpty)
        {
            state = default;
            return;
        }

        // A parameter p produces at most p.Length + 1 values, so the sequence produces at most sequenceContent.Length + 1 values.
        var maxCount = sequenceContent.Length + 1;
        int[]? rented = null;
        var parameters = maxCount <= SgrParametersStackAllocThreshold
            ? stackalloc int[SgrParametersStackAllocThreshold]
            : (rented = ArrayPool<int>.Shared.Rent(maxCount));

        try
        {
            // Parameters are separated by ';', and the sub-parameters of a parameter (ITU T.416) by ':'.
            Span<int> parts = stackalloc int[MaxSgrSubParameters];
            var partCount = 0;
            var ignoreParameter = false;
            var count = 0;
            var start = 0;
            while (true)
            {
                var end = start;
                while (end < sequenceContent.Length && sequenceContent[end] is not (':' or ';'))
                {
                    end++;
                }

                if (!ignoreParameter)
                {
                    // More sub-parameters than any SGR parameter uses, or a value this parser does not understand:
                    // the whole parameter is ignored rather than reset the style.
                    if (partCount < parts.Length && TryParseSgrValue(sequenceContent[start..end], out var value))
                    {
                        parts[partCount++] = value;
                    }
                    else
                    {
                        ignoreParameter = true;
                    }
                }

                if (end == sequenceContent.Length || sequenceContent[end] is ';')
                {
                    if (!ignoreParameter)
                    {
                        AddSgrParameter(parameters, ref count, parts[..partCount]);
                    }

                    if (end == sequenceContent.Length)
                        break;

                    partCount = 0;
                    ignoreParameter = false;
                }

                start = end + 1;
            }

            // When the sequence carries parameters but none of them could be parsed, it is ignored:
            // a reset would drop styling the caller never asked to clear.
            ApplySgrParameters(ref state, parameters[..count]);
        }
        finally
        {
            if (rented is not null)
                ArrayPool<int>.Shared.Return(rented);
        }
    }

    /// <summary>
    /// Reads a value of an SGR parameter as <c>int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out _)</c>
    /// does: ASCII digits, which may be followed by NUL characters, and no overflow. An empty value is 0.
    /// </summary>
    private static bool TryParseSgrValue(ReadOnlySpan<char> text, out int value)
    {
        value = 0;
        var index = 0;
        for (; index < text.Length; index++)
        {
            var digit = (uint)(text[index] - '0');
            if (digit > 9)
                break;

            if (value >= int.MaxValue / 10 && (value > int.MaxValue / 10 || digit > int.MaxValue % 10))
                return false;

            value = (value * 10) + (int)digit;
        }

        if (index is 0)
            return text.IsEmpty;

        // Like int.TryParse, ignore trailing NUL characters
        return text[index..].IndexOfAnyExcept('\0') < 0;
    }

    /// <summary>
    /// Normalizes the values of a single ';'-delimited SGR parameter, which may carry ':'-delimited sub-parameters
    /// (ITU T.416), into the flat form the rest of the parser consumes.
    /// </summary>
    private static void AddSgrParameter(Span<int> values, ref int valueCount, ReadOnlySpan<int> parts)
    {
        var count = parts.Length;

        // No sub-parameters: the classic ';'-only form, which the rest of the parser reads directly.
        if (count is 1)
        {
            values[valueCount++] = parts[0];
            return;
        }

        // Sub-parameters belong to the parameter that introduces them. Only the extended-color
        // parameters consume them; for anything else (for example 4:3, curly underline) the base
        // parameter is applied and the sub-parameters are ignored.
        if (parts[0] is not (38 or 48))
        {
            values[valueCount++] = parts[0];
            return;
        }

        // 38:5:n and 48:5:n
        if (count is 3 && parts[1] is 5)
        {
            parts[..3].CopyTo(values[valueCount..]);
            valueCount += 3;
            return;
        }

        // 38:2:r:g:b, and 38:2:<color-space>:r:g:b which carries an extra leading id to skip
        if (parts[1] is 2 && count is 5 or 6)
        {
            var red = count is 6 ? 3 : 2;
            values[valueCount] = parts[0];
            values[valueCount + 1] = parts[1];
            parts.Slice(red, 3).CopyTo(values[(valueCount + 2)..]);
            valueCount += 5;
        }
    }

    private static void ApplySgrParameters(ref SgrState state, ReadOnlySpan<int> parameters)
    {
        for (var i = 0; i < parameters.Length; i++)
        {
            var parameter = parameters[i];
            switch (parameter)
            {
                case 0:
                    state = default;
                    break;
                case 1:
                    state.Bold = true;
                    break;
                case 3:
                    state.Italic = true;
                    break;
                case 4:
                    state.Underline = true;
                    break;
                case 7:
                    state.Inverse = true;
                    break;
                case 22:
                    state.Bold = false;
                    break;
                case 23:
                    state.Italic = false;
                    break;
                case 24:
                    state.Underline = false;
                    break;
                case 27:
                    state.Inverse = false;
                    break;
                case 39:
                    state.Foreground = SgrState.NoColor;
                    break;
                case 49:
                    state.Background = SgrState.NoColor;
                    break;
                case >= 30 and <= 37:
                    state.Foreground = SgrState.Indexed(parameter - 30);
                    break;
                case >= 40 and <= 47:
                    state.Background = SgrState.Indexed(parameter - 40);
                    break;
                case >= 90 and <= 97:
                    state.Foreground = SgrState.Indexed(8 + parameter - 90);
                    break;
                case >= 100 and <= 107:
                    state.Background = SgrState.Indexed(8 + parameter - 100);
                    break;
                case 38:
                    state.Foreground = ReadExtendedColor(parameters, ref i, state.Foreground);
                    break;
                case 48:
                    state.Background = ReadExtendedColor(parameters, ref i, state.Background);
                    break;
            }
        }
    }

    // Returns the color an extended-color parameter (38 or 48) sets, or currentColor when the parameter is invalid,
    // and moves index to the last value the parameter consumes.
    private static int ReadExtendedColor(ReadOnlySpan<int> parameters, ref int index, int currentColor)
    {
        if (index + 1 >= parameters.Length)
            return currentColor;

        var mode = parameters[index + 1];
        if (mode is 5)
        {
            if (index + 2 < parameters.Length)
            {
                var colorIndex = parameters[index + 2];
                index += 2;
                return colorIndex is >= 0 and <= 255 ? SgrState.Indexed(colorIndex) : currentColor;
            }

            index++;
            return currentColor;
        }

        if (mode is 2)
        {
            if (index + 4 < parameters.Length)
            {
                var red = parameters[index + 2];
                var green = parameters[index + 3];
                var blue = parameters[index + 4];
                index += 4;
                return red is >= 0 and <= 255 && green is >= 0 and <= 255 && blue is >= 0 and <= 255
                    ? SgrState.Rgb(red, green, blue)
                    : currentColor;
            }

            index++;
            return currentColor;
        }

        index++;
        return currentColor;
    }

    // Creates the style of a state, reusing the colors of the previous style when they did not change.
    private static AnsiStyle CreateStyle(SgrState state, SgrState previousState, AnsiStyle previousStyle)
    {
        if (state == default)
            return AnsiStyle.None;

        var foreground = state.Foreground == previousState.Foreground ? previousStyle.Foreground : CreateColor(state.Foreground);
        var background = state.Background == previousState.Background ? previousStyle.Background : CreateColor(state.Background);
        return new AnsiStyle(foreground, background, state.Bold, state.Italic, state.Underline, state.Inverse);
    }

    private static AnsiColor? CreateColor(int color)
    {
        if (color is SgrState.NoColor)
            return null;

        if ((color & SgrState.RgbFlag) is not 0)
            return AnsiColor.FromRgb((byte)(color >> 16), (byte)(color >> 8), (byte)color);

        var index = color & 0xFF;
        return IndexedColors[index] ??= AnsiColor.FromIndexed(index);
    }

    /// <summary>
    /// Returns the index of the last character of the sequence introduced at <paramref name="escapeIndex"/>,
    /// or -1 when the introducer is unknown or the sequence is cut off before its terminator.
    /// </summary>
    private static int FindSequenceEnd(ReadOnlySpan<char> value, int escapeIndex)
    {
        if (escapeIndex + 1 >= value.Length)
            return -1;

        return value[escapeIndex + 1] switch
        {
            '[' => FindCsiTerminator(value, escapeIndex + 2),
            ']' => FindOscTerminator(value, escapeIndex + 2),
            _ => -1,
        };
    }

    // OSC sequences are terminated by BEL, or by ST which is written as ESC '\'.
    private static int FindOscTerminator(ReadOnlySpan<char> value, int start)
    {
        for (var i = start; i < value.Length; i++)
        {
            if (value[i] is BellCharacter)
                return i;

            if (value[i] is EscapeCharacter && i + 1 < value.Length && value[i + 1] is '\\')
                return i + 1;
        }

        return -1;
    }

    // CSI sequences are a few characters long: a scalar loop is faster than a vectorized search there.
    private static int FindCsiTerminator(ReadOnlySpan<char> value, int start)
    {
        for (var i = start; i < value.Length; i++)
        {
            if (value[i] is >= (char)0x40 and <= (char)0x7E)
                return i;
        }

        return -1;
    }

    private static void AddRun(List<AnsiTextRun> runs, AnsiStyle style, int start, int end)
    {
        if (end <= start)
            return;

        // A style that comes back after a style without text (red, blue, red) continues the run before it
        if (runs.Count > 0 && runs[^1] is { } previous && previous.End == start && previous.Style == style)
        {
            runs[^1] = previous with { End = end };
            return;
        }

        runs.Add(new AnsiTextRun(start, end, style));
    }

    // The style while parsing, without allocations. A color is NoColor (the default color), an indexed color, or an RGB color.
    private record struct SgrState
    {
        public const int NoColor = 0;
        public const int IndexedFlag = 1 << 24;
        public const int RgbFlag = 1 << 25;

        public int Foreground;
        public int Background;
        public bool Bold;
        public bool Italic;
        public bool Underline;
        public bool Inverse;

        public static int Indexed(int index) => IndexedFlag | index;

        public static int Rgb(int red, int green, int blue) => RgbFlag | (red << 16) | (green << 8) | blue;
    }

    public sealed record AnsiText(string Text, IReadOnlyList<AnsiTextRun> Runs);

    public sealed record AnsiTextRun(int Start, int End, AnsiStyle Style);

    public sealed record AnsiStyle(AnsiColor? Foreground, AnsiColor? Background, bool Bold, bool Italic, bool Underline, bool Inverse)
    {
        public static AnsiStyle None { get; } = new(Foreground: null, Background: null, Bold: false, Italic: false, Underline: false, Inverse: false);
    }

    public sealed record AnsiColor(AnsiColorKind Kind, byte Red, byte Green, byte Blue, byte IndexedValue)
    {
        public static AnsiColor FromIndexed(int value)
        {
            ArgumentOutOfRangeException.ThrowIfNegative(value);
            ArgumentOutOfRangeException.ThrowIfGreaterThan(value, 255);

            return new AnsiColor(AnsiColorKind.Indexed, Red: 0, Green: 0, Blue: 0, IndexedValue: (byte)value);
        }

        public static AnsiColor FromRgb(byte red, byte green, byte blue)
        {
            return new AnsiColor(AnsiColorKind.Rgb, red, green, blue, IndexedValue: 0);
        }
    }

    public enum AnsiColorKind
    {
        Indexed,
        Rgb,
    }
}
