using System.Text;
using Meziantou.Framework.Fuzzing;

namespace Meziantou.Framework.SyntaxHighlighting.Fuzz;

/// <summary>
/// The highlighter never throws for the content of a text, and its output is safe HTML: the only markup is the
/// emitter's own <c>&lt;span class="..."&gt;</c> and <c>&lt;/span&gt;</c> tags, balanced, never between the two halves of
/// a surrogate pair, and everything else is the input with <c>&amp; &lt; &gt; " '</c> escaped, so that removing the tags
/// and decoding the five entities gives the input back. The match timeout is left infinite: catastrophic backtracking
/// is a bug, which libFuzzer reports as a timeout. A large finite timeout, which compiles every regex again, gives the
/// same output; the language names are case-insensitive; and the class prefix only changes the first class of the tags.
/// </summary>
public static class SyntaxHighlightingFuzzTargets
{
    // Every identifier and alias; an alias highlights like its grammar, so the grammars are reached by several selectors
    private static readonly string[] Languages = [.. SyntaxHighlighter.GetSupportedLanguages()];

    private static readonly string[] ClassPrefixes = ["hljs-", "", "x_-9"];

    // Far longer than any match takes, so that it never expires: the output must be the one of the infinite timeout.
    // The library caches the grammars compiled for a timeout, so a single finite value compiles each grammar only once.
    private static readonly HighlightOptions[] FiniteTimeoutOptions =
    [
        new() { IgnoreIllegals = false, MatchTimeout = TimeSpan.FromSeconds(10) },
        new() { IgnoreIllegals = true, MatchTimeout = TimeSpan.FromSeconds(10) },
    ];

    private static readonly HighlightOptions[] DefaultOptions =
    [
        new() { IgnoreIllegals = false },
        new() { IgnoreIllegals = true },
    ];

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
        new("highlight-language-name", HighlightLanguageName) { Seeds = GetLanguageNameSeeds, Dictionary = [.. Tokens, .. Languages] },
        new("highlight-class-prefix", HighlightClassPrefix) { Seeds = GetClassPrefixSeeds, Dictionary = [.. Tokens, .. ClassPrefixes.Where(prefix => prefix.Length > 0), "hljs", "language-", "\0"] },
    ];

    /// <summary>
    /// The input is two bytes that select the language (little endian), a byte of options (<c>IgnoreIllegals</c>, the
    /// class prefix, and in its high bit the comparison with a finite match timeout), then the text.
    /// </summary>
    private static void Highlight(ReadOnlySpan<byte> data)
    {
        var selector = FuzzInput.ReadByte(ref data) | (FuzzInput.ReadByte(ref data) << 8);
        var flags = FuzzInput.ReadByte(ref data);
        var language = Languages[selector % Languages.Length];
        var options = new HighlightOptions
        {
            IgnoreIllegals = (flags & 1) == 0,
            ClassPrefix = ClassPrefixes[((flags & 0x7F) >> 1) % ClassPrefixes.Length],
        };
        var text = FuzzText.Decode(data);

        var html = SyntaxHighlighter.Highlight(text, language, options);
        CheckHtml(text, html, language);

        if ((flags & 0x80) != 0)
        {
            var reference = options.ClassPrefix == "hljs-" ? html : SyntaxHighlighter.Highlight(text, language, DefaultOptions[options.IgnoreIllegals ? 1 : 0]);
            var timed = SyntaxHighlighter.Highlight(text, language, FiniteTimeoutOptions[options.IgnoreIllegals ? 1 : 0]);
            FuzzAssert.Equal(reference, timed, $"The output for '{language}' with a finite match timeout differs from the output with the infinite one");
        }
    }

    /// <summary>
    /// The input is a byte of options, then either a known language name (when the low bit is 0: two bytes that select
    /// it, little endian, then a byte whose bits change the case of its letters) or a fuzzed name (the text up to a 0
    /// byte), then the text to highlight. <see cref="SyntaxHighlighter.IsSupported"/>, <see cref="SyntaxHighlighter.TryHighlight"/>
    /// and <see cref="SyntaxHighlighter.Highlight"/> agree, and a supported name highlights like the identifier it matches
    /// ignoring the case of ASCII letters (no other character folds to an ASCII one).
    /// </summary>
    private static void HighlightLanguageName(ReadOnlySpan<byte> data)
    {
        var flags = FuzzInput.ReadByte(ref data);
        string name;
        if ((flags & 1) == 0)
        {
            var selector = FuzzInput.ReadByte(ref data) | (FuzzInput.ReadByte(ref data) << 8);
            var caseMask = FuzzInput.ReadByte(ref data);
            name = ChangeCase(Languages[selector % Languages.Length], caseMask);
        }
        else
        {
            name = FuzzText.Decode(FuzzInput.ReadUntil(ref data, 0));
        }

        var options = DefaultOptions[(flags >> 1) & 1];
        var text = FuzzText.Decode(data);
        var identifier = Array.Find(Languages, language => EqualsIgnoreAsciiCase(language, name));

        var isSupported = SyntaxHighlighter.IsSupported(name);
        FuzzAssert.Equal(identifier is not null, isSupported, $"IsSupported({FuzzAssert.Format(name)}) is wrong");

        var succeeded = SyntaxHighlighter.TryHighlight(text, name, out var tryHtml, options);
        FuzzAssert.Equal(isSupported, succeeded, $"TryHighlight with {FuzzAssert.Format(name)} disagrees with IsSupported");

        string? html;
        try
        {
            html = SyntaxHighlighter.Highlight(text, name, options);
        }
        catch (NotSupportedException)
        {
            html = null;
        }

        if (identifier is null)
        {
            FuzzAssert.True(html is null, $"Highlight with the unsupported language {FuzzAssert.Format(name)} does not throw a NotSupportedException");
            FuzzAssert.True(tryHtml is null, $"TryHighlight with the unsupported language {FuzzAssert.Format(name)} returns some HTML");
            return;
        }

        FuzzAssert.True(html is not null, $"Highlight with the supported language {FuzzAssert.Format(name)} throws a NotSupportedException");
        FuzzAssert.Equal(html, tryHtml, $"TryHighlight and Highlight with {FuzzAssert.Format(name)} give different outputs");
        FuzzAssert.Equal(SyntaxHighlighter.Highlight(text, identifier, options), html, $"The output for {FuzzAssert.Format(name)} differs from the output for '{identifier}'");
        CheckHtml(text, html, identifier);
    }

    /// <summary>
    /// The input is two bytes that select the language (little endian), a byte of options (<c>IgnoreIllegals</c>), a class
    /// prefix (the text up to a 0 byte), then the text. The prefix is rejected with an <see cref="ArgumentException"/>
    /// exactly when it contains a character other than an ASCII letter, a digit, <c>-</c> or <c>_</c>; an accepted prefix
    /// replaces <c>hljs-</c> at the start of the class attributes and changes nothing else.
    /// </summary>
    private static void HighlightClassPrefix(ReadOnlySpan<byte> data)
    {
        var selector = FuzzInput.ReadByte(ref data) | (FuzzInput.ReadByte(ref data) << 8);
        var flags = FuzzInput.ReadByte(ref data);
        var language = Languages[selector % Languages.Length];
        var prefix = FuzzText.Decode(FuzzInput.ReadUntil(ref data, 0));
        var text = FuzzText.Decode(data);
        var isValid = prefix.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_');

        HighlightOptions options;
        try
        {
            options = new HighlightOptions { IgnoreIllegals = (flags & 1) == 0, ClassPrefix = prefix };
        }
        catch (ArgumentException ex) when (ex.GetType() == typeof(ArgumentException))
        {
            FuzzAssert.False(isValid, $"The class prefix {FuzzAssert.Format(prefix)} is rejected: {ex.Message}");
            return;
        }

        FuzzAssert.True(isValid, $"The class prefix {FuzzAssert.Format(prefix)} is accepted");
        FuzzAssert.Equal(prefix, options.ClassPrefix, "The class prefix is not the one that was set");

        var html = SyntaxHighlighter.Highlight(text, language, options);
        CheckHtml(text, html, language);

        const string DefaultTag = "<span class=\"hljs-";
        var reference = SyntaxHighlighter.Highlight(text, language, DefaultOptions[options.IgnoreIllegals ? 1 : 0]);
        FuzzAssert.Equal(reference.Replace(DefaultTag, "<span class=\"" + prefix, StringComparison.Ordinal), html, $"The class prefix {FuzzAssert.Format(prefix)} changes more than the class names of the output for '{language}'");
    }

    /// <summary>Changes the case of the ASCII letters of a name whose index (modulo 8) is set in <paramref name="mask"/>.</summary>
    private static string ChangeCase(string name, byte mask) => string.Create(name.Length, (name, mask), static (span, state) =>
    {
        for (var i = 0; i < span.Length; i++)
        {
            var c = state.name[i];
            span[i] = (state.mask & (1 << (i % 8))) != 0 && char.IsAsciiLetter(c) ? (char)(c ^ 0x20) : c;
        }
    });

    private static bool EqualsIgnoreAsciiCase(string a, string b)
    {
        if (a.Length != b.Length)
            return false;

        for (var i = 0; i < a.Length; i++)
        {
            if (a[i] != b[i] && !(char.IsAsciiLetter(a[i]) && (a[i] ^ 0x20) == b[i]))
                return false;
        }

        return true;
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

            // High bit: compare with a finite match timeout
            yield return CreateSeed(language + "-timeout", index, (byte)(0x80 | (index % 6)), Snippets[index % Snippets.Length]);
        }
    }

    private static IEnumerable<FuzzSeed> GetLanguageNameSeeds()
    {
        for (var i = 0; i < Languages.Length; i++)
        {
            yield return FuzzSeed.FromText("known-" + i, [(byte)((i & 1) << 1), (byte)i, (byte)(i >> 8), (byte)(i * 37)], Snippets[i % Snippets.Length]);
        }

        // Near misses: no other character folds to an ASCII letter, and the name is not trimmed
        string[] names = ["", "CSharp", "C#", "unknown", " c", "c ", "c\t", "\u017Fh", "\u212Ash", "\u0130ni", "\u0131ni", "\uFF43", "c\uD800", "\U0001F600", "language-c", "hljs", "js;", "c++\u0301"];
        for (var i = 0; i < names.Length; i++)
        {
            yield return FuzzSeed.FromText("name-" + i, [(byte)(1 | ((i & 1) << 1)), .. FuzzText.Encode(names[i]), 0], Snippets[i % Snippets.Length]);
        }
    }

    private static IEnumerable<FuzzSeed> GetClassPrefixSeeds()
    {
        string[] prefixes = ["hljs-", "", "x_-9", "my-", "A", "-", "_", "hljs-hljs-", "a b", "a\"b", "<", "&", "x>", "\u00E9", "\uD800", "\U0001F600", "a.b", "a\tb", "\uFF41"];
        for (var i = 0; i < prefixes.Length; i++)
        {
            var languageIndex = Array.IndexOf(Languages, MainLanguages[i % MainLanguages.Length]);
            yield return FuzzSeed.FromText("prefix-" + i, [(byte)languageIndex, (byte)(languageIndex >> 8), (byte)(i % 2), .. FuzzText.Encode(prefixes[i]), 0], Snippets[i % Snippets.Length]);
        }
    }

    private static FuzzSeed CreateSeed(string name, int languageIndex, byte flags, string text) => FuzzSeed.FromText(name, [(byte)languageIndex, (byte)(languageIndex >> 8), flags], text);
}
