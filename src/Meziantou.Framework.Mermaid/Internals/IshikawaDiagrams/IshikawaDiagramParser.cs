using System.Text;

namespace Meziantou.Framework.Mermaid.Internals.IshikawaDiagrams;

/// <summary>
/// Parses the <c>ishikawa-beta</c> (or <c>ishikawa</c>) diagrams of Mermaid: the first line is the effect, and the
/// indentation of the next lines makes the tree of the causes, as Mermaid reads them: the indentation of the first
/// cause is the level of the main causes, and a line is a cause of the last line less indented than it.
/// </summary>
internal sealed class IshikawaDiagramParser
{
    private readonly ParseContext _context;
    private readonly IshikawaDiagram _diagram = new();
    private readonly List<(int Level, IshikawaCause? Cause)> _stack = [];
    private bool _hasEffect;
    private int? _baseLevel;
    private int _count;

    private IshikawaDiagramParser(ParseContext context)
    {
        _context = context;
    }

    public static IshikawaDiagram Parse(ParseContext context, int position) => new IshikawaDiagramParser(context).Parse(position);

    private IshikawaDiagram Parse(int position)
    {
        var text = _context.Text;
        var keywordLength = GetKeywordLength(text, position, text.Length);
        if (keywordLength is 0)
            throw _context.Error(position, "'ishikawa-beta' is expected.");

        MermaidCommon.ApplyCommonConfiguration(_diagram, _context, "ishikawa");

        // The rest of the line of the keyword is a statement too
        var lineStart = position + keywordLength;
        while (lineStart <= text.Length)
        {
            var lineEnd = text.IndexOfOrdinal('\n', lineStart);
            if (lineEnd < 0)
            {
                lineEnd = text.Length;
            }

            ParseLine(lineStart, lineEnd);
            lineStart = lineEnd + 1;
        }

        return _diagram;
    }

    private void ParseLine(int start, int end)
    {
        var text = _context.Text;
        var i = start;
        while (i < end && IsWhiteSpace(text[i]))
        {
            i++;
        }

        if (i == end || (text[i] == '%' && i + 1 < end && text[i + 1] == '%'))
            return;

        if (GetKeywordLength(text, i, end) > 0)
            throw _context.Error(i, "A line cannot start with the keyword 'ishikawa': it is not a cause.");

        var textEnd = end;
        while (textEnd > i && IsWhiteSpace(text[textEnd - 1]))
        {
            textEnd--;
        }

        AddNode(i - start, Decode(text[i..textEnd]), i);
    }

    private void AddNode(int rawLevel, string label, int offset)
    {
        if (!_hasEffect)
        {
            _diagram.Effect = new DiagramText(label);
            _hasEffect = true;
            _stack.Add((0, null));
            return;
        }

        if (++_count > _context.Options.MaxNodes)
            throw _context.Error(offset, $"The diagram has more than {_context.Options.MaxNodes} causes.");

        // The first cause sets the indentation of the main causes
        _baseLevel ??= rawLevel;
        var level = Math.Max(1, rawLevel - _baseLevel.Value + 1);
        while (_stack.Count > 1 && _stack[^1].Level >= level)
        {
            _stack.RemoveAt(_stack.Count - 1);
        }

        if (_stack.Count > _context.Options.MaxDepth)
            throw _context.Error(offset, $"The causes are nested deeper than {_context.Options.MaxDepth} levels.");

        var cause = new IshikawaCause(new DiagramText(label));
        (_stack[^1].Cause?.Causes ?? _diagram.Causes).Add(cause);
        _stack.Add((level, cause));
    }

    /// <summary>Returns the length of the keyword <c>ishikawa-beta</c> or <c>ishikawa</c> at a position, ignoring the case, when a non-word character follows it, as the lexer of Mermaid; 0 otherwise.</summary>
    internal static int GetKeywordLength(string text, int position, int end)
    {
        foreach (var keyword in (ReadOnlySpan<string>)["ishikawa-beta", "ishikawa"])
        {
            var keywordEnd = position + keyword.Length;
            if (keywordEnd <= end && string.Compare(text, position, keyword, 0, keyword.Length, StringComparison.OrdinalIgnoreCase) == 0
                && (keywordEnd == end || !(char.IsAsciiLetterOrDigit(text[keywordEnd]) || text[keywordEnd] == '_')))
            {
                return keyword.Length;
            }
        }

        return 0;
    }

    /// <summary>Decodes a text: the <c>&lt;br&gt;</c>, <c>&lt;br/&gt;</c> and <c>&lt;br /&gt;</c> tags are line breaks, the rest is kept as written.</summary>
    internal static string Decode(string text)
    {
        if (!text.Contains("<br", StringComparison.Ordinal))
            return text;

        var builder = new StringBuilder(text.Length);
        for (var i = 0; i < text.Length; i++)
        {
            if (string.CompareOrdinal(text, i, "<br", 0, 3) == 0)
            {
                var j = i + 3;
                while (j < text.Length && IsWhiteSpace(text[j]))
                {
                    j++;
                }

                if (j < text.Length && text[j] == '/')
                {
                    j++;
                }

                if (j < text.Length && text[j] == '>')
                {
                    builder.Append('\n');
                    i = j;
                    continue;
                }
            }

            builder.Append(text[i]);
        }

        return builder.ToString();
    }

    /// <summary>Indicates whether a character is white space for JavaScript (<c>\s</c>), which Mermaid uses for the indentation and to trim the texts.</summary>
    internal static bool IsWhiteSpace(char c) => c is '\t' or '\n' or '\v' or '\f' or '\r' or ' ' or '\u00A0' or '\u1680' or (>= '\u2000' and <= '\u200A') or '\u2028' or '\u2029' or '\u202F' or '\u205F' or '\u3000' or '\uFEFF';
}
