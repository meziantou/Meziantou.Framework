using System.Text;
using Meziantou.Framework.Fuzzing;

namespace Meziantou.Framework.SyntaxHighlighting.Fuzz;

/// <summary>
/// The highlighter never throws for the content of a text, and its output is safe HTML: the only markup is the
/// emitter's own <c>&lt;span class="..."&gt;</c> and <c>&lt;/span&gt;</c> tags, balanced, never between the two halves of
/// a surrogate pair, and everything else is the input with <c>&amp; &lt; &gt; " '</c> escaped, so that removing the tags
/// and decoding the five entities gives the input back. The match timeout is left infinite: catastrophic backtracking
/// is a bug, which libFuzzer reports as a timeout.
/// </summary>
public static class SyntaxHighlightingFuzzTargets
{
    // Every identifier and alias; an alias highlights like its grammar, so the grammars are reached by several selectors
    private static readonly string[] Languages = [.. SyntaxHighlighter.GetSupportedLanguages()];

    private static readonly string[] ClassPrefixes = ["hljs-", "", "x_-9"];

    private static readonly string[] Tokens =
    [
        "<", ">", "&", "\"", "'", "`", "/", "\\", "{", "}", "(", ")", "[", "]", "@", "#", "$", "%", "^", "*", "-", "=", "+", ":", ";", ",", ".", "~", "|", "!", "?", " ", "\t", "\n", "\r\n",
        "//", "/*", "*/", "<!--", "-->", "\"\"\"", "'''", "```", "${", "#{", "{{", "}}", "{%", "%}", "<%", "%>", "<?", "?>", "<![CDATA[", "]]>", "</", "/>", "=>", "->", "::", "...", "--", "#!",
        "&amp;", "&lt;", "</span>", "<span class=\"", "<script>", "</script>", "<style>", "</style>",
        "if", "else", "for", "while", "return", "function", "def", "class", "end", "let", "var", "const", "true", "false", "null", "nil", "SELECT", "FROM", "WHERE", "import", "#include", "package",
        "0x1F", "1e10", "3.14", "\u00E9", "\u4E2D", "\U0001F600", "\u2028", "\uFEFF", "\uD800",
    ];

    // Snippets of several families of languages: each language starts from one of them
    private static readonly string[] Snippets =
    [
        "using System;\nnamespace N { /* block */ public sealed class C<T> : I where T : new() { private int _x = 0x1F; // line\n    public string M(int a) => $\"v={a}\" + @\"x\"\"y\" + 'c'; }\n}\n#if DEBUG\n#endif\n",
        "const f = async (a, b = 2) => { let s = `t ${a + b}`; return /re[a-z]+\\//gi.test(s) ? 1e3 : null; }; // x\nclass A extends B { #p = 1; static m() { return <div className=\"x\">{y}</div>; } }\n",
        "<!DOCTYPE html>\n<html lang=\"en\"><head><style>body { color: #fff; } a:hover::after { content: \"\\201C\"; }</style><script>var x = '<\\/script>' && 1 < 2;</script></head>\n<body><!-- c --><p class=a data-x='y'>&amp; &lt;</p><![CDATA[x]]><br/></body></html>\n",
        "#!/bin/bash\nset -euo pipefail\nfor f in *.txt; do echo \"${f%.txt} $(date +%s)\" | grep -E '^a' >> out.log; done\nif [[ -n $X && $# -gt 0 ]]; then exit 1; fi\ncat <<EOF\nheredoc $HOME `pwd`\nEOF\n",
        "SELECT a.id, COUNT(*) AS n FROM dbo.[t] a LEFT JOIN u ON u.id = a.uid WHERE a.name LIKE 'x''%' AND b <> 3 -- comment\nGROUP BY a.id /* block */ ORDER BY n DESC;\nCREATE TABLE t (id INT PRIMARY KEY, s VARCHAR(10) DEFAULT N'x');\n",
        "{\"a\": [1, 2.5e-3, true, null, \"s\\u00e9\\n\\\"\"], \"b\": {\"c\": -0, \"d\": {}}, \"e\": []}\n",
        "%YAML 1.2\n---\nkey: value # c\nlist:\n  - &a 1\n  - *a\n  - 'q''q'\n  - \"d\\t\"\nblock: |\n  text\nfolded: >-\n  more\n? complex\n: !!str 2001-12-14\n...\n",
        "# Title\n\nSome *em* and **strong** `code` [link](http://x \"t\") ![img](a.png) <b>html</b>\n\n```js\nvar x = 1;\n```\n\n> quote\n- item\n1. num\n\n| a | b |\n|---|---|\n| 1 | 2 |\n",
        "@decorator(1)\ndef f(a: int = 1, *args, **kw) -> str:\n    \"\"\"doc\"\"\"\n    return f'{a!r:>10}' + r'\\d' + b'x' + u'\\N{BULLET}'  # c\nclass C(object): pass\nlambda x: x ** 2\nasync def g(): await h()\n",
        "(defun f (x) \"doc\" (let ((y 1)) (+ x y))) ; c\nlet f x = match x with | Some y -> y | None -> 0 (* c *)\nmain = putStrLn \"hi\" -- c\n{- block -}\n#[derive(Debug)] fn main() { let r = &'a str; println!(\"{}\", r); }\n",
        "\u00E9\u4E2D\U0001F600 \\\U0001F600 $\U0001F600 ?\U0001F600 #\U0001F600 x '\U0001F600' \"\u2028\" \t\r\n<>&\"' \uD800 x \uDC00",
        "[section]\nkey = value ; c\n# c\nmov eax, [ebx+4] ; c\nlabel: jmp label\nserver { listen 80; location / { proxy_pass http://x; } }\nFROM alpine:3\nRUN echo hi && \\\n  ls\n",
        "<?php echo \"<b>$x</b>\"; ?>\n{% if x %}{{ y | upper }}{% endif %}\n<%= @x %>\n@model M\n@{ var a = 1; }\n<template><div :class=\"c\" @click=\"f\">{{ x }}</div></template>\n",
    ];

    private static readonly string[] MainLanguages = ["csharp", "javascript", "typescript", "html", "xml", "bash", "shell", "sql", "json", "yaml", "markdown", "python", "css", "cpp", "php", "ruby", "rust", "go", "java", "powershell"];

    public static IReadOnlyList<FuzzTarget> All { get; } =
    [
        new("highlight", Highlight) { Seeds = GetSeeds, Dictionary = Tokens },
    ];

    /// <summary>
    /// The input is two bytes that select the language (little endian), a byte of options (<c>IgnoreIllegals</c> and the
    /// class prefix), then the text.
    /// </summary>
    private static void Highlight(ReadOnlySpan<byte> data)
    {
        var selector = FuzzInput.ReadByte(ref data) | (FuzzInput.ReadByte(ref data) << 8);
        var flags = FuzzInput.ReadByte(ref data);
        var language = Languages[selector % Languages.Length];
        var options = new HighlightOptions
        {
            IgnoreIllegals = (flags & 1) == 0,
            ClassPrefix = ClassPrefixes[(flags >> 1) % ClassPrefixes.Length],
        };
        var text = FuzzText.Decode(data);

        var html = SyntaxHighlighter.Highlight(text, language, options);
        CheckHtml(text, html, language);
    }

    private static void CheckHtml(string input, string html, string language)
    {
        var text = new StringBuilder(input.Length);
        var depth = 0;
        var index = 0;
        while (index < html.Length)
        {
            var c = html[index];
            switch (c)
            {
                case '<':
                    var length = MatchEmitterTag(html, index, out var isOpening);
                    if (length == 0)
                        FuzzAssert.Fail($"A '<' at {index} of the output for '{language}' does not start an emitter tag: {FuzzAssert.Format(Excerpt(html, index))}");

                    depth += isOpening ? 1 : -1;
                    if (depth < 0)
                        FuzzAssert.Fail($"The </span> at {index} of the output for '{language}' closes no <span>");

                    index += length;
                    continue;

                case '&':
                    var entity = DecodeEntity(html, index, out var entityLength);
                    if (entity == '\0')
                        FuzzAssert.Fail($"The '&' at {index} of the output for '{language}' does not start one of the entities of the emitter: {FuzzAssert.Format(Excerpt(html, index))}");

                    text.Append(entity);
                    index += entityLength;
                    continue;

                case '>' or '"' or '\'':
                    FuzzAssert.Fail($"The '{c}' at {index} of the output for '{language}' is not escaped: {FuzzAssert.Format(Excerpt(html, index))}");
                    break;
            }

            // Markup between the two halves of a surrogate pair makes the output invalid UTF-16
            if (char.IsLowSurrogate(c) && text.Length > 0 && char.IsHighSurrogate(text[^1]) && !char.IsHighSurrogate(html[index - 1]))
                FuzzAssert.Fail($"A tag splits the surrogate pair before {index} in the output for '{language}': {FuzzAssert.Format(Excerpt(html, index))}");

            text.Append(c);
            index++;
        }

        FuzzAssert.Equal(0, depth, $"Some <span> are never closed in the output for '{language}'");
        FuzzAssert.Equal(input, text.ToString(), $"The text of the output for '{language}' is not the input");
    }

    /// <summary>Matches <c>&lt;/span&gt;</c> or <c>&lt;span class="[A-Za-z0-9_\- ]*"&gt;</c> at an index, and returns its length, or 0.</summary>
    private static int MatchEmitterTag(string html, int index, out bool isOpening)
    {
        isOpening = false;
        var rest = html.AsSpan(index);
        if (rest.StartsWith("</span>", StringComparison.Ordinal))
            return "</span>".Length;

        const string Start = "<span class=\"";
        if (!rest.StartsWith(Start, StringComparison.Ordinal))
            return 0;

        var position = Start.Length;
        while (position < rest.Length && (char.IsAsciiLetterOrDigit(rest[position]) || rest[position] is '_' or '-' or ' '))
        {
            position++;
        }

        if (!rest[position..].StartsWith("\">", StringComparison.Ordinal))
            return 0;

        isOpening = true;
        return position + 2;
    }

    /// <summary>Decodes one of the five entities the emitter writes, or returns <c>'\0'</c>.</summary>
    private static char DecodeEntity(string html, int index, out int length)
    {
        var rest = html.AsSpan(index);
        foreach (var (entity, value) in (ReadOnlySpan<(string, char)>)[("&amp;", '&'), ("&lt;", '<'), ("&gt;", '>'), ("&quot;", '"'), ("&#x27;", '\'')])
        {
            if (rest.StartsWith(entity, StringComparison.Ordinal))
            {
                length = entity.Length;
                return value;
            }
        }

        length = 0;
        return '\0';
    }

    private static string Excerpt(string html, int index)
    {
        var start = Math.Max(0, index - 40);
        return html[start..Math.Min(html.Length, index + 40)];
    }

    private static IEnumerable<FuzzSeed> GetSeeds()
    {
        for (var i = 0; i < Languages.Length; i++)
        {
            yield return CreateSeed(Languages[i], i, flags: 0, Snippets[i % Snippets.Length]);
        }

        foreach (var language in MainLanguages)
        {
            var index = Array.IndexOf(Languages, language);
            if (index < 0)
                continue;

            for (var snippet = 0; snippet < Snippets.Length; snippet++)
            {
                // Odd flags: illegal lexemes make the result plain text
                yield return CreateSeed(language + "-" + snippet, index, (byte)(snippet % 6), Snippets[snippet]);
            }
        }
    }

    private static FuzzSeed CreateSeed(string name, int languageIndex, byte flags, string text) => FuzzSeed.FromText(name, [(byte)languageIndex, (byte)(languageIndex >> 8), flags], text);
}
