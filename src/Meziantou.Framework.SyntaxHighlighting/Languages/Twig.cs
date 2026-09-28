using Meziantou.Framework.SyntaxHighlighting.Engine;
using Meziantou.Framework.SyntaxHighlighting.Languages.Common;

namespace Meziantou.Framework.SyntaxHighlighting.Languages;

internal static class Twig
{
    private static readonly string[] FunctionNames =
    [
        "absolute_url", "asset|0", "asset_version", "attribute", "block", "constant", "controller|0", "country_timezones",
        "csrf_token", "cycle", "date", "dump", "expression", "form|0", "form_end", "form_errors", "form_help", "form_label",
        "form_rest", "form_row", "form_start", "form_widget", "html_classes", "include", "is_granted", "logout_path",
        "logout_url", "max", "min", "parent", "path|0", "random", "range", "relative_path", "render", "render_esi", "source",
        "template_from_string", "url|0",
    ];

    private static readonly string[] Filters =
    [
        "abs", "abbr_class", "abbr_method", "batch", "capitalize", "column", "convert_encoding", "country_name",
        "currency_name", "currency_symbol", "data_uri", "date", "date_modify", "default", "escape", "file_excerpt",
        "file_link", "file_relative", "filter", "first", "format", "format_args", "format_args_as_text", "format_currency",
        "format_date", "format_datetime", "format_file", "format_file_from_text", "format_number", "format_time",
        "html_to_markdown", "humanize", "inky_to_html", "inline_css", "join", "json_encode", "keys", "language_name", "last",
        "length", "locale_name", "lower", "map", "markdown", "markdown_to_html", "merge", "nl2br", "number_format", "raw",
        "reduce", "replace", "reverse", "round", "slice", "slug", "sort", "spaceless", "split", "striptags", "timezone_name",
        "title", "trans", "transchoice", "trim", "u|0", "upper", "url_encode", "yaml_dump", "yaml_encode",
    ];

    private static readonly string[] TagNames =
    [
        "apply", "autoescape", "block", "cache", "deprecated", "do", "embed", "extends", "filter", "flush", "for",
        "form_theme", "from", "if", "import", "include", "macro", "sandbox", "set", "stopwatch", "trans",
        "trans_default_domain", "transchoice", "use", "verbatim", "with",
    ];

    // Declared after the name lists, which it uses: static fields are initialized in declaration order.
    public static CompiledMode Instance { get; } = Compiler.Compile(CreateMode());

    private static Mode CreateMode()
    {
        var str = new Mode
        {
            Scope = "string",

            // Deviation from highlight.js, which does not know the backslash escapes: the string of `'it\'s'` ended at
            // the escaped quote, and the quote that really closes it opened a string that swallowed the rest of the
            // document, template tags included.
            Contains = [CommonModes.BackslashEscape],
            Variants =
            [
                new Mode { Begin = "'", End = "'" },
                new Mode { Begin = "\"", End = "\"" },
            ],
        };

        var number = new Mode { Scope = "number", Match = @"\d+" };

        var parameters = new Mode
        {
            Begin = @"\(",
            End = @"\)",
            ExcludeBegin = true,
            ExcludeEnd = true,
            Contains = [str, number],
        };

        var functions = new Mode
        {
            // Deviation from highlight.js, which joins the names with their relevance suffix (`asset|0`) into the begin
            // pattern, where `|0` is an alternative: a `0` was taken for a function name, so it was never a number.
            BeginKeywords = [.. FunctionNames.Select(name => name.Split('|')[0])],
            Keywords = Engine.Keywords.FromMap(new Dictionary<string, string[]>(StringComparer.Ordinal) { ["name"] = FunctionNames }),
            Contains = [parameters],
        };

        var filter = new Mode
        {
            BeginParts = [@"\|(?=[A-Za-z_]+:?)"],
            BeginScope = new Dictionary<int, string> { [1] = "punctuation" },
            Contains =
            [
                new Mode { Match = @"[A-Za-z_]+:?", Keywords = Engine.Keywords.FromWords(Filters) },
            ],
        };

        Mode TagNamed(string tagNamesRe) => new()
        {
            // Deviation from highlight.js, which does not allow the whitespace control modifiers (`{%-`, `-%}`, `{%~`,
            // `~%}`), so such a tag was not highlighted at all.
            BeginParts = [@"\{%[-~]?", @"\s*", tagNamesRe],
            BeginScope = new Dictionary<int, string> { [1] = "template-tag", [3] = "name" },
            EndScope = "template-tag",
            End = @"[-~]?%\}",
            Keywords = Engine.Keywords.FromWords(["in"]),
            Contains = [filter, functions, str, number],
        };

        return new Mode
        {
            CaseInsensitive = true,

            // Deviation from highlight.js, which embeds `xml`: its xml grammar is this library's `html` grammar (the
            // `xml` one does not highlight <script> and <style> contents), so the markup is wrapped in `language-html`.
            SubLanguage = "html",
            Contains =
            [
                CommonModes.Comment(@"\{#", "#\\}"),

                // Deviation from highlight.js, where a tag name could end in the middle of a word: `{% form_theme %}` was
                // the `for` tag followed by `m_theme`, and `{% blockquote %}` the `block` tag.
                TagNamed("(?:" + string.Join('|', TagNames.Concat(TagNames.Select(name => "end" + name))) + ")(?![a-z_])"),
                TagNamed("[a-z_]+"),
                new Mode
                {
                    Scope = "template-variable",
                    Begin = @"\{\{",
                    End = @"\}\}",
                    Contains = [Mode.Self, filter, functions, str, number],
                },
            ],
        };
    }
}
