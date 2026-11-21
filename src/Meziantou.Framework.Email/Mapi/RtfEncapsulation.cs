using System.Globalization;
using System.Text;
using Meziantou.Framework.Email.Internals;

namespace Meziantou.Framework.Email.Mapi;

/// <summary>
/// Extracts the HTML and plain text bodies that Outlook encapsulates in RTF bodies, and encapsulates bodies in RTF
/// ([MS-OXRTFEX]). Outlook often stores the HTML body of a message only in its compressed RTF body.
/// </summary>
/// <example>
/// <code xmlns:ref="urn:meziantou:file-referencer" ref:source="../../../tests/Meziantou.Framework.Email.Tests/RtfEncapsulationTests.cs" ref:region="RtfEncapsulation_Usage" ref:dedent="true">
/// var rtf = RtfEncapsulation.EncapsulateHtml("&lt;p&gt;Hello {world}&lt;/p&gt;\r\n");
/// Assert.Equal(RtfEncapsulationFormat.Html, RtfEncapsulation.GetFormat(rtf));
/// Assert.Equal("&lt;p&gt;Hello {world}&lt;/p&gt;\r\n", RtfEncapsulation.Decapsulate(rtf));
///
/// // An RTF body that does not encapsulate another format
/// Assert.Equal(RtfEncapsulationFormat.None, RtfEncapsulation.GetFormat(@"{\rtf1\ansi Hello}"u8));
/// Assert.Null(RtfEncapsulation.Decapsulate(@"{\rtf1\ansi Hello}"u8));
/// </code>
/// </example>
public static class RtfEncapsulation
{
    // The number of tokens of the header where the reader looks for \fromhtml1 and \fromtext ([MS-OXRTFEX] 2.2.3.1)
    private const int MaxHeaderTokens = 10;
    private const int DefaultCodePage = 1252;

    // The destinations that do not produce visible text, skipped like the ignorable destinations ("\*")
    private static readonly HashSet<string> SkippedDestinations = new(StringComparer.Ordinal)
    {
        "author", "bkmkend", "bkmkstart", "buptim", "colorschememapping", "colortbl", "comment", "company", "creatim", "datastore", "doccomm",
        "fldinst", "filetbl", "footer", "footerf", "footerl", "footerr", "generator", "header", "headerf", "headerl", "headerr", "info", "keywords",
        "latentstyles", "listoverridetable", "listtable", "nonshppict", "object", "objdata", "operator", "pict", "pn", "pntext", "pntxta", "pntxtb",
        "printim", "revtbl", "revtim", "rsidtbl", "stylesheet", "subject", "tc", "template", "themedata", "title", "userprops", "xe", "xmlnstbl",
    };

    /// <summary>Returns the format an RTF body encapsulates, from the control words of its header.</summary>
    /// <param name="rtf">The RTF, decompressed (see <see cref="MapiMessage.GetRtfBody"/>).</param>
    /// <returns>The format of the encapsulated body, or <see cref="RtfEncapsulationFormat.None"/> for an RTF body.</returns>
    public static RtfEncapsulationFormat GetFormat(ReadOnlySpan<byte> rtf)
    {
        if (!rtf.StartsWith("{\\rtf1"u8))
        {
            return RtfEncapsulationFormat.None;
        }

        var position = 0;
        for (var tokens = 0; tokens < MaxHeaderTokens && position < rtf.Length;)
        {
            var c = rtf[position];
            if (c is (byte)'\r' or (byte)'\n')
            {
                position++;
                continue;
            }

            tokens++;
            if (c == '{')
            {
                position++;
                continue;
            }

            // Any token other than a group start or a control word ends the header
            if (c != '\\' || position + 1 >= rtf.Length || !IsLetter(rtf[position + 1]))
            {
                return RtfEncapsulationFormat.None;
            }

            var (word, parameter, hasParameter) = ReadControlWord(rtf, ref position);
            if (word == "fromhtml")
            {
                // Only \fromhtml1 is an encapsulation ([MS-OXRTFEX] 2.1.3.1.2)
                return hasParameter && parameter == 1 ? RtfEncapsulationFormat.Html : RtfEncapsulationFormat.None;
            }

            if (word == "fromtext")
            {
                return RtfEncapsulationFormat.Text;
            }
        }

        return RtfEncapsulationFormat.None;
    }

    /// <summary>Extracts the HTML or the plain text an RTF body encapsulates.</summary>
    /// <param name="rtf">The RTF, decompressed (see <see cref="MapiMessage.GetRtfBody"/>).</param>
    /// <returns>The HTML (<see cref="RtfEncapsulationFormat.Html"/>) or the plain text (<see cref="RtfEncapsulationFormat.Text"/>), or <see langword="null"/> when the RTF does not encapsulate another format.</returns>
    public static string? Decapsulate(ReadOnlySpan<byte> rtf)
    {
        var format = GetFormat(rtf);
        if (format is RtfEncapsulationFormat.None)
        {
            return null;
        }

        return new Decoder(format == RtfEncapsulationFormat.Html).Decode(rtf);
    }

    /// <summary>Encapsulates an HTML body in RTF, which <see cref="Decapsulate"/> extracts unchanged.</summary>
    /// <param name="html">The HTML.</param>
    /// <returns>The RTF. It contains only the encapsulated HTML: a reader that does not support the encapsulation shows an empty document.</returns>
    public static byte[] EncapsulateHtml(string html)
    {
        ArgumentNullException.ThrowIfNull(html);
        var builder = new StringBuilder(html.Length + 128);
        builder.Append(@"{\rtf1\ansi\ansicpg1252\fromhtml1 \deff0{\fonttbl{\f0\fswiss\fcharset0 Arial;}}{\*\htmltag64}");
        if (html.Length > 0)
        {
            builder.Append(@"{\*\htmltag0 ");
            Escape(builder, html);
            builder.Append('}');
        }

        builder.Append('}');
        return Encoding.ASCII.GetBytes(builder.ToString());
    }

    /// <summary>Encapsulates a plain text body in RTF, which <see cref="Decapsulate"/> extracts unchanged.</summary>
    /// <param name="text">The plain text.</param>
    /// <returns>The RTF, which any RTF reader shows as the text.</returns>
    public static byte[] EncapsulateText(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var builder = new StringBuilder(text.Length + 128);
        builder.Append(@"{\rtf1\ansi\ansicpg1252\fromtext \deff0{\fonttbl{\f0\fmodern\fcharset0 Courier New;}}\uc1\pard\plain\f0\fs20 ");
        Escape(builder, text);
        builder.Append('}');
        return Encoding.ASCII.GetBytes(builder.ToString());
    }

    private static void Escape(StringBuilder builder, string value)
    {
        for (var i = 0; i < value.Length; i++)
        {
            var c = value[i];
            switch (c)
            {
                case '\r' when i + 1 < value.Length && value[i + 1] == '\n':
                    builder.Append("\\par\r\n");
                    i++;
                    break;
                case '\t':
                    builder.Append("\\tab ");
                    break;
                case '\\' or '{' or '}':
                    builder.Append('\\').Append(c);
                    break;
                case >= ' ' and < (char)0x7F:
                    builder.Append(c);
                    break;
                default:
                    // \uN takes a signed 16-bit value, followed by one replacement character (\uc1)
                    builder.Append("\\u").Append(((short)c).ToString(CultureInfo.InvariantCulture)).Append('?');
                    break;
            }
        }
    }

    private static bool IsLetter(byte c) => c is >= (byte)'a' and <= (byte)'z' or >= (byte)'A' and <= (byte)'Z';

    private static bool IsDigit(byte c) => c is >= (byte)'0' and <= (byte)'9';

    /// <summary>Reads a control word at <paramref name="position"/>, its backslash included, and the space that ends it.</summary>
    private static (string Word, int Parameter, bool HasParameter) ReadControlWord(ReadOnlySpan<byte> rtf, ref int position)
    {
        var start = ++position;

        // [MSFT-RTF]: a control word has at most 32 letters, and its parameter at most 10 digits
        while (position < rtf.Length && IsLetter(rtf[position]) && position - start < 32)
        {
            position++;
        }

        var word = Encoding.ASCII.GetString(rtf[start..position]);
        var negative = false;
        if (position + 1 < rtf.Length && rtf[position] == '-' && IsDigit(rtf[position + 1]))
        {
            negative = true;
            position++;
        }

        var hasParameter = false;
        long parameter = 0;
        var digits = 0;
        while (position < rtf.Length && IsDigit(rtf[position]) && digits < 10)
        {
            parameter = (parameter * 10) + (rtf[position] - '0');
            hasParameter = true;
            digits++;
            position++;
        }

        if (position < rtf.Length && rtf[position] == ' ')
        {
            position++;
        }

        parameter = Math.Clamp(negative ? -parameter : parameter, int.MinValue, int.MaxValue);
        return (word, (int)parameter, hasParameter);
    }

    private static int GetCodePage(int charset) => charset switch
    {
        77 => 10000,
        128 => 932,
        129 => 949,
        130 => 1361,
        134 => 936,
        136 => 950,
        161 => 1253,
        162 => 1254,
        163 => 1258,
        177 => 1255,
        178 => 1256,
        186 => 1257,
        204 => 1251,
        222 => 874,
        238 => 1250,
        254 => 437,
        _ => 0,
    };

    /// <summary>The de-encapsulating reader of [MS-OXRTFEX] 2.2.3.2 (HTML) and 2.2.3.3 (plain text).</summary>
    private sealed class Decoder(bool html)
    {
        private readonly StringBuilder _output = new();
        private readonly List<byte> _pendingBytes = [];
        private readonly Dictionary<int, int> _fontCodePages = [];
        private readonly Stack<GroupState> _groups = new();
        private int _pendingCodePage;
        private int _defaultCodePage = DefaultCodePage;
        private int _fontTableFont = -1;
        private int _skipCount;
        private GroupState _state = new() { Uc = 1, CodePage = -1 };

        private bool CanOutput => !_state.Skip && (_state.HtmlTag || !_state.Suppressed);

        public string Decode(ReadOnlySpan<byte> rtf)
        {
            var position = 0;
            var groupStart = false;
            while (position < rtf.Length)
            {
                var c = rtf[position];
                switch (c)
                {
                    case (byte)'{':
                        _groups.Push(_state);
                        _skipCount = 0;
                        groupStart = true;
                        position++;
                        continue;

                    case (byte)'}':
                        if (_groups.Count > 0)
                        {
                            _state = _groups.Pop();
                        }

                        _skipCount = 0;
                        groupStart = false;
                        position++;
                        continue;

                    case (byte)'\\':
                        if (position + 1 < rtf.Length && IsLetter(rtf[position + 1]))
                        {
                            var (word, parameter, hasParameter) = ReadControlWord(rtf, ref position);
                            OnControlWord(rtf, ref position, word, parameter, hasParameter, groupStart);
                        }
                        else
                        {
                            OnControlSymbol(rtf, ref position, groupStart);
                        }

                        groupStart = false;
                        continue;

                    case (byte)'\r' or (byte)'\n' or 0:
                        position++;
                        continue;

                    default:
                        groupStart = false;
                        position++;
                        if (ConsumeSkippedCharacter())
                        {
                            continue;
                        }

                        if (CanOutput)
                        {
                            AppendByte(c);
                        }

                        continue;
                }
            }

            FlushBytes();
            return _output.ToString();
        }

        private void OnControlSymbol(ReadOnlySpan<byte> rtf, ref int position, bool groupStart)
        {
            if (position + 1 >= rtf.Length)
            {
                position++;
                return;
            }

            var symbol = rtf[position + 1];
            position += 2;
            switch (symbol)
            {
                case (byte)'*':
                    // An ignorable destination: only \*\htmltag is copied ([MS-OXRTFEX] 2.2.3.2)
                    if (groupStart)
                    {
                        if (html && position + 8 <= rtf.Length && rtf[position..].StartsWith("\\htmltag"u8) && (position + 8 == rtf.Length || !IsLetter(rtf[position + 8])))
                        {
                            ReadControlWord(rtf, ref position);
                            _state.HtmlTag = true;
                        }
                        else
                        {
                            _state.Skip = true;
                        }
                    }

                    return;

                case (byte)'\'':
                    var value = position + 2 <= rtf.Length ? ParseHex(rtf[position], rtf[position + 1]) : -1;
                    if (value < 0)
                    {
                        return;
                    }

                    position += 2;
                    if (!ConsumeSkippedCharacter() && CanOutput)
                    {
                        AppendByte((byte)value);
                    }

                    return;

                case (byte)'\\' or (byte)'{' or (byte)'}':
                    if (!ConsumeSkippedCharacter() && CanOutput)
                    {
                        AppendChar((char)symbol);
                    }

                    return;

                case (byte)'~':
                    AppendCharIfVisible('\u00A0');
                    return;

                case (byte)'_':
                    AppendCharIfVisible('\u00AD');
                    return;

                case (byte)'\r' or (byte)'\n':
                    // A backslash before a line break is a \par ([MSFT-RTF])
                    AppendTextIfVisible("\r\n");
                    return;

                default:
                    return;
            }
        }

        private void OnControlWord(ReadOnlySpan<byte> rtf, ref int position, string word, int parameter, bool hasParameter, bool groupStart)
        {
            if (word == "bin")
            {
                // The binary data is skipped
                position += (int)Math.Clamp(parameter, 0, rtf.Length - position);
                return;
            }

            if (groupStart && word == "fonttbl")
            {
                _state.Skip = true;
                _state.FontTable = true;
                return;
            }

            if (groupStart && SkippedDestinations.Contains(word))
            {
                _state.Skip = true;
                return;
            }

            if (_state.FontTable)
            {
                switch (word)
                {
                    case "f":
                        _fontTableFont = parameter;
                        _fontCodePages[parameter] = 0;
                        break;
                    case "fcharset" when _fontTableFont >= 0 && GetCodePage(parameter) is var codePage and > 0:
                        _fontCodePages[_fontTableFont] = codePage;
                        break;
                    case "cpg" when _fontTableFont >= 0 && parameter > 0:
                        _fontCodePages[_fontTableFont] = parameter;
                        break;
                }

                return;
            }

            switch (word)
            {
                case "ansicpg" when parameter > 0:
                    _defaultCodePage = parameter;
                    return;

                case "f":
                    // The current font is tracked even in a suppressed fragment ([MS-OXRTFEX] 2.2.3.2)
                    _state.CodePage = _fontCodePages.TryGetValue(parameter, out var fontCodePage) ? fontCodePage : 0;
                    return;

                case "htmlrtf":
                    if (html)
                    {
                        _state.Suppressed = !hasParameter || parameter != 0;
                    }

                    return;

                case "uc":
                    _state.Uc = Math.Clamp(parameter, 0, 255);
                    return;

                case "u":
                    _skipCount = 0;
                    if (CanOutput)
                    {
                        AppendChar((char)(parameter < 0 ? parameter + 65536 : parameter));
                    }

                    _skipCount = _state.Uc;
                    return;
            }

            _skipCount = 0;
            var text = word switch
            {
                "par" or "line" => "\r\n",
                "tab" => "\t",
                "lquote" => "\u2018",
                "rquote" => "\u2019",
                "ldblquote" => "\u201C",
                "rdblquote" => "\u201D",
                "bullet" => "\u2022",
                "endash" => "\u2013",
                "emdash" => "\u2014",
                "emspace" => "\u2003",
                "enspace" => "\u2002",
                "qmspace" => "\u2005",
                _ => null,
            };

            if (text is not null)
            {
                AppendTextIfVisible(text);
            }
        }

        /// <summary>Returns whether the character is one of the replacement characters that follow a <c>\u</c> control word.</summary>
        private bool ConsumeSkippedCharacter()
        {
            if (_skipCount > 0)
            {
                _skipCount--;
                return true;
            }

            return false;
        }

        private void AppendCharIfVisible(char value)
        {
            if (!ConsumeSkippedCharacter() && CanOutput)
            {
                AppendChar(value);
            }
        }

        private void AppendTextIfVisible(string value)
        {
            _skipCount = 0;
            if (CanOutput)
            {
                FlushBytes();
                _output.Append(value);
            }
        }

        private void AppendChar(char value)
        {
            FlushBytes();
            _output.Append(value);
        }

        private void AppendByte(byte value)
        {
            // The text of an \htmltag group is in the default code page, the other text in the code page of the current font
            var codePage = _state.HtmlTag || _state.CodePage <= 0 ? _defaultCodePage : _state.CodePage;
            if (_pendingBytes.Count > 0 && codePage != _pendingCodePage)
            {
                FlushBytes();
            }

            _pendingCodePage = codePage;
            _pendingBytes.Add(value);
        }

        private void FlushBytes()
        {
            if (_pendingBytes.Count == 0)
            {
                return;
            }

            _output.Append(PstContext.GetEncoding(_pendingCodePage).GetString([.. _pendingBytes]));
            _pendingBytes.Clear();
        }

        private static int ParseHex(byte high, byte low)
        {
            var h = HexValue(high);
            var l = HexValue(low);
            return h < 0 || l < 0 ? -1 : (h << 4) | l;
        }

        private static int HexValue(byte c) => c switch
        {
            >= (byte)'0' and <= (byte)'9' => c - '0',
            >= (byte)'a' and <= (byte)'f' => c - 'a' + 10,
            >= (byte)'A' and <= (byte)'F' => c - 'A' + 10,
            _ => -1,
        };
    }

    private struct GroupState
    {
        public bool Skip;
        public bool HtmlTag;
        public bool FontTable;
        public bool Suppressed;
        public int CodePage;
        public int Uc;
    }
}
