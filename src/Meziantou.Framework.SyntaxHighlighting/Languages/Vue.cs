using Meziantou.Framework.SyntaxHighlighting.Engine;
using Meziantou.Framework.SyntaxHighlighting.Languages.Common;

namespace Meziantou.Framework.SyntaxHighlighting.Languages;

/// <summary>
/// Vue single-file components (<c>.vue</c>).
/// </summary>
/// <remarks>
/// highlight.js has no Vue grammar, so this one is written from scratch on top of the HTML grammar: the template is
/// HTML, text interpolations (<c>{{ ... }}</c>) are template variables whose content is JavaScript, directive names
/// (<c>v-if</c>, <c>:prop</c>, <c>@event</c>, <c>#slot</c>) are attributes whose values are JavaScript, the content of
/// <c>&lt;script&gt;</c> is JavaScript (TypeScript with <c>lang="ts"</c>), and the content of <c>&lt;style&gt;</c> is
/// CSS (SCSS or Less with <c>lang="scss"</c> or <c>lang="less"</c>). The content of an element whose language has no
/// grammar (<c>&lt;template lang="pug"&gt;</c>, <c>&lt;style lang="stylus"&gt;</c>) is plain text.
/// See https://vuejs.org/api/sfc-spec.html and https://vuejs.org/guide/essentials/template-syntax.html.
/// </remarks>
internal static class Vue
{
    // A closing tag cannot occur in an expression, so an interpolation that is not closed ends before it rather than
    // turning the rest of the document into JavaScript.
    private const string InterpolationEndRe = @"\}\}|<\/[\w-]+\s*>";

    public static CompiledMode Instance { get; } = Compiler.Compile(CreateMode());

    private static Mode CreateMode()
    {
        var html = Html.CreateModes();

        // `v-name:argument.modifier`, and the shorthands `:argument` (v-bind), `@argument` (v-on) and `#argument`
        // (v-slot). An argument can be dynamic (`:[key]`) and contain colons (`@update:model-value`). Like in Vue, a
        // dynamic argument ends at the end of the attribute name, and it cannot contain another `[`, so an unclosed one
        // is not scanned again from each `[` of the rest of the name.
        const string ArgumentRe = @"(?:\[[^\[\]\s=/>]*\]|[\w:-]+)";
        var directive = new Mode
        {
            Scope = "attr",
            Begin = @"(?:v-[\w-]+(?::" + ArgumentRe + ")?|[:@#]" + ArgumentRe + @")(?:\.[\w-]+)*",
            Starts = new Mode
            {
                // Ends right away, unless the directive has a value.
                End = @"\B|\b",
                Contains =
                [
                    CreateDirectiveValue(@"\s*=\s*""", "\""),
                    CreateDirectiveValue(@"\s*=\s*'", "'"),
                    CreateDirectiveValue(@"\s*=\s*(?=[^\s""'=<>`])", @"(?=[\s>]|\/>)"),
                ],
            },
        };

        var interpolation = new Mode
        {
            Scope = "template-variable",
            Begin = @"\{\{",
            End = @"\}\}|(?=<\/[\w-]+\s*>)",
            Contains =
            [
                new Mode
                {
                    Begin = "(?!" + InterpolationEndRe + @")(?=[\s\S])",
                    End = "(?=" + InterpolationEndRe + ")",
                    SubLanguage = "javascript",
                    RestartsSubLanguage = true,
                },
            ],
        };

        html.Attributes.Contains = [directive, .. html.Attributes.Contains];
        html.Root.Contains =
        [
            interpolation,
            HtmlComponents.CreateTagWithLang(html, "template", "pug|jade", subLanguage: null),
            HtmlComponents.CreateTagWithLang(html, "script", "ts|tsx|typescript", "typescript"),
            HtmlComponents.CreateTagWithLang(html, "style", "scss", "scss"),
            HtmlComponents.CreateTagWithLang(html, "style", "less", "less"),
            HtmlComponents.CreateTagWithLang(html, "style", "sass|styl|stylus", subLanguage: null),
            .. html.Root.Contains,
        ];

        return html.Root;
    }

    private static Mode CreateDirectiveValue(string begin, string end)
    {
        return new Mode
        {
            Begin = begin,
            End = end,
            SubLanguage = "javascript",
            RestartsSubLanguage = true,
            ExcludeBegin = true,
            ExcludeEnd = true,
        };
    }
}
