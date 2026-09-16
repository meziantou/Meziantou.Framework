using System.Buffers;

namespace Meziantou.Framework.SyntaxHighlighting.Engine;

internal sealed class HtmlEmitter
{
    private static readonly SearchValues<char> EscapeChars = SearchValues.Create("&<>\"'");

    private readonly StringBuilder _buffer;
    private readonly string _classPrefix;

    // Keyed by the scope after alias resolution: grammars embedded as sub-languages share this
    // emitter but each resolves its own aliases.
    private readonly Dictionary<string, string> _tagCache = new(StringComparer.Ordinal);

    public HtmlEmitter(HighlightOptions options, int capacity)
    {
        _classPrefix = options.ClassPrefix;
        _buffer = new StringBuilder(capacity);
    }

    public void OpenScope(string scope, IReadOnlyDictionary<string, string>? aliases)
    {
        var resolved = aliases is not null && aliases.TryGetValue(scope, out var aliased) ? aliased : scope;
        _buffer.Append(GetOpenTag(resolved));
    }

    public void CloseScope() => _buffer.Append("</span>");

    public void AddText(ReadOnlySpan<char> text) => AppendEscaped(text);

    public void OpenSubLanguage(string name) => _buffer.Append("<span class=\"language-").Append(name).Append("\">");

    public void Clear() => _buffer.Clear();

    public string ToHtml() => _buffer.ToString();

    private string GetOpenTag(string scope)
    {
        if (_tagCache.TryGetValue(scope, out var cached))
            return cached;

        var tag = string.Concat("<span class=\"", ScopeToCssClass(scope, _classPrefix), "\">");
        _tagCache[scope] = tag;
        return tag;
    }

    private void AppendEscaped(ReadOnlySpan<char> text)
    {
        while (true)
        {
            var idx = text.IndexOfAny(EscapeChars);
            if (idx < 0)
            {
                if (text.Length > 0)
                    _buffer.Append(text);
                return;
            }

            if (idx > 0)
                _buffer.Append(text[..idx]);
            _buffer.Append(text[idx] switch
            {
                '&' => "&amp;",
                '<' => "&lt;",
                '>' => "&gt;",
                '"' => "&quot;",
                _ => "&#x27;",
            });
            text = text[(idx + 1)..];
        }
    }

    private static string ScopeToCssClass(string name, string classPrefix)
    {
        if (name.StartsWith("language:", StringComparison.Ordinal))
            return string.Concat("language-", name.AsSpan("language:".Length));

        if (!name.Contains('.', StringComparison.Ordinal))
            return classPrefix + name;

        // Tiered scope: comment.line -> "hljs-comment line_"
        var pieces = name.Split('.');
        var sb = new StringBuilder();
        sb.Append(classPrefix).Append(pieces[0]);
        for (var i = 1; i < pieces.Length; i++)
            sb.Append(' ').Append(pieces[i]).Append('_', i);
        return sb.ToString();
    }
}
