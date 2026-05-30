using System.Globalization;
using System.Text;

namespace Meziantou.AspNetCore.Components;

internal static class AnsiTextParser
{
    private const char EscapeCharacter = '\u001b';

    public static ParsedText Parse(string text)
    {
        var builder = new StringBuilder(text.Length);
        var runs = new List<TextRun>();
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
            runs.Add(new TextRun(0, builder.Length, AnsiStyle.None));
        }

        return new ParsedText(builder.ToString(), runs);
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

    private static void AddRun(List<TextRun> runs, AnsiStyle style, int start, int end)
    {
        if (end > start)
        {
            runs.Add(new TextRun(start, end, style));
        }
    }

    internal sealed record ParsedText(string Text, IReadOnlyList<TextRun> Runs);

    internal sealed record TextRun(int Start, int End, AnsiStyle Style);

    internal sealed record AnsiStyle(AnsiColor? Foreground, AnsiColor? Background, bool Bold, bool Italic, bool Underline, bool Inverse)
    {
        public static AnsiStyle None => new(Foreground: null, Background: null, Bold: false, Italic: false, Underline: false, Inverse: false);
    }

    internal sealed record AnsiColor(AnsiColorKind Kind, byte Red, byte Green, byte Blue, byte IndexedValue)
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

    internal enum AnsiColorKind
    {
        Indexed,
        Rgb,
    }
}
