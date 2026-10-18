using System.Globalization;
using System.Text;
using Meziantou.Framework.WebPlatform.Dom;
using Meziantou.Framework.WebPlatform.Html;
using Meziantou.Framework.WebPlatform.Infra;

namespace Meziantou.Framework.WebPlatform.WebVtt;

/// <summary>
/// The WebVTT parser (https://w3c.github.io/webvtt/#file-parsing): the bytes of a WebVTT file are decoded as UTF-8 and parsed
/// into cues, regions and the style sheets of the STYLE blocks.
/// </summary>
/// <remarks>
/// The parser is incremental (https://w3c.github.io/webvtt/#incremental-webvtt-parser): <see cref="Append"/> can be called
/// with the bytes of the resource as they arrive, and <see cref="Complete"/> once the byte stream has ended. The algorithm
/// of the specification moves a position in the input and only ever moves it back to the start of the current line (when a
/// line that contains "--&gt;" ends a block), so it is implemented line by line: a line is processed once its line feed is
/// received, and a block is returned when the line that ends it is processed.
/// </remarks>
internal sealed class WebVttParser
{
    private enum State
    {
        /// <summary>The first line of the file is not received yet (the WebVTT file signature).</summary>
        Signature,

        /// <summary>The line that follows the first line: it starts the header block unless it is empty.</summary>
        Header,

        /// <summary>The blocks of the file.</summary>
        Blocks,

        /// <summary>The file does not start with the WebVTT file signature: it is not processed.</summary>
        Failed,
    }

    private readonly Action<VttCue> _onCue;
    private readonly Action<string>? _onStyleSheet;
    private readonly List<VttRegion> _regions;
    private readonly Decoder _decoder = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: false).GetDecoder();
    private readonly StringBuilder _line = new();
    private readonly StringBuilder _buffer = new();
    private State _state;
    private bool _isStart = true;
    private bool _lastWasCarriageReturn;
    private bool _seenCue;

    // The variables of "collect a WebVTT block"
    private int _lineCount;
    private bool _inHeader;
    private bool _seenArrow;
    private VttCue? _cue;
    private bool _isStyleSheet;
    private VttRegion? _region;

    /// <summary>Creates a parser.</summary>
    /// <param name="onCue">Called for each cue of the file, in order.</param>
    /// <param name="regions">The text track list of regions, to which the regions of the file are added.</param>
    /// <param name="onStyleSheet">Called with the text of each STYLE block of the file, in order.</param>
    public WebVttParser(Action<VttCue> onCue, List<VttRegion>? regions = null, Action<string>? onStyleSheet = null)
    {
        _onCue = onCue;
        _regions = regions ?? [];
        _onStyleSheet = onStyleSheet;
    }

    /// <summary>Whether the file does not start with the WebVTT file signature (it was not successfully processed).</summary>
    public bool HasFailed => _state == State.Failed;

    /// <summary>Parses a whole file.</summary>
    public static bool Parse(ReadOnlySpan<byte> bytes, Action<VttCue> onCue, List<VttRegion>? regions = null, Action<string>? onStyleSheet = null)
    {
        var parser = new WebVttParser(onCue, regions, onStyleSheet);
        parser.Append(bytes);
        parser.Complete();
        return !parser.HasFailed;
    }

    /// <summary>Parses the bytes of the file that follow the bytes parsed so far.</summary>
    public void Append(ReadOnlySpan<byte> bytes)
    {
        if (_state == State.Failed || bytes.IsEmpty)
            return;

        var chars = new char[_decoder.GetCharCount(bytes, flush: false)];
        var count = _decoder.GetChars(bytes, chars, flush: false);
        AppendChars(chars.AsSpan(0, count));
    }

    /// <summary>The byte stream has ended: the last line and the last block are processed.</summary>
    public void Complete()
    {
        if (_state == State.Failed)
            return;

        var chars = new char[_decoder.GetCharCount([], flush: true)];
        var count = _decoder.GetChars([], chars, flush: true);
        AppendChars(chars.AsSpan(0, count));
        if (_state == State.Signature)
        {
            // The file is a single line: it is the WebVTT file signature, or the file is not processed
            ProcessLine(_line.ToString());
            _line.Clear();
            return;
        }

        if (_line.Length > 0)
        {
            ProcessLine(_line.ToString());
            _line.Clear();
        }

        if (_state == State.Blocks && _lineCount > 0)
        {
            EndBlock();
        }
    }

    private void AppendChars(ReadOnlySpan<char> chars)
    {
        foreach (var c in chars)
        {
            // The UTF-8 decode algorithm removes the byte order mark
            if (_isStart)
            {
                _isStart = false;
                if (c == (char)0xFEFF)
                    continue;
            }

            // U+0000 is replaced by U+FFFD, CRLF and CR by LF
            if (c == '\n')
            {
                if (_lastWasCarriageReturn)
                {
                    _lastWasCarriageReturn = false;
                    continue;
                }

                EndLine();
            }
            else if (c == '\r')
            {
                _lastWasCarriageReturn = true;
                EndLine();
            }
            else
            {
                _lastWasCarriageReturn = false;
                _line.Append(c == '\0' ? (char)0xFFFD : c);
            }

            if (_state == State.Failed)
                return;
        }
    }

    private void EndLine()
    {
        var line = _line.ToString();
        _line.Clear();
        ProcessLine(line);
    }

    private void ProcessLine(string line)
    {
        switch (_state)
        {
            case State.Signature:
                // The file starts with "WEBVTT", followed by a space, a tab or a line feed (or the end of the file)
                if (!line.StartsWith("WEBVTT", StringComparison.Ordinal) || (line.Length > 6 && line[6] is not (' ' or '\t')))
                {
                    _state = State.Failed;
                    return;
                }

                _state = State.Header;
                break;

            case State.Header:
                // "If the character indicated by position is not a U+000A LINE FEED (LF) character, then collect a WebVTT
                // block with the in header flag set."
                _state = State.Blocks;
                if (line.Length > 0)
                {
                    _inHeader = true;
                    ProcessBlockLine(line);
                }

                break;

            case State.Blocks:
                // The line feeds between the blocks are skipped
                if (_lineCount == 0 && line.Length == 0)
                    return;

                ProcessBlockLine(line);
                break;
        }
    }

    /// <summary>The steps of the loop of "collect a WebVTT block" for a line (https://w3c.github.io/webvtt/#collect-a-webvtt-block).</summary>
    private void ProcessBlockLine(string line)
    {
        _lineCount++;
        if (line.Contains("-->", StringComparison.Ordinal))
        {
            if (!_inHeader && (_lineCount == 1 || (_lineCount == 2 && !_seenArrow)))
            {
                _seenArrow = true;

                // Cue creation: the identifier of the cue is the line that precedes its timings, if any
                var cue = new VttCue(0, 0, "");
                cue.SetParsedId(_buffer.ToString());
                if (CollectCueTimingsAndSettings(line, _regions, cue))
                {
                    _cue = cue;
                    _buffer.Clear();
                    _seenCue = true;
                }
                else
                {
                    _cue = null;
                }
            }
            else
            {
                // The line starts the next block
                EndBlock();
                ProcessBlockLine(line);
            }

            return;
        }

        if (line.Length == 0)
        {
            EndBlock();
            return;
        }

        if (!_inHeader && _lineCount == 2 && !_seenCue)
        {
            if (IsBlockKeyword(_buffer, "STYLE"))
            {
                _isStyleSheet = true;
                _buffer.Clear();
            }
            else if (IsBlockKeyword(_buffer, "REGION"))
            {
                _region = new VttRegion();
                _buffer.Clear();
            }
        }

        if (_buffer.Length > 0)
        {
            _buffer.Append('\n');
        }

        _buffer.Append(line);
    }

    /// <summary>The buffer starts with the keyword, and its remaining characters are all ASCII whitespace.</summary>
    private static bool IsBlockKeyword(StringBuilder buffer, string keyword)
    {
        if (buffer.Length < keyword.Length)
            return false;

        for (var i = 0; i < buffer.Length; i++)
        {
            if (i < keyword.Length ? buffer[i] != keyword[i] : !InfraAscii.IsAsciiWhitespace(buffer[i]))
                return false;
        }

        return true;
    }

    /// <summary>The block ends: its cue, its style sheet or its region is returned.</summary>
    private void EndBlock()
    {
        var buffer = _buffer.ToString();
        var cue = _cue;
        var isStyleSheet = _isStyleSheet;
        var region = _region;
        _buffer.Clear();
        _lineCount = 0;
        _inHeader = false;
        _seenArrow = false;
        _cue = null;
        _isStyleSheet = false;
        _region = null;
        if (cue is not null)
        {
            cue.SetParsedText(buffer);
            _onCue(cue);
        }
        else if (isStyleSheet)
        {
            _onStyleSheet?.Invoke(buffer);
        }
        else if (region is not null)
        {
            CollectRegionSettings(buffer, region);
            _regions.Add(region);
        }
    }

    // ----------------------------------------------------------------------------------------------------------------
    // Region settings (https://w3c.github.io/webvtt/#region-settings-parsing)
    // ----------------------------------------------------------------------------------------------------------------

    private static void CollectRegionSettings(string input, VttRegion region)
    {
        foreach (var setting in InfraString.SplitOnAsciiWhitespace(input))
        {
            var colon = setting.IndexOf(':', StringComparison.Ordinal);
            if (colon <= 0 || colon == setting.Length - 1)
                continue;

            var name = setting.AsSpan(0, colon);
            var value = setting[(colon + 1)..];
            switch (name)
            {
                case "id":
                    region.Id = value;
                    break;

                case "width":
                    if (ParsePercentage(value) is { } width)
                    {
                        region.SetParsedWidth(width);
                    }

                    break;

                case "lines":
                    if (value.AsSpan().ContainsAnyExceptInRange('0', '9'))
                        break;

                    // The number of lines is an unsigned long in the API
                    region.Lines = double.Parse(value, NumberStyles.None, CultureInfo.InvariantCulture) is var lines && lines <= uint.MaxValue ? (uint)lines : uint.MaxValue;
                    break;

                case "regionanchor":
                    if (ParseAnchor(value) is var (regionAnchorX, regionAnchorY))
                    {
                        region.SetParsedRegionAnchor(regionAnchorX, regionAnchorY);
                    }

                    break;

                case "viewportanchor":
                    if (ParseAnchor(value) is var (viewportAnchorX, viewportAnchorY))
                    {
                        region.SetParsedViewportAnchor(viewportAnchorX, viewportAnchorY);
                    }

                    break;

                case "scroll":
                    if (value == "up")
                    {
                        region.Scroll = ScrollSetting.Up;
                    }

                    break;
            }
        }
    }

    private static (double X, double Y)? ParseAnchor(string value)
    {
        var comma = value.IndexOf(',', StringComparison.Ordinal);
        if (comma < 0)
            return null;

        return ParsePercentage(value.AsSpan(0, comma)) is { } x && ParsePercentage(value.AsSpan(comma + 1)) is { } y ? (x, y) : null;
    }

    /// <summary>
    /// https://w3c.github.io/webvtt/#parse-a-percentage-string: a WebVTT percentage (one or more ASCII digits, optionally
    /// followed by a dot and one or more ASCII digits, then "%") in the range 0..100, or null.
    /// </summary>
    internal static double? ParsePercentage(ReadOnlySpan<char> input)
    {
        if (input.Length < 2 || input[^1] != '%')
            return null;

        var number = input[..^1];
        var dot = number.IndexOf('.');
        var integer = dot < 0 ? number : number[..dot];
        if (integer.IsEmpty || integer.ContainsAnyExceptInRange('0', '9'))
            return null;

        if (dot >= 0 && (number[(dot + 1)..].IsEmpty || number[(dot + 1)..].ContainsAnyExceptInRange('0', '9')))
            return null;

        if (!Microsyntaxes.TryParseFloat(number, out var percentage) || percentage < 0 || percentage > 100)
            return null;

        return percentage;
    }

    // ----------------------------------------------------------------------------------------------------------------
    // Cue timings and settings (https://w3c.github.io/webvtt/#cue-timings-and-settings-parsing)
    // ----------------------------------------------------------------------------------------------------------------

    /// <summary>https://w3c.github.io/webvtt/#collect-webvtt-cue-timings-and-settings</summary>
    private static bool CollectCueTimingsAndSettings(string input, List<VttRegion> regions, VttCue cue)
    {
        var position = 0;
        SkipWhitespace(input, ref position);
        if (CollectTimestamp(input, ref position) is not { } startTime)
            return false;

        SkipWhitespace(input, ref position);
        if (!input.AsSpan(position).StartsWith("-->"))
            return false;

        position += 3;
        SkipWhitespace(input, ref position);
        if (CollectTimestamp(input, ref position) is not { } endTime)
            return false;

        cue.SetParsedTimes(startTime, endTime);
        ParseCueSettings(input[position..], regions, cue);
        return true;
    }

    private static void SkipWhitespace(string input, ref int position)
    {
        while (position < input.Length && InfraAscii.IsAsciiWhitespace(input[position]))
        {
            position++;
        }
    }

    /// <summary>https://w3c.github.io/webvtt/#parse-the-webvtt-cue-settings</summary>
    internal static void ParseCueSettings(string input, List<VttRegion> regions, VttCue cue)
    {
        foreach (var setting in InfraString.SplitOnAsciiWhitespace(input))
        {
            var colon = setting.IndexOf(':', StringComparison.Ordinal);
            if (colon <= 0 || colon == setting.Length - 1)
                continue;

            var name = setting.AsSpan(0, colon);
            var value = setting[(colon + 1)..];
            switch (name)
            {
                case "region":
                    // The last region of the file whose identifier is the value
                    cue.SetParsedRegion(regions.FindLast(region => region.Id == value));
                    break;

                case "vertical":
                    if (value == "rl")
                    {
                        cue.SetParsedVertical(DirectionSetting.Rl);
                    }
                    else if (value == "lr")
                    {
                        cue.SetParsedVertical(DirectionSetting.Lr);
                    }

                    break;

                case "line":
                    ParseLineSetting(value, cue);
                    break;

                case "position":
                    ParsePositionSetting(value, cue);
                    break;

                case "size":
                    if (ParsePercentage(value) is { } size)
                    {
                        cue.SetParsedSize(size);
                    }

                    break;

                case "align":
                    switch (value)
                    {
                        case "start":
                            cue.SetParsedAlign(AlignSetting.Start);
                            break;
                        case "center":
                            cue.SetParsedAlign(AlignSetting.Center);
                            break;
                        case "end":
                            cue.SetParsedAlign(AlignSetting.End);
                            break;
                        case "left":
                            cue.SetParsedAlign(AlignSetting.Left);
                            break;
                        case "right":
                            cue.SetParsedAlign(AlignSetting.Right);
                            break;
                    }

                    break;
            }
        }
    }

    private static void ParseLineSetting(string value, VttCue cue)
    {
        var comma = value.IndexOf(',', StringComparison.Ordinal);
        var linePosition = comma < 0 ? value.AsSpan() : value.AsSpan(0, comma);
        var lineAlign = comma < 0 ? null : value[(comma + 1)..];
        if (!linePosition.ContainsAnyInRange('0', '9'))
            return;

        double number;
        var isPercentage = linePosition[^1] == '%';
        if (isPercentage)
        {
            if (ParsePercentage(linePosition) is not { } percentage)
                return;

            number = percentage;
        }
        else
        {
            // Only U+002D HYPHEN-MINUS characters (the first character only), ASCII digits and one U+002E DOT character
            // (between two ASCII digits)
            for (var i = 0; i < linePosition.Length; i++)
            {
                var c = linePosition[i];
                if (c == '-' ? i != 0 : c != '.' && !char.IsAsciiDigit(c))
                    return;
            }

            var dot = linePosition.IndexOf('.');
            if (dot >= 0)
            {
                if (linePosition[(dot + 1)..].Contains('.') || dot == 0 || dot == linePosition.Length - 1 || !char.IsAsciiDigit(linePosition[dot - 1]) || !char.IsAsciiDigit(linePosition[dot + 1]))
                    return;
            }

            if (!Microsyntaxes.TryParseFloat(linePosition, out number))
                return;
        }

        LineAlignSetting? alignment = lineAlign switch
        {
            "start" => LineAlignSetting.Start,
            "center" => LineAlignSetting.Center,
            "end" => LineAlignSetting.End,
            _ => null,
        };

        if (alignment is null && lineAlign is not null)
            return;

        cue.SetParsedLine(number, snapToLines: !isPercentage, alignment);
    }

    private static void ParsePositionSetting(string value, VttCue cue)
    {
        var comma = value.IndexOf(',', StringComparison.Ordinal);
        var columnPosition = comma < 0 ? value.AsSpan() : value.AsSpan(0, comma);
        var columnAlign = comma < 0 ? null : value[(comma + 1)..];
        if (ParsePercentage(columnPosition) is not { } number)
            return;

        PositionAlignSetting? alignment = columnAlign switch
        {
            "line-left" => PositionAlignSetting.LineLeft,
            "center" => PositionAlignSetting.Center,
            "line-right" => PositionAlignSetting.LineRight,
            _ => null,
        };

        if (alignment is null && columnAlign is not null)
            return;

        cue.SetParsedPosition(number, alignment);
    }

    /// <summary>https://w3c.github.io/webvtt/#collect-a-webvtt-timestamp: the time in seconds, or null.</summary>
    internal static double? CollectTimestamp(ReadOnlySpan<char> input, ref int position)
    {
        var mostSignificantUnitsAreHours = false;
        if (position >= input.Length || !char.IsAsciiDigit(input[position]))
            return null;

        var digits = CollectDigits(input, ref position);
        var value1 = ParseInteger(digits);
        if (digits.Length != 2 || value1 > 59)
        {
            mostSignificantUnitsAreHours = true;
        }

        if (position >= input.Length || input[position] != ':')
            return null;

        position++;
        digits = CollectDigits(input, ref position);
        if (digits.Length != 2)
            return null;

        var value2 = ParseInteger(digits);
        double value3;
        if (mostSignificantUnitsAreHours || (position < input.Length && input[position] == ':'))
        {
            if (position >= input.Length || input[position] != ':')
                return null;

            position++;
            digits = CollectDigits(input, ref position);
            if (digits.Length != 2)
                return null;

            value3 = ParseInteger(digits);
        }
        else
        {
            value3 = value2;
            value2 = value1;
            value1 = 0;
        }

        if (position >= input.Length || input[position] != '.')
            return null;

        position++;
        digits = CollectDigits(input, ref position);
        if (digits.Length != 3)
            return null;

        var value4 = ParseInteger(digits);
        if (value2 > 59 || value3 > 59)
            return null;

        return (value1 * 60 * 60) + (value2 * 60) + value3 + (value4 / 1000);
    }

    private static ReadOnlySpan<char> CollectDigits(ReadOnlySpan<char> input, ref int position)
    {
        var start = position;
        while (position < input.Length && char.IsAsciiDigit(input[position]))
        {
            position++;
        }

        return input[start..position];
    }

    private static double ParseInteger(ReadOnlySpan<char> digits) => digits.IsEmpty ? 0 : double.Parse(digits, NumberStyles.None, CultureInfo.InvariantCulture);
}
