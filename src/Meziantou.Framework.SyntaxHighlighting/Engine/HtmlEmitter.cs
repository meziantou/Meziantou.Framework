using System.Buffers;

namespace Meziantou.Framework.SyntaxHighlighting.Engine;

internal sealed class HtmlEmitter
{
    private static readonly SearchValues<char> EscapeChars = SearchValues.Create("&<>\"'");

    private readonly StringBuilder _buffer = new();
    private readonly HighlightOptions _options;
    private readonly IReadOnlyDictionary<string, string>? _aliases;
    private readonly Dictionary<string, string> _tagCache = new(StringComparer.Ordinal);

    public HtmlEmitter(HighlightOptions options, IReadOnlyDictionary<string, string>? aliases)
    {
        _options = options;
        _aliases = aliases;
    }

    public void OpenScope(string scope) => _buffer.Append(GetOpenTag(scope));

    public void CloseScope() => _buffer.Append("</span>");

    public void AddText(string text) => AppendEscaped(text.AsSpan());

    public void AddText(ReadOnlySpan<char> text) => AppendEscaped(text);

    public string ToHtml() => _buffer.ToString();

    public void OpenSubLanguage(string name) =>
        _buffer.Append("<span class=\"language-").Append(name).Append("\">");

    public void AppendRaw(string html) => _buffer.Append(html);

    private string GetOpenTag(string scope)
    {
        if (_tagCache.TryGetValue(scope, out var cached))
            return cached;

        var resolved = _aliases is not null && _aliases.TryGetValue(scope, out var aliased) ? aliased : scope;
        var tag = string.Concat("<span class=\"", ScopeToCssClass(resolved, _options.ClassPrefix), "\">");
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
