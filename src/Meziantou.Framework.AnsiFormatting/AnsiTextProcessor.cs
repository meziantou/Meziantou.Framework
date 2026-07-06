using System.Buffers;

namespace Meziantou.Framework;

public static class AnsiTextProcessor
{
    private const char EscapeCharacter = '\u001b';
    // Sequences shorter than this are processed using a stack-allocated buffer to avoid renting an array.
    private const int StackAllocThreshold = 512;

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
        var i = 0;
        while (i < value.Length)
        {
            var escapeIndex = value[i..].IndexOf(EscapeCharacter);
            if (escapeIndex < 0)
                return false;

            escapeIndex += i;
            if (escapeIndex + 1 < value.Length && value[escapeIndex + 1] == '[')
                return true;

            i = escapeIndex + 1;
        }

        return false;
    }

    public static AnsiText ParseTextWithAnsiStyles(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        var builder = new StringBuilder(text.Length);
        var runs = new List<AnsiTextRun>();
        var currentStyle = AnsiStyle.None;
        var currentRunStart = 0;
        var index = 0;

        while (index < text.Length)
        {
            if (TryConsumeControlSequence(text, ref index, out var sgrParameters))
            {
                if (sgrParameters is not null)
                {
                    var updatedStyle = ApplySgr(currentStyle, sgrParameters);
                    if (updatedStyle != currentStyle)
                    {
                        AddRun(runs, currentStyle, currentRunStart, builder.Length);
                        currentRunStart = builder.Length;
                        currentStyle = updatedStyle;
                    }
                }

                continue;
            }

            builder.Append(text[index]);
            index++;
        }

        AddRun(runs, currentStyle, currentRunStart, builder.Length);

        if (runs.Count is 0 && builder.Length > 0)
        {
            runs.Add(new AnsiTextRun(0, builder.Length, AnsiStyle.None));
        }

        return new AnsiText(builder.ToString(), runs);
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

                // A removable sequence is a CSI: ESC '[' ... <terminator in 0x40-0x7E>
                if (escapeIndex + 1 < value.Length && value[escapeIndex + 1] == '[')
                {
                    var terminator = FindCsiTerminator(value, escapeIndex + 2);
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

                // Lone ESC (not part of a CSI sequence): kept as-is.
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

    private static bool TryConsumeControlSequence(string text, ref int index, out List<int>? sgrParameters)
    {
        sgrParameters = null;

        if (index >= text.Length || text[index] != EscapeCharacter)
            return false;

        if (index + 1 >= text.Length || text[index + 1] != '[')
            return false;

        var sequenceStart = index;
        index += 2;

        while (index < text.Length)
        {
            var current = text[index];
            if (current is >= '@' and <= '~')
            {
                var sequenceContent = text.AsSpan(sequenceStart + 2, index - sequenceStart - 2);
                if (current is 'm')
                {
                    sgrParameters = ParseSgrParameters(sequenceContent);
                }

                index++;
                return true;
            }

            index++;
        }

        index = sequenceStart;
        return false;
    }

    private static List<int> ParseSgrParameters(ReadOnlySpan<char> sequenceContent)
    {
        if (sequenceContent.IsEmpty)
            return [0];

        var values = new List<int>();
        var segmentStart = 0;

        for (var i = 0; i <= sequenceContent.Length; i++)
        {
            if (i < sequenceContent.Length && sequenceContent[i] != ';')
                continue;

            var segment = sequenceContent[segmentStart..i];
            if (segment.IsEmpty)
            {
                values.Add(0);
            }
            else if (int.TryParse(segment, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value))
            {
                values.Add(value);
            }

            segmentStart = i + 1;
        }

        return values;
    }

    private static AnsiStyle ApplySgr(AnsiStyle style, List<int> parameters)
    {
        if (parameters.Count is 0)
            return AnsiStyle.None;

        var result = style;

        for (var i = 0; i < parameters.Count; i++)
        {
            var parameter = parameters[i];
            switch (parameter)
            {
                case 0:
                    result = AnsiStyle.None;
                    break;
                case 1:
                    result = result with { Bold = true };
                    break;
                case 3:
                    result = result with { Italic = true };
                    break;
                case 4:
                    result = result with { Underline = true };
                    break;
                case 7:
                    result = result with { Inverse = true };
                    break;
                case 22:
                    result = result with { Bold = false };
                    break;
                case 23:
                    result = result with { Italic = false };
                    break;
                case 24:
                    result = result with { Underline = false };
                    break;
                case 27:
                    result = result with { Inverse = false };
                    break;
                case 39:
                    result = result with { Foreground = null };
                    break;
                case 49:
                    result = result with { Background = null };
                    break;
                case >= 30 and <= 37:
                    result = result with { Foreground = AnsiColor.FromIndexed(parameter - 30) };
                    break;
                case >= 40 and <= 47:
                    result = result with { Background = AnsiColor.FromIndexed(parameter - 40) };
                    break;
                case >= 90 and <= 97:
                    result = result with { Foreground = AnsiColor.FromIndexed(8 + parameter - 90) };
                    break;
                case >= 100 and <= 107:
                    result = result with { Background = AnsiColor.FromIndexed(8 + parameter - 100) };
                    break;
                case 38:
                case 48:
                    result = ApplyExtendedColor(result, parameters, ref i, isForeground: parameter is 38);
                    break;
            }
        }

        return result;
    }

    private static AnsiStyle ApplyExtendedColor(AnsiStyle style, List<int> parameters, ref int index, bool isForeground)
    {
        if (index + 1 >= parameters.Count)
            return style;

        var mode = parameters[index + 1];
        if (mode is 5)
        {
            if (index + 2 < parameters.Count)
            {
                var colorIndex = parameters[index + 2];
                if (colorIndex is >= 0 and <= 255)
                {
                    style = isForeground
                        ? style with { Foreground = AnsiColor.FromIndexed(colorIndex) }
                        : style with { Background = AnsiColor.FromIndexed(colorIndex) };
                }

                index += 2;
            }
            else
            {
                index++;
            }

            return style;
        }

        if (mode is 2)
        {
            if (index + 4 < parameters.Count)
            {
                var red = parameters[index + 2];
                var green = parameters[index + 3];
                var blue = parameters[index + 4];
                if (red is >= 0 and <= 255 && green is >= 0 and <= 255 && blue is >= 0 and <= 255)
                {
                    var color = AnsiColor.FromRgb((byte)red, (byte)green, (byte)blue);
                    style = isForeground
                        ? style with { Foreground = color }
                        : style with { Background = color };
                }

                index += 4;
            }
            else
            {
                index++;
            }
        }
        else
        {
            index++;
        }

        return style;
    }

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
        if (end > start)
        {
            runs.Add(new AnsiTextRun(start, end, style));
        }
    }

    public sealed record AnsiText(string Text, IReadOnlyList<AnsiTextRun> Runs);

    public sealed record AnsiTextRun(int Start, int End, AnsiStyle Style);

    public sealed record AnsiStyle(AnsiColor? Foreground, AnsiColor? Background, bool Bold, bool Italic, bool Underline, bool Inverse)
    {
        public static AnsiStyle None => new(Foreground: null, Background: null, Bold: false, Italic: false, Underline: false, Inverse: false);
    }

    public sealed record AnsiColor(AnsiColorKind Kind, byte Red, byte Green, byte Blue, byte IndexedValue)
    {
        public static AnsiColor FromIndexed(int value)
        {
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
