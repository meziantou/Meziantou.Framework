using System.Text;
using Meziantou.Framework.HumanReadable.Utils;

namespace Meziantou.Framework.HumanReadable;

public sealed class HumanReadableTextWriter
{
    private const string Indentation = "  ";
    private static readonly string NewLine = Environment.NewLine;

    private readonly StringBuilder _text = new();
    private readonly HumanReadableSerializerOptions _options;
    private readonly Stack<Scope> _scopes = new();

    private int _indentation;
    private int _depth;

    private WriterContext _context;

    public HumanReadableTextWriter(HumanReadableSerializerOptions options)
    {
        _options = options;
    }

    private void WritePendingText(bool indent = true)
    {
        if (_context is WriterContext.NewLine)
        {
            _text.Append(NewLine);
        }
        else if (_context is WriterContext.PropertyName)
        {
            _text.Append(' ');
        }

        if (indent && _context is WriterContext.NewLine)
        {
            for (var i = 0; i < _indentation; i++)
            {
                _text.Append(Indentation);
            }
        }

        _context = WriterContext.None;
    }

    private void Write(string value, bool isValue = false)
    {
        Write(value.AsSpan(), isValue);
    }

    private void Write(ReadOnlySpan<char> value, bool isValue = false)
    {
        var first = true;
        if (isValue && _options.ShowInvisibleCharactersInValues)
        {
            foreach (var (line, eol) in StringUtils.EnumerateLines(value))
            {
                WritePendingText(indent: !line.IsEmpty);
                ReplaceInvisibleCharacters(_text, line);
                ReplaceInvisibleCharacters(_text, eol);
            }
        }
        else
        {
            if (!value.IsEmpty)
            {
                foreach (var (line, eol) in StringUtils.EnumerateLines(value))
                {
                    if (!first)
                    {
                        WriteNewLine();
                    }

                    WritePendingText(indent: !line.IsEmpty);
                    if (isValue && _options.ShowInvisibleCharactersInValues)
                    {
                        ReplaceInvisibleCharacters(_text, line);
                        ReplaceInvisibleCharacters(_text, eol);
                    }
                    else
                    {
                        _text.Append(line);
                    }

                    first = false;
                }
            }
        }
    }

    private void WriteNewLine()
    {
        _context = WriterContext.NewLine;
    }

    private static void ReplaceInvisibleCharacters(StringBuilder sb, ReadOnlySpan<char> value)
    {
        for (var i = 0; i < value.Length; i++)
        {
            var c = value[i];

            if (c is '\r')
            {
                if (i + 1 < value.Length && value[i + 1] is '\n')
                {
                    sb.Append("\u240D\u240A\r\n");
                    i++;
                }
                else
                {
                    sb.Append("\u240D\r");
                }
            }
            else if (c is '\n')
            {
                sb.Append("\u240A\n");
            }
            else if (c is >= '\u0000' and <= '\u0020') // Control characters: https://www.compart.com/en/unicode/block/U+2400
            {
                sb.Append((char)((short)'\u2400' + (short)c));
            }
            else if (c is '\u007F')
            {
                sb.Append('\u2421');
            }
            else
            {
                sb.Append(c);
            }
        }
    }

    public void WriteValue(ReadOnlySpan<char> value)
    {
        var multipleLines = StringUtils.IsMultiLines(value);
        if (multipleLines)
        {
            var indent = _text.Length > 0 && _context is not WriterContext.ArrayItemStart;
            if (indent)
            {
                Indent();
                WriteNewLine();
            }

            Write(value, isValue: true);

            if (indent)
            {
                Unindent();
            }

            WriteNewLine();
        }
        else
        {
            Write(value);
            WriteNewLine();
        }
    }

    public void WriteValue(string value)
    {
        WriteValue(value.AsSpan());
    }

    public void WriteFormattedValue(string mediaTypeName, string value)
    {
        var formatter = _options.GetFormatter(mediaTypeName);
        if (formatter is null)
        {
            WriteValue(value);
            return;
        }

        formatter.Format(this, value, _options);
    }

    public void WriteNullValue()
    {
        Write("<null>");
        WriteNewLine();
    }

    public void WritePropertyName(string propertyName)
    {
        Write(propertyName);
        Write(":");
        _context = WriterContext.PropertyName;
    }

    private void IncrementDepth()
    {
        _depth++;
        if (_depth > _options.MaxDepth)
            throw new HumanReadableSerializerException($"Current depth ({_depth}) is equal to or larger than the maximum allowed depth of {_options.MaxDepth}. Cannot write the next object or array");
    }

    public void StartObject()
    {
        IncrementDepth();
        if (_text.Length > 0 && _context != WriterContext.ArrayItemStart)
        {
            WriteNewLine();
            Indent();
            _scopes.Push(new Scope(this, unindent: true));
        }
        else
        {
            _scopes.Push(new Scope(this, unindent: false));
        }
    }

    public void EndObject()
    {
        _depth--;
        _scopes.Pop().Dispose();
    }

    public void WriteEmptyObject()
    {
        WriteValue("{}");
    }

    public void StartArray()
    {
        if (_text.Length > 0 && _context != WriterContext.ArrayItemStart)
        {
            WriteNewLine();
            Indent();
            _scopes.Push(new Scope(this, unindent: true));
        }
        else
        {
            _scopes.Push(new Scope(this, unindent: false));
        }
    }

    public void EndArray()
    {
        _scopes.Pop().Dispose();
    }

    public void WriteEmptyArray()
    {
        WriteValue("[]");
    }

    public void StartArrayItem(string? index = null)
    {
        Write("- " + index);
        Indent(); // "- " is the same length as one indentation
        _context = WriterContext.ArrayItemStart;
        _scopes.Push(new Scope(this, unindent: true));
    }

    public void EndArrayItem()
    {
        _scopes.Pop().Dispose();
    }

    private void Indent() => _indentation++;

    private void Unindent() => _indentation--;

    public override string ToString() => _text.ToString();

    private readonly struct Scope
    {
        private readonly HumanReadableTextWriter _writer;
        private readonly bool _unindent;

        internal Scope(HumanReadableTextWriter writer, bool unindent)
        {
            _writer = writer;
            _unindent = unindent;
        }

        public void Dispose()
        {
            if (_unindent)
            {
                _writer.Unindent();
            }

            _writer._context = WriterContext.NewLine;
        }
    }

    private enum WriterContext
    {
        None,
        PropertyName,
        ArrayItemStart,
        ObjectStart,
        NewLine,
    }
}
