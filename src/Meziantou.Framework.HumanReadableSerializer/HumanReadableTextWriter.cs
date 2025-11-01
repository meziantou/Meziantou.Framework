using Meziantou.Framework.HumanReadable.Utils;

namespace Meziantou.Framework.HumanReadable;

/// <summary>Writes human-readable text output with support for indentation, arrays, and objects.</summary>
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

    /// <summary>Initializes a new instance of the <see cref="HumanReadableTextWriter"/> class.</summary>
    /// <param name="options">The serialization options.</param>
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

    /// <summary>Writes a value to the output.</summary>
    /// <param name="value">The value to write.</param>
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

    /// <summary>Writes a value to the output.</summary>
    /// <param name="value">The value to write.</param>
    public void WriteValue(string value)
    {
        WriteValue(value.AsSpan());
    }

    /// <summary>Writes a formatted value to the output using the formatter for the specified media type.</summary>
    /// <param name="mediaTypeName">The media type name (e.g., "application/json").</param>
    /// <param name="value">The value to write.</param>
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

    /// <summary>Writes a null value to the output.</summary>
    public void WriteNullValue()
    {
        Write("<null>");
        WriteNewLine();
    }

    /// <summary>Writes a property name to the output.</summary>
    /// <param name="propertyName">The name of the property.</param>
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

    /// <summary>Starts writing an object.</summary>
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

    /// <summary>Ends writing an object.</summary>
    public void EndObject()
    {
        _depth--;
        _scopes.Pop().Dispose();
    }

    /// <summary>Writes an empty object to the output.</summary>
    public void WriteEmptyObject()
    {
        WriteValue("{}");
    }

    /// <summary>Starts writing an array.</summary>
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

    /// <summary>Ends writing an array.</summary>
    public void EndArray()
    {
        _scopes.Pop().Dispose();
    }

    /// <summary>Writes an empty array to the output.</summary>
    public void WriteEmptyArray()
    {
        WriteValue("[]");
    }

    /// <summary>Starts writing an array item.</summary>
    /// <param name="index">An optional index or label for the array item.</param>
    public void StartArrayItem(string? index = null)
    {
        Write("- " + index);
        Indent(); // "- " is the same length as one indentation
        _context = WriterContext.ArrayItemStart;
        _scopes.Push(new Scope(this, unindent: true));
    }

    /// <summary>Ends writing an array item.</summary>
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
