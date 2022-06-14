#nullable disable
using System.ComponentModel;
using System.Text;

namespace Meziantou.Framework.Html;

public sealed class HtmlReaderParseEventArgs : CancelEventArgs
{
    public HtmlReaderParseEventArgs(StringBuilder value, StringBuilder rawValue)
    {
        Value = value ?? throw new ArgumentNullException(nameof(value));
        RawValue = rawValue ?? throw new ArgumentNullException(nameof(rawValue));
    }

    public bool Continue { get; set; }
    public bool Eof { get; set; }
    public string CurrentElement { get; set; }
    public int EatNextCharacters { get; set; }
    public char PreviousCharacter { get; set; }
    public char CurrentCharacter { get; set; }
    public char PeekCharacter { get; set; }
    public HtmlParserState State { get; set; }
    public StringBuilder Value { get; }
    public StringBuilder RawValue { get; }
}
