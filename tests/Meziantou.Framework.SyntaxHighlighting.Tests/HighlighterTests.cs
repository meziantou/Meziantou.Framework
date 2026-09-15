using Meziantou.Framework.SyntaxHighlighting.Engine;
using Meziantou.Framework.SyntaxHighlighting.Languages;

namespace Meziantou.Framework.SyntaxHighlighting.Tests;

public class HighlighterTests
{
    [Fact]
    public void Highlight()
    {
        AssertHighlighter("csharp",
            """
            public class MyClass { }
            """,
            """
            <span class="hljs-keyword">public</span> <span class="hljs-keyword">class</span> <span class="hljs-title">MyClass</span> { }
            """);
    }

    [Fact]
    public void Highlight_CustomClassPrefix()
    {
        var result = SyntaxHighlighter.Highlight(
            "public class MyClass { }",
            "csharp",
            new HighlightOptions { ClassPrefix = "syntax-" });

        Assert.Equal(
            """<span class="syntax-keyword">public</span> <span class="syntax-keyword">class</span> <span class="syntax-title">MyClass</span> { }""",
            result);
    }

    [Fact]
    public void Highlight_DifferentOptions_DoNotCrossContaminate()
    {
        const string Code = "public class C { }";

        var defaultResult = SyntaxHighlighter.Highlight(Code, "csharp");
        var customResult = SyntaxHighlighter.Highlight(Code, "csharp", new HighlightOptions { ClassPrefix = "x-" });
        var defaultResult2 = SyntaxHighlighter.Highlight(Code, "csharp");

        Assert.Contains("hljs-keyword", defaultResult);
        Assert.DoesNotContain("x-keyword", defaultResult);
        Assert.Contains("x-keyword", customResult);
        Assert.DoesNotContain("hljs-keyword", customResult);
        Assert.Equal(defaultResult, defaultResult2);
    }

    [Fact]
    public void Highlight_NullText_ThrowsArgumentNullException()
    {
        var exception = Assert.Throws<ArgumentNullException>(() => SyntaxHighlighter.Highlight(text: null!, "csharp"));

        Assert.Equal("text", exception.ParamName);
    }

    [Fact]
    public void Highlight_NullLanguage_ThrowsArgumentNullException()
    {
        var exception = Assert.Throws<ArgumentNullException>(() => SyntaxHighlighter.Highlight("class C { }", language: null!));

        Assert.Equal("language", exception.ParamName);
    }

    [Fact]
    public void Highlight_UnknownLanguage_ThrowsNotSupportedException()
    {
        Assert.Throws<NotSupportedException>(() => SyntaxHighlighter.Highlight("class C { }", "not-a-language"));
    }

    [Fact]
    public void Highlight_EmptyText_ReturnsEmptyString()
    {
        Assert.Empty(SyntaxHighlighter.Highlight("", "csharp"));
    }

    [Theory]
    [InlineData("csharp")]
    [InlineData("cs")]
    [InlineData("c#")]
    [InlineData("CSharp")]
    [InlineData("C#")]
    [InlineData("YAML")]
    public void IsSupported_KnownLanguage_ReturnsTrue(string language)
    {
        Assert.True(SyntaxHighlighter.IsSupported(language));
    }

    [Theory]
    [InlineData("not-a-language")]
    [InlineData("")]
    [InlineData("rust")]
    public void IsSupported_UnknownLanguage_ReturnsFalse(string language)
    {
        Assert.False(SyntaxHighlighter.IsSupported(language));
    }

    [Fact]
    public void IsSupported_Null_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => SyntaxHighlighter.IsSupported(null!));
    }

    [Fact]
    public void TryHighlight_KnownLanguage_ReturnsTrueAndMarkup()
    {
        Assert.True(SyntaxHighlighter.TryHighlight("class C { }", "csharp", out var html));
        Assert.Equal("""<span class="hljs-keyword">class</span> <span class="hljs-title">C</span> { }""", html);
    }

    [Fact]
    public void TryHighlight_UnknownLanguage_ReturnsFalse()
    {
        Assert.False(SyntaxHighlighter.TryHighlight("class C { }", "not-a-language", out var html));
        Assert.Null(html);
    }

    [Fact]
    public void TryHighlight_HonoursOptions()
    {
        Assert.True(SyntaxHighlighter.TryHighlight("class C { }", "csharp", out var html, new HighlightOptions { ClassPrefix = "x-" }));
        Assert.Contains("x-keyword", html);
    }

    [Fact]
    public void GetSupportedLanguages_HasNoDuplicates()
    {
        var duplicates = SyntaxHighlighter.GetSupportedLanguages()
            .GroupBy(language => language, StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key);

        Assert.Empty(duplicates);
    }

    /// <summary>
    /// Ties the advertised list to reality: every identifier the registry reports must actually
    /// resolve to a grammar that can highlight, so the list cannot drift from what works.
    /// </summary>
    [Fact]
    public void GetSupportedLanguages_EveryEntryCanHighlight()
    {
        foreach (var language in SyntaxHighlighter.GetSupportedLanguages())
        {
            Assert.True(SyntaxHighlighter.IsSupported(language));
            Assert.True(SyntaxHighlighter.TryHighlight("x = 1", language, out var html));
            Assert.NotNull(html);
        }
    }
    [Fact]
    public void Highlight_ClassPrefix_AppliesToTieredScopesButNotToSubLanguages()
    {
        var options = new HighlightOptions { ClassPrefix = "x-" };

        Assert.Equal(
            """<span class="x-keyword">class</span> <span class="x-title class_">A</span> {}""",
            SyntaxHighlighter.Highlight("class A {}", "typescript", options));
        Assert.Contains("""<span class="language-css">""", SyntaxHighlighter.Highlight("<style>a { color: red; }</style>", "html", options), ignoreCase: false);
    }

    [Fact]
    public void HighlightOptions_NullClassPrefix_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => new HighlightOptions { ClassPrefix = null! });
    }

    [Theory]
    [InlineData("x\" onmouseover=\"alert(1)")]
    [InlineData("a b")]
    [InlineData("é")]
    public void HighlightOptions_InvalidClassPrefix_ThrowsArgumentException(string prefix)
    {
        Assert.Throws<ArgumentException>(() => new HighlightOptions { ClassPrefix = prefix });
    }

    [Fact]
    public void HighlightOptions_EmptyClassPrefix_IsAllowed()
    {
        Assert.Equal("""<span class="keyword">class</span> <span class="title">C</span> { }""", SyntaxHighlighter.Highlight("class C { }", "csharp", new HighlightOptions { ClassPrefix = "" }));
    }

    [Fact]
    public void Highlight_IllegalLexeme_IsEmittedAsTextByDefault()
    {
        Assert.Equal(
            """<span class="hljs-punctuation">{</span><span class="hljs-attr">&quot;a&quot;</span><span class="hljs-punctuation">:</span> <span class="hljs-number">1</span><span class="hljs-punctuation">}</span>;""",
            SyntaxHighlighter.Highlight("{\"a\": 1};", "json"));
    }

    [Fact]
    public void Highlight_IgnoreIllegalsFalse_IllegalLexemeReturnsPlainText()
    {
        Assert.Equal("{&quot;a&quot;: 1};", SyntaxHighlighter.Highlight("{\"a\": 1};", "json", new HighlightOptions { IgnoreIllegals = false }));
    }

    [Fact]
    public void Highlight_IgnoreIllegalsFalse_SubLanguagesStillIgnoreIllegals()
    {
        Assert.Equal(
            "<span class=\"hljs-keyword\">const</span> s = css`<span class=\"language-css\">\n  $primary: red;\n  <span class=\"hljs-selector-class\">.btn</span> { <span class=\"hljs-attribute\">color</span>: red; }\n`</span>;",
            SyntaxHighlighter.Highlight("const s = css`\n  $primary: red;\n  .btn { color: red; }\n`;", "javascript", new HighlightOptions { IgnoreIllegals = false }));
    }

    [Fact]
    public void Highlight_SubLanguage_ResumesInTheStateThePreviousFragmentEndedIn()
    {
        // The markup after `${u}` is still inside the attribute and the tag.
        Assert.Equal(
            """<span class="hljs-keyword">const</span> h = html`<span class="language-xml"><span class="hljs-tag">&lt;<span class="hljs-name">a</span> <span class="hljs-attr">href</span>=<span class="hljs-string">&quot;</span></span></span><span class="hljs-subst">${u}</span><span class="language-xml"><span class="hljs-tag"><span class="hljs-string">&quot;</span> <span class="hljs-attr">class</span>=<span class="hljs-string">&quot;c&quot;</span>&gt;</span>t<span class="hljs-tag">&lt;/<span class="hljs-name">a</span>&gt;</span>`</span>;""",
            SyntaxHighlighter.Highlight("const h = html`<a href=\"${u}\" class=\"c\">t</a>`;", "javascript"));
    }

    [Theory]
    [InlineData("csharp", "// comment\nvar x = 1;\n#region A\n")]
    [InlineData("dockerfile", "FROM x\nRUN echo a \\\n  b\n")]
    [InlineData("http", "GET / HTTP/1.1\nHost: x")]
    [InlineData("markdown", "    code\n\ntext *em*\n\n# Heading")]
    [InlineData("php", "<?php\n$a = <<<EOT\nhi\nEOT;\n")]
    [InlineData("yaml", "items:\n  - a\n")]
    public void Highlight_CrLfLineEndings_HighlightLikeLfLineEndings(string language, string code)
    {
        var lf = SyntaxHighlighter.Highlight(code, language);
        var crlf = SyntaxHighlighter.Highlight(code.ReplaceLineEndings("\r\n"), language);

        Assert.Equal(lf.ReplaceLineEndings("\r\n"), crlf);
    }

    [Fact]
    public void Highlight_MixedLineEndings_ArePreserved()
    {
        Assert.Equal(
            "<span class=\"hljs-attr\">a:</span> <span class=\"hljs-number\">1</span>\r\n<span class=\"hljs-attr\">b:</span> <span class=\"hljs-number\">2</span>\n<span class=\"hljs-attr\">c:</span> <span class=\"hljs-number\">3</span>\r\n\r",
            SyntaxHighlighter.Highlight("a: 1\r\nb: 2\nc: 3\r\n\r", "yaml"));
    }

    [Fact]
    public void Highlight_ByteOrderMark_IsKeptAndDoesNotPreventHighlighting()
    {
        Assert.Equal(
            "﻿<span class=\"hljs-punctuation\">{</span><span class=\"hljs-attr\">&quot;a&quot;</span><span class=\"hljs-punctuation\">:</span> <span class=\"hljs-number\">1</span><span class=\"hljs-punctuation\">}</span>",
            SyntaxHighlighter.Highlight("﻿{\"a\": 1}", "json", new HighlightOptions { IgnoreIllegals = false }));
    }

    [Fact]
    public void Highlight_MatchTimeout_HighlightsLikeTheDefault()
    {
        const string Code = "<style>a { color: red; }</style><script>var x = 1;</script>";

        Assert.Equal(SyntaxHighlighter.Highlight(Code, "html"), SyntaxHighlighter.Highlight(Code, "html", new HighlightOptions { MatchTimeout = TimeSpan.FromSeconds(30) }));
    }

    [Fact]
    public void LanguageRegistry_CustomMatchTimeout_CompilesTheGrammarOnce()
    {
        var timeout = TimeSpan.FromSeconds(3);

        var grammar = LanguageRegistry.Get("csharp", timeout);

        Assert.Same(grammar, LanguageRegistry.Get("cs", timeout));
        Assert.NotSame(LanguageRegistry.Get("csharp"), grammar);
        Assert.Equal(timeout, grammar.BeginRegexes().First().MatchTimeout);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-2)]
    [InlineData(int.MaxValue)]
    public void HighlightOptions_InvalidMatchTimeout_ThrowsArgumentOutOfRangeException(long milliseconds)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new HighlightOptions { MatchTimeout = TimeSpan.FromMilliseconds(milliseconds) });
    }

    [Fact]
    public void Highlight_RegexTimeout_ReturnsPlainText()
    {
        // Catastrophic backtracking: without the timeout this match would never finish.
        var grammar = Compiler.Compile(new Mode { Contains = [new Mode { Scope = "string", Begin = @"^(\w|\w\w)+$" }] }, TimeSpan.FromMilliseconds(100));
        var input = new string('a', 64) + "!<";

        Assert.Equal(new string('a', 64) + "!&lt;", Tokenizer.Highlight(input, grammar, HighlightOptions.Default));
    }

    [Fact]
    public void Highlight_GrammarThatStopsMakingProgress_ReturnsPlainText()
    {
        // A zero-width begin that re-enters itself at the same position forever.
        var looping = new Mode { Scope = "keyword", Begin = "(?=a)" };
        looping.Contains = [looping];
        var grammar = Compiler.Compile(new Mode { Contains = [looping] });

        Assert.Equal("a&amp;b", Tokenizer.Highlight("a&b", grammar, HighlightOptions.Default));
    }

    // highlight.js tests `input.indexOf("</" + name, after) !== -1`: a prefix match, so <a> is closed by </abbr>.
    [Theory]
    [InlineData("<a> </abbr>", "a", 3, true)]
    [InlineData("<a> </a>", "a", 3, true)]
    [InlineData("</a> <a>", "a", 5, false)]
    [InlineData("<a> </b>", "a", 3, false)]
    [InlineData("<ab> </a>", "ab", 4, false)]
    [InlineData("<My.Tag> </My.Tag>", "My.Tag", 8, true)]
    public void ClosingTagIndex_MatchesHighlightJsPrefixSemantics(string input, string name, int position, bool expected)
    {
        Assert.Equal(expected, new BeginGuards.ClosingTagIndex(input).HasClosingTagAtOrAfter(name, position));
    }

    [Fact]
    public void ClosingTagIndex_NameLongerThanTheIndexedDepth_FallsBackToScanning()
    {
        var name = new string('a', 300);
        var input = "<" + name + "> </" + name + "b>";

        Assert.True(new BeginGuards.ClosingTagIndex(input).HasClosingTagAtOrAfter(name, name.Length + 2));
        Assert.False(new BeginGuards.ClosingTagIndex(input).HasClosingTagAtOrAfter(name + "c", name.Length + 2));
    }

    [Fact]
    public void Compile_BeginPartsWithNamedGroup_Throws()
    {
        Assert.Throws<InvalidOperationException>(() => Compiler.Compile(new Mode { Contains = [new Mode { BeginParts = ["(?<name>a)", "b"], BeginScope = new Dictionary<int, string> { [1] = "keyword" } }] }));
    }

    [Fact]
    public void Compile_ExcludeBeginWithReturnBegin_Throws()
    {
        Assert.Throws<InvalidOperationException>(() => Compiler.Compile(new Mode { Contains = [new Mode { Begin = "a", ExcludeBegin = true, ReturnBegin = true }] }));
    }

    [Fact]
    public void GetSupportedLanguages_CoversEveryGrammar()
    {
        var grammars = SyntaxHighlighter.GetSupportedLanguages().Select(LanguageRegistry.Get).Distinct(ReferenceEqualityComparer.Instance).Count();

        Assert.Equal(GetGrammarIdentifiers().Length, grammars);
        Assert.All(GetGrammarIdentifiers(), language => Assert.True(SyntaxHighlighter.IsSupported(language)));
    }
}

file static class CompiledModeExtensions
{
    public static IEnumerable<System.Text.RegularExpressions.Regex> BeginRegexes(this CompiledMode mode) => mode.Contains.Select(child => child.BeginRe).OfType<System.Text.RegularExpressions.Regex>();
}
