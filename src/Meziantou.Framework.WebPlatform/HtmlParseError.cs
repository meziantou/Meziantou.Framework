namespace Meziantou.Framework.WebPlatform;

/// <summary>A parse error reported by the HTML parser (https://html.spec.whatwg.org/multipage/parsing.html#parse-errors).</summary>
public sealed class HtmlParseError
{
    internal HtmlParseError(string code, int offset, int line, int column)
    {
        Code = code;
        Offset = offset;
        Line = line;
        Column = column;
    }

    /// <summary>The error code, as defined by the specification (e.g. <c>unexpected-null-character</c>).</summary>
    public string Code { get; }

    /// <summary>The offset of the character in the input, in UTF-16 code units.</summary>
    public int Offset { get; }

    /// <summary>The one-based line number.</summary>
    public int Line { get; }

    /// <summary>The one-based column number.</summary>
    public int Column { get; }

    /// <inheritdoc/>
    public override string ToString() => $"({Line},{Column}): {Code}";
}
