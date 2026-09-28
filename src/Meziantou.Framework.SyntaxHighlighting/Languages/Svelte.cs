using Meziantou.Framework.SyntaxHighlighting.Engine;
using Meziantou.Framework.SyntaxHighlighting.Languages.Common;

namespace Meziantou.Framework.SyntaxHighlighting.Languages;

/// <summary>
/// Svelte components (<c>.svelte</c>).
/// </summary>
/// <remarks>
/// highlight.js has no Svelte grammar, so this one is written from scratch on top of the HTML grammar. Expressions
/// (<c>{count + 1}</c>, in text and in attributes) are template variables whose content is JavaScript. Blocks and tags
/// (<c>{#if ...}</c>, <c>{:else}</c>, <c>{/if}</c>, <c>{@html ...}</c>) are template tags whose name is a keyword,
/// as are <c>as</c>, <c>then</c> and <c>catch</c> in their expression. Directive names (<c>on:click|once</c>,
/// <c>bind:value</c>) are attributes. The content of <c>&lt;script&gt;</c> is JavaScript (TypeScript with
/// <c>lang="ts"</c>) whose runes (<c>$state</c>, <c>$derived.by</c>) are built-ins, and the content of
/// <c>&lt;style&gt;</c> is CSS (SCSS or Less with <c>lang="scss"</c> or <c>lang="less"</c>).
/// See https://svelte.dev/docs/svelte/basic-markup and https://svelte.dev/docs/svelte/what-are-runes.
/// </remarks>
internal static class Svelte
{
    // A closing tag cannot occur in an expression, except in a string, so an expression that is not closed ends before
    // it rather than turning the rest of the document into JavaScript.
    private const string ExpressionEndRe = @"\}|<\/[\w-]+\s*>";

    public static CompiledMode Instance { get; } = Compiler.Compile(CreateMode());

    private static Mode CreateMode()
    {
        var html = Html.CreateModes();

        var expression = new Mode
        {
            Scope = "template-variable",
            Begin = @"\{",
            End = @"\}|(?=<\/[\w-]+\s*>)",
            Contains = [CreateExpressionContent(extraModes: [])],
        };

        var block = new Mode
        {
            Scope = "template-tag",
            Begin = @"\{(?:[#/@][a-zA-Z]+|:else\s+if\b|:[a-zA-Z]+)",
            End = @"\}|(?=<\/[\w-]+\s*>)",
            Keywords = Keywords.FromWords(["if", "else", "each", "key", "await", "then", "catch", "snippet", "render", "html", "attach", "const", "debug"]),
            Contains =
            [
                CreateExpressionContent(
                [
                    // `{#each items as item}`, `{#await promise then value}`.
                    new Mode { Scope = "keyword", Begin = @"(?<![\w$.])(?:as|then|catch)(?![\w$])" },
                ]),
            ],
        };

        // Directives, whose modifiers (`on:click|preventDefault`) are part of the name.
        var directive = new Mode { Scope = "attr", Begin = @"(?:on|bind|class|style|use|transition|in|out|animate|let):[^\s=/>""'{}]+" };

        // Runes are called (`$state(0)`, `$state<T>()`, `$state.raw(0)`). They are recognized before the script is
        // highlighted, so they are not recognized after `//`, where they would cut the comment short.
        var rune = new Mode { Scope = "built_in", Begin = @"(?<![\w$.])\$(?:state|derived|effect|props|bindable|inspect|host)(?:\.[a-z]+)?(?=\s*[(<])(?<!//[^\n]*)" };

        // `{name}` is short for `name={name}`, and `{...props}` spreads attributes.
        html.Attributes.Contains = [expression, directive, .. html.Attributes.Contains];
        html.AttributeValue.Variants = [expression, .. html.AttributeValue.Variants!];
        html.DoubleQuotedValue.Contains = [expression, .. html.DoubleQuotedValue.Contains];
        html.SingleQuotedValue.Contains = [expression, .. html.SingleQuotedValue.Contains];

        // Like the HTML script body, this one restarts from the root of the JavaScript grammar, so it does not resume the
        // state the last expression of the markup (which is JavaScript too) ended in.
        html.ScriptTag.Starts = new Mode(html.ScriptBody) { Contains = [rune] };
        html.Root.Contains =
        [
            block,
            expression,
            HtmlComponents.CreateTagWithLang(html, "script", "ts|typescript", "typescript", [rune]),
            HtmlComponents.CreateTagWithLang(html, "style", "scss", "scss"),
            HtmlComponents.CreateTagWithLang(html, "style", "less", "less"),
            HtmlComponents.CreateTagWithLang(html, "style", "sass|styl|stylus", subLanguage: null),
            .. html.Root.Contains,
        ];

        return html.Root;
    }

    private static Mode CreateExpressionContent(Mode[] extraModes)
    {
        // Nested braces and strings are skipped, so the expression ends at its own `}`, not at the first one of its code.
        var escape = new Mode { Begin = @"\\[\s\S]", Skip = true };
        var doubleQuoted = new Mode { Begin = "\"", End = "\"|$", Skip = true, Contains = [escape] };
        var singleQuoted = new Mode { Begin = "'", End = "'|$", Skip = true, Contains = [escape] };
        var template = new Mode { Begin = "`", End = "`", Skip = true, Contains = [escape] };
        var braces = new Mode { Begin = @"\{", End = @"\}", Skip = true };
        braces.Contains = [braces, doubleQuoted, singleQuoted, template];

        return new Mode
        {
            // The content starts right after the `{`, unless the expression is empty.
            Begin = "(?!" + ExpressionEndRe + @")(?=[\s\S])",
            End = "(?=" + ExpressionEndRe + ")",
            SubLanguage = "javascript",
            RestartsSubLanguage = true,
            Contains = [braces, doubleQuoted, singleQuoted, template, .. extraModes],
        };
    }
}
