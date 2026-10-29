// Copyright (c) Alexandre Mutel. All rights reserved.
// This file is licensed under the BSD-Clause 2 license.
// See the license.txt file in the project root for more information.

using System.Globalization;

using Meziantou.Framework.Markdown.Extensions.GenericAttributes;
using Meziantou.Framework.Markdown.Helpers;
using Meziantou.Framework.Markdown.Renderers.Html;
using Meziantou.Framework.Markdown.Renderers.Roundtrip;
using Meziantou.Framework.Markdown.Syntax;
using Meziantou.Framework.Markdown.Syntax.Inlines;

namespace Meziantou.Framework.Markdown.Tests;

public class TestHtmlAttributes
{
    [Fact]
    public void TestAddClass()
    {
        var attributes = new HtmlAttributes();
        attributes.AddClass("test");
        Assert.NotNull(attributes.Classes);
        Assert.Equal(new List<string>() { "test" }, attributes.Classes);

        attributes.AddClass("test");
        Assert.HasCount(1, attributes.Classes);

        attributes.AddClass("test1");
        Assert.Equal(new List<string>() { "test", "test1" }, attributes.Classes);
    }

    [Fact]
    public void TestAddProperty()
    {
        var attributes = new HtmlAttributes();
        attributes.AddProperty("key1", "1");
        Assert.NotNull(attributes.Properties);
        Assert.Equal(new List<KeyValuePair<string, string?>>() { new KeyValuePair<string, string?>("key1", "1") }, attributes.Properties);

        attributes.AddPropertyIfNotExist("key1", "1");
        Assert.NotNull(attributes.Properties);
        Assert.Equal(new List<KeyValuePair<string, string?>>() { new KeyValuePair<string, string?>("key1", "1") }, attributes.Properties);

        attributes.AddPropertyIfNotExist("key2", "2");
        Assert.Equal(new List<KeyValuePair<string, string?>>() { new KeyValuePair<string, string?>("key1", "1"), new KeyValuePair<string, string?>("key2", "2") }, attributes.Properties);
    }

    [Fact]
    public void TestCopyTo()
    {
        var from = new HtmlAttributes();
        from.AddClass("test");
        from.AddProperty("key1", "1");

        var to = new HtmlAttributes();
        from.CopyTo(to);

        Assert.Same(from.Classes, to.Classes);
        Assert.Same(from.Properties, to.Properties);

        //          From: Classes      From: Properties     To: Classes     To: Properties
        // test1:        null                null              null             null
        from = new HtmlAttributes();
        to = new HtmlAttributes();
        from.CopyTo(to, false, false);
        Assert.Null(to.Classes);
        Assert.Null(to.Properties);

        // test2:      ["test"]            ["key1", "1"]       null             null
        from = new HtmlAttributes();
        to = new HtmlAttributes();
        from.AddClass("test");
        from.AddProperty("key1", "1");
        from.CopyTo(to, false, false);
        Assert.Equal(new List<string>() { "test" }, to.Classes);
        Assert.Equal(new List<KeyValuePair<string, string?>>() { new KeyValuePair<string, string?>("key1", "1")}, to.Properties);

        // test3:        null                null            ["test"]       ["key1", "1"]
        from = new HtmlAttributes();
        to = new HtmlAttributes();
        to.AddClass("test");
        to.AddProperty("key1", "1");
        from.CopyTo(to, false, false);
        Assert.Equal(new List<string>() { "test" }, to.Classes);
        Assert.Equal(new List<KeyValuePair<string, string?>>() { new KeyValuePair<string, string?>("key1", "1") }, to.Properties);

        // test4:      ["test1"]           ["key2", "2"]     ["test"]       ["key1", "1"]
        from = new HtmlAttributes();
        to = new HtmlAttributes();
        from.AddClass("test1");
        from.AddProperty("key2", "2");
        to.AddClass("test");
        to.AddProperty("key1", "1");
        from.CopyTo(to, false, false);
        Assert.Equal(new List<string>() { "test", "test1" }, to.Classes);
        Assert.Equal(new List<KeyValuePair<string, string?>>() { new KeyValuePair<string, string?>("key1", "1"), new KeyValuePair<string, string?>("key2", "2") }, to.Properties);
    }

    [Theory]
    [InlineData("![x](y.png){onerror=alert(1)}", "<p><img src=\"y.png\" alt=\"x\" /></p>\n")]
    [InlineData("[click](http://ok){ONCLICK=\"alert(1)\" style=\"color:red\" data-x=1 title=t}", "<p><a href=\"http://ok\" title=\"t\">click</a></p>\n")]
    [InlineData("hello {x-data x-init=\"alert(document.domain)\" hx-get=/delete hx-on:click=alert(1) data-bind=x v-html=x ng-click=x is=x lang=en}", "<p lang=\"en\">hello</p>\n")]
    [InlineData("[click](http://ok){href=javascript:alert(1) title=t}", "<p><a href=\"http://ok\" title=\"t\">click</a></p>\n")]
    [InlineData("{#id .cls srcdoc=x formaction=javascript:alert(1) xmlns:x=y}\nparagraph", "<p id=\"id\" class=\"cls\">paragraph</p>\n")]
    [InlineData("# Title {onmouseover=alert(1) lang=en}", "<h1 id=\"title\" lang=\"en\">Title</h1>\n")]
    [InlineData("```js {onclick=alert(1) data-lang=js title=t}\ncode\n```", "<pre><code class=\"language-js\" title=\"t\">code\n</code></pre>\n")]
    public void GenericAttributesRemoveUnsafeAttributes(string markdown, string expected)
    {
        var pipeline = new MarkdownPipelineBuilder().UseAdvancedExtensions().DisableHtml().Build();

        Assert.Equal(expected, MarkdownConverter.ToHtml(markdown, pipeline));
    }

    [Theory]
    [InlineData("# h {title=\"a&b<c>\"}", "<h1 title=\"a&amp;b&lt;c&gt;\">h</h1>\n")]
    [InlineData("# h {title='a\"b'}", "<h1 title=\"a&quot;b\">h</h1>\n")]
    [InlineData("[x](/u){title=\"a&b\"}", "<p><a href=\"/u\" title=\"a&amp;b\">x</a></p>\n")]
    [InlineData("# h {#a&b}", "<h1 id=\"a&amp;b\">h</h1>\n")]
    [InlineData("# h {.a&b .c<d}", "<h1 class=\"a&amp;b c&lt;d\">h</h1>\n")]
    [InlineData("```a\"b&c<d>\nx\n```", "<pre><code class=\"language-a&quot;b&amp;c&lt;d&gt;\">x\n</code></pre>\n")]
    [InlineData("```js {.a&b title=\"<t>\"}\nx\n```", "<pre><code class=\"a&amp;b language-js\" title=\"&lt;t&gt;\">x\n</code></pre>\n")]
    public void GenericAttributesAreEscaped(string markdown, string expected)
    {
        var pipeline = new MarkdownPipelineBuilder().UseGenericAttributes().Build();

        Assert.Equal(expected, MarkdownConverter.ToHtml(markdown, pipeline));
    }

    [Theory]
    [InlineData("# h {#x}", "<h1 id=\"x\">h</h1>\n")]
    [InlineData("# h {.c}", "<h1 class=\"c\">h</h1>\n")]
    [InlineData("# h {#x .y}", "<h1 id=\"x\" class=\"y\">h</h1>\n")]
    [InlineData("```js {#i .c}\ncode\n```", "<pre><code id=\"i\" class=\"c language-js\">code\n</code></pre>\n")]
    [InlineData("```{.c}\ncode\n```", "<pre><code class=\"c\">code\n</code></pre>\n")]
    [InlineData("{#x .y title=t}\ntext", "<p id=\"x\" class=\"y\" title=\"t\">text</p>\n")]
    [InlineData("*a*{.c}", "<p><em class=\"c\">a</em></p>\n")]
    [InlineData("[l](/u){#i}", "<p><a href=\"/u\" id=\"i\">l</a></p>\n")]
    [InlineData("# h {#}", "<h1>h {#}</h1>\n")]
    [InlineData("# h {.}", "<h1>h {.}</h1>\n")]
    [InlineData("*a*{.c .}", "<p><em>a</em>{.c .}</p>\n")]
    public void GenericAttributesWithOneCharacterIdsAndClasses(string markdown, string expected)
    {
        var pipeline = new MarkdownPipelineBuilder().UseGenericAttributes().Build();

        Assert.Equal(expected, MarkdownConverter.ToHtml(markdown, pipeline));
    }

    [Theory]
    [InlineData("text {#custom}", "<p id=\"custom\">text</p>\n")]
    [InlineData("text  {#custom}  \n\nnext", "<p id=\"custom\">text</p>\n<p>next</p>\n")]
    [InlineData("text\nmore {#custom}\n", "<p id=\"custom\">text\nmore</p>\n")]
    [InlineData("*a* {.c}", "<p class=\"c\"><em>a</em></p>\n")]
    [InlineData("*a {#i}", "<p id=\"i\">*a</p>\n")]
    [InlineData("- a\n\n  text {#custom}", "<ul>\n<li><p>a</p>\n<p id=\"custom\">text</p>\n</li>\n</ul>\n")]
    [InlineData("- text {#custom}", "<ul>\n<li>text</li>\n</ul>\n")]
    [InlineData("> text {#custom}", "<blockquote>\n<p id=\"custom\">text</p>\n</blockquote>\n")]
    [InlineData("# h {#custom}", "<h1 id=\"custom\">h</h1>\n")]
    [InlineData("a {#i} b", "<p id=\"i\">a  b</p>\n")]
    [InlineData("text {#i}\t{.c} ", "<p id=\"i\" class=\"c\">text</p>\n")]
    [InlineData("text\u00A0{#i}", "<p id=\"i\">text\u00A0</p>\n")]
    [InlineData("text {#i}\u00A0", "<p id=\"i\">text \u00A0</p>\n")]
    [InlineData("text\v {#i}", "<p id=\"i\">text</p>\n")]
    [InlineData("\u00A0 {#i}\n=", "<h1 id=\"i\">\u00A0</h1>\n")]
    public void GenericAttributesEndingTheTextDoNotLeaveWhitespace(string markdown, string expected)
    {
        var pipeline = new MarkdownPipelineBuilder().UseGenericAttributes().Build();

        Assert.Equal(expected, MarkdownConverter.ToHtml(markdown, pipeline));
    }

    [Fact]
    public void GenericAttributesFilterCanBeReplaced()
    {
        var pipeline = new MarkdownPipelineBuilder()
            .UseAdvancedExtensions()
            .UseGenericAttributes(name => name is not "style")
            .Build();

        Assert.Equal("<p><img src=\"y.png\" onload=\"f()\" alt=\"x\" /></p>\n", MarkdownConverter.ToHtml("![x](y.png){onload=f() style=a}", pipeline));
    }

    [Fact]
    public void GenericAttributesFilterCanExtendTheDefaultFilter()
    {
        var pipeline = new MarkdownPipelineBuilder()
            .UseAdvancedExtensions()
            .UseGenericAttributes(name => GenericAttributesExtension.IsSafeAttributeName(name) || name.StartsWith("data-", StringComparison.OrdinalIgnoreCase))
            .Build();

        Assert.Equal("<p><a href=\"u\" title=\"t\" data-x=\"1\">x</a></p>\n", MarkdownConverter.ToHtml("[x](u){title=t data-x=1 style=a}", pipeline));
    }

    [Theory]
    [InlineData("onclick", false)]
    [InlineData("OnLoad", false)]
    [InlineData("href", false)]
    [InlineData("SRC", false)]
    [InlineData("xlink:href", false)]
    [InlineData("xmlns:svg", false)]
    [InlineData("style", false)]
    [InlineData("data-src", false)]
    [InlineData("x-init", false)]
    [InlineData("hx-on:click", false)]
    [InlineData("name", false)]
    [InlineData("class", false)]
    [InlineData("ariaLabel", false)]
    [InlineData("title", true)]
    [InlineData("LANG", true)]
    [InlineData("aria-label", true)]
    [InlineData("ARIA-HIDDEN", true)]
    [InlineData("role", true)]
    [InlineData("alt", true)]
    [InlineData("colspan", true)]
    [InlineData("start", true)]
    [InlineData("tabindex", true)]
    [InlineData("target", true)]
    [InlineData("datetime", true)]
    public void GenericAttributesIsSafeAttributeName(string name, bool expected)
    {
        Assert.Equal(expected, GenericAttributesExtension.IsSafeAttributeName(name));
    }

    // Timed: tests running at the same time would slow it down and make the time budget flaky
    [Theory(DisableParallelization = true)]
    [InlineData("{#a", "")]
    [InlineData("{.a", "")]
    [InlineData("={a", "")]
    [InlineData("x{#ab", "")]
    [InlineData("{#a", " !}")]
    [InlineData("{.a", " !}")]
    [InlineData("={a", " !}")]
    [InlineData("{a={a", " !}")]
    public void GenericAttributesWithoutValidClosingBraceAreParsedInLinearTime(string item, string suffix)
    {
        // Each '{' used to scan, and copy, the rest of the paragraph, even when a '}' follows
        var markdown = string.Concat(Enumerable.Repeat(item, 160 * 1024 / item.Length)) + suffix;
        var pipeline = new MarkdownPipelineBuilder().UseAdvancedExtensions().Build();

        var stopwatch = ThreadCpuStopwatch.StartNew();
        var html = MarkdownConverter.ToHtml(markdown, pipeline);
        stopwatch.Stop();

        Assert.Equal("<p>" + markdown + "</p>\n", html);
        Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(10), $"Parsing took {stopwatch.Elapsed}");
    }

    // Timed: tests running at the same time would slow it down and make the time budget flaky
    [Theory(DisableParallelization = true)]
    [InlineData(false)]
    [InlineData(true)]
    public void ManyPropertiesAttachedToABlockAreMergedInLinearTime(bool inOneBlock)
    {
        // Each property searched all the properties of the object it is attached to
        const int Count = 300_000;
        var names = Enumerable.Range(0, Count).Select(i => "aria-k" + i.ToString(CultureInfo.InvariantCulture));
        var markdown = inOneBlock
            ? "a {aria-x=1} {" + string.Join(' ', names.Select(name => name + "=x")) + "}\n"
            : "a" + string.Concat(names.Select(name => "{" + name + "=x}")) + "{aria-k0=y}\n";
        var pipeline = new MarkdownPipelineBuilder().UseAdvancedExtensions().Build();

        var stopwatch = ThreadCpuStopwatch.StartNew();
        var document = MarkdownConverter.Parse(markdown, pipeline);
        stopwatch.Stop();

        var properties = document[0].GetAttributes().Properties!;
        Assert.HasCount(inOneBlock ? Count + 1 : Count, properties);
        Assert.Equal(new KeyValuePair<string, string?>("aria-k0", "x"), properties[inOneBlock ? 1 : 0]);
        Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(10), $"Parsing took {stopwatch.Elapsed}");
    }

    [Theory]
    [InlineData("a {aria-a=1 aria-b=2} {aria-b=3 aria-c=4 aria-c=5 aria-d=6 aria-e=7 aria-f=8 aria-g=9 aria-a=10}", "<p aria-a=\"1\" aria-b=\"2\" aria-c=\"4\" aria-d=\"6\" aria-e=\"7\" aria-f=\"8\" aria-g=\"9\">a</p>\n")]
    [InlineData("a {aria-a=1 aria-a=2} {aria-b=3 aria-b=4 aria-c=5 aria-d=6 aria-e=7 aria-f=8 aria-g=9 aria-h=10} {aria-h=11 aria-i=12}", "<p aria-a=\"1\" aria-a=\"2\" aria-b=\"3\" aria-c=\"5\" aria-d=\"6\" aria-e=\"7\" aria-f=\"8\" aria-g=\"9\" aria-h=\"10\" aria-i=\"12\">a</p>\n")]
    [InlineData("*a*{aria-a=1 aria-b=2 aria-c=3 aria-d=4 aria-e=5} b {aria-a=6 aria-b=7 aria-c=8 aria-d=9 aria-e=10} *c*{aria-a=11 aria-f=12 aria-g=13 aria-h=14 aria-i=15} {aria-a=16 aria-j=17}", "<p aria-a=\"6\" aria-b=\"7\" aria-c=\"8\" aria-d=\"9\" aria-e=\"10\" aria-j=\"17\"><em aria-a=\"1\" aria-b=\"2\" aria-c=\"3\" aria-d=\"4\" aria-e=\"5\">a</em> b  <em aria-a=\"11\" aria-f=\"12\" aria-g=\"13\" aria-h=\"14\" aria-i=\"15\">c</em></p>\n")]
    public void MergedPropertiesKeepTheFirstValueOfEachName(string markdown, string expected)
    {
        var pipeline = new MarkdownPipelineBuilder().UseGenericAttributes().Build();

        Assert.Equal(expected, MarkdownConverter.ToHtml(markdown, pipeline));
    }

    [Theory]
    [InlineData("{#a{#a{#a", "<p>{#a{#a{#a</p>\n")]
    [InlineData("={a={a={a", "<p>={a={a={a</p>\n")]
    [InlineData("{#ab{#ab{#ab !}", "<p>{#ab{#ab{#ab !}</p>\n")]
    [InlineData("={a={a={a !}", "<p>={a={a={a !}</p>\n")]
    [InlineData("{#a{#a{#ab}", "<p id=\"a{#a{#ab\"></p>\n")]
    [InlineData("text {#a {#ab}", "<p id=\"ab\">text {#a</p>\n")]
    [InlineData("{#a{#a{#a{#a{#a{#a{#a{#a{#a{#a{#a{#a{#a{#a{#a{#a{#a{#a{#a{#a {.cls}", "<p class=\"cls\">{#a{#a{#a{#a{#a{#a{#a{#a{#a{#a{#a{#a{#a{#a{#a{#a{#a{#a{#a{#a</p>\n")]
    [InlineData("={a={a={a={a={a={a={a={a={a={a={a={a={a={a={a={a={a={a={a={a *b*{title=t lang=fr}", "<p>={a={a={a={a={a={a={a={a={a={a={a={a={a={a={a={a={a={a={a={a <em title=\"t\" lang=\"fr\">b</em></p>\n")]
    [InlineData("{#a{#a{#a\n{#ab}", "<p>{#a{#a{#a</p>\n")]
    [InlineData("# t {#a{#a{#a", "<h1 id=\"t-aaa\">t {#a{#a{#a</h1>\n")]
    [InlineData("# t {#ab}{#ab", "<h1 id=\"ab\">t</h1>\n")]
    [InlineData("[l](u){#a{#a{#a{title=\"x}", "<p><a href=\"u\" id=\"a{#a{#a{title=&quot;x\">l</a></p>\n")]
    [InlineData("[l](u){title='a{#a{#a' lang=fr}", "<p><a href=\"u\" title=\"a{#a{#a\" lang=\"fr\">l</a></p>\n")]
    [InlineData("```js {#a{#a{#a", "<pre><code class=\"language-js\"></code></pre>\n")]
    public void GenericAttributesWithoutValidClosingBrace(string markdown, string expected)
    {
        var pipeline = new MarkdownPipelineBuilder().UseAdvancedExtensions().Build();

        Assert.Equal(expected, MarkdownConverter.ToHtml(markdown, pipeline));
    }

    [Fact]
    public void GenericAttributesScanOutcomesMatchTryParse()
    {
        // The outcomes computed in linear time must follow the steps of TryParse exactly
        const string Alphabet = "{{}}##..=='\" \t\n\u00A0\0aZ1_:-!";
        var state = 42UL;
        int Next(int maxValue)
        {
            // Deterministic pseudo-random numbers
            state = (state * 6364136223846793005UL) + 1442695040888963407UL;
            return (int)((state >> 33) % (ulong)maxValue);
        }

        for (var i = 0; i < 5000; i++)
        {
            var chars = new char[1 + Next(40)];
            for (var j = 0; j < chars.Length; j++)
            {
                chars[j] = Alphabet[Next(Alphabet.Length)];
            }

            var text = new string(chars);
            var end = Next(text.Length);
            var start = Next(end + 2);
            var outcomes = new byte[end + 2 - start];
            GenericAttributesParser.ComputeScanOutcomes(text, start, end, outcomes);
            for (var position = Math.Max(0, start - 1); position <= end; position++)
            {
                var slice = new StringSlice(text, position, end);
                var expected = GenericAttributesParser.TryParse(ref slice, out _);
                var actual = (position == 0 || text[position - 1] != '{') && GenericAttributesParser.IsValidScanOutcome(outcomes[position + 1 - start]);
                Assert.Equal(expected, actual, $"Text: '{text}', start: {start}, end: {end}, position: {position}");
            }
        }
    }

    [Theory]
    [InlineData("# H{#id}\n")]
    [InlineData("# H # {#id}  \n")]
    [InlineData("## H {.a} more\n")]
    [InlineData("Setext{#id}\n---\n")]
    [InlineData("{#id .c}\n~~~\ncode\n~~~\n")]
    [InlineData("  {#id}  \r\n\r\n~~~\ncode\n~~~\n")]
    [InlineData("{.a}{.b}\n# H\n")]
    [InlineData("{.a}\n{.b}\n# H\n")]
    [InlineData("``` js {#id} x\ncode\n```\n")]
    [InlineData("```js{#id}\ncode\n```\n")]
    [InlineData("text {#p aria-busy}\n")]
    [InlineData("a {.b}{.c} d\n")]
    [InlineData("{.a} b\n")]
    [InlineData("[l](u){title=1} ![i](j){width=10}\n")]
    [InlineData("*e*{.x}\n")]
    [InlineData("a{.b}\r\nc\r\n")]
    [InlineData("> a {.b}\n> c\n")]
    [InlineData("- a\n  {.b}\n  c\n")]
    public void GenericAttributesRoundtrip(string markdown)
    {
        TestRoundtrip.RoundTrip(markdown, new MarkdownPipelineBuilder().UseGenericAttributes());
    }

    [Theory]
    [InlineData("{.a}{.b}\n# H")]
    [InlineData("{.a}\n{.b}\n# H")]
    [InlineData("a {.b}{.c} d")]
    [InlineData("{.a} b")]
    [InlineData("[l](u){.a}{.b}")]
    [InlineData("{#id .c}\n~~~\ncode\n~~~")]
    [InlineData("``` js {#id} x\ncode\n```")]
    public void GenericAttributesAreAttachedToTheSameObjectsWithTrivia(string markdown)
    {
        var html = MarkdownConverter.ToHtml(markdown, new MarkdownPipelineBuilder().UseGenericAttributes().Build());
        var htmlWithTrivia = MarkdownConverter.ToHtml(markdown, new MarkdownPipelineBuilder().UseGenericAttributes().EnableTrackTrivia().Build());

        Assert.Equal(html, htmlWithTrivia);
    }

    [Theory]
    [InlineData("# H\n", "x", null, "# H {#x}\n", "<h1 id=\"x\">H</h1>\n")]
    [InlineData("# H {#a}\n", "b", null, "# H {#b}\n", "<h1 id=\"b\">H</h1>\n")]
    [InlineData("# H{#a} more  \n", "b", null, "# H{#b} more  \n", "<h1 id=\"b\">H</h1>\n")]
    [InlineData("# H # {#a}  \n", "b", null, "# H # {#b}  \n", "<h1 id=\"b\">H</h1>\n")]
    [InlineData("# H #  \r\n", null, "c", "# H # {.c}  \r\n", "<h1 class=\"c\">H</h1>\n")]
    [InlineData("#\n", "x", null, "# {#x}\n", "<h1 id=\"x\"></h1>\n")]
    [InlineData("## *H*\n", "x", null, "## *H* {#x}\n", "<h2 id=\"x\"><em>H</em></h2>\n")]
    [InlineData("# a { b {#a}\n", "b", null, "# a { b {#b}\n", "<h1 id=\"b\">a { b</h1>\n")]
    [InlineData("# H {.a}\n", null, "b", "# H {.a .b}\n", "<h1 class=\"a b\">H</h1>\n")]
    [InlineData("{.a}\n# H\n", null, "b", "{.a .b}\n# H\n", "<h1 class=\"a b\">H</h1>\n")]
    [InlineData("p\n\n{.a}\n# H\n", null, "b", "p\n\n{.a .b}\n# H\n", "<p>p</p>\n<h1 class=\"a b\">H</h1>\n")]
    [InlineData("Setext\n===\n", "x", null, "Setext {#x}\n===\n", "<h1 id=\"x\">Setext</h1>\n")]
    [InlineData("Setext{#a}\n---\n", "b", null, "Setext{#b}\n---\n", "<h2 id=\"b\">Setext</h2>\n")]
    [InlineData("a {#a}\nb\n---\n", "b", null, "a {#b}\nb\n---\n", "<h2 id=\"b\">a b</h2>\n")]
    [InlineData("> # H\n> p\n", "x", null, "> # H {#x}\n> p\n", "<blockquote>\n<h1 id=\"x\">H</h1>\n<p>p</p>\n</blockquote>\n")]
    [InlineData("- # H\n", "x", null, "- # H {#x}\n", "<ul>\n<li><h1 id=\"x\">H</h1>\n</li>\n</ul>\n")]
    public void GenericAttributesRoundtripChangedHeadingAttributes(string markdown, string? id, string? cssClass, string expected, string expectedHtml)
    {
        var result = RoundtripWithChangedHeadingAttributes(markdown, new MarkdownPipelineBuilder().UseGenericAttributes(), attributes =>
        {
            if (id is not null)
            {
                attributes.Id = id;
            }

            if (cssClass is not null)
            {
                attributes.AddClass(cssClass);
            }
        });

        Assert.Equal(expected, result);
        Assert.Equal(expectedHtml, MarkdownConverter.ToHtml(result, new MarkdownPipelineBuilder().UseGenericAttributes().Build()));
    }

    [Theory]
    [InlineData("# H {#a}\n", "# H {}\n")]
    [InlineData("{#a}\n# H\n", "{}\n# H\n")]
    [InlineData("Setext {#a}\n===\n", "Setext {}\n===\n")]
    [InlineData("{#a}\n===\n", "{}\n===\n")]
    [InlineData("> {#a}\n> # H\n", "> {}\n> # H\n")]
    public void GenericAttributesRoundtripRemovedHeadingAttributes(string markdown, string expected)
    {
        var result = RoundtripWithChangedHeadingAttributes(markdown, new MarkdownPipelineBuilder().UseGenericAttributes(), attributes => attributes.Id = null);

        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData("title", "a b", "# H {title=\"a b\"}\n")]
    [InlineData("title", "a\"b c", "# H {title='a\"b c'}\n")]
    [InlineData("title", "", "# H {title=\"\"}\n")]
    [InlineData("title", "}", "# H {title=\"}\"}\n")]
    [InlineData("lang", "en", "# H {lang=en}\n")]
    [InlineData("hidden", null, "# H {hidden}\n")]
    public void GenericAttributesRoundtripChangedHeadingProperties(string name, string? value, string expected)
    {
        var result = RoundtripWithChangedHeadingAttributes("# H\n", new MarkdownPipelineBuilder().UseGenericAttributes(), attributes => attributes.AddPropertyIfNotExist(name, value));

        Assert.Equal(expected, result);
        var expectedValue = "=\"" + System.Net.WebUtility.HtmlEncode(value) + "\"";
        Assert.Equal("<h1 " + name + expectedValue + ">H</h1>\n", MarkdownConverter.ToHtml(result, new MarkdownPipelineBuilder().UseGenericAttributes().Build()));
    }

    [Theory]
    [InlineData("a b")]
    [InlineData("a}")]
    [InlineData("")]
    public void GenericAttributesRoundtripThrowsForIdentifiersThatCannotBeWritten(string id)
    {
        Assert.Throws<InvalidOperationException>(() => RoundtripWithChangedHeadingAttributes("# H\n", new MarkdownPipelineBuilder().UseGenericAttributes(), attributes => attributes.Id = id));
    }

    [Theory]
    [InlineData("on click", "x")]
    [InlineData("title", "a' \"b")]
    [InlineData("title", "a\nb")]
    public void GenericAttributesRoundtripThrowsForPropertiesThatCannotBeWritten(string name, string value)
    {
        Assert.Throws<InvalidOperationException>(() => RoundtripWithChangedHeadingAttributes("# H\n", new MarkdownPipelineBuilder().UseGenericAttributes(), attributes => attributes.AddProperty(name, value)));
    }

    [Theory]
    [InlineData("# a {b=\n")]
    [InlineData("# a {b= \n")]
    [InlineData("# x {.c b=\n")]
    [InlineData("a {b=\n===\n")]
    [InlineData("For\n*\n====\n")]
    [InlineData("For\n1)\n====\n")]
    [InlineData("For\nb.\n====\n")]
    [InlineData("[foo]:\n===\n")]
    [InlineData("[foo\n]:\n===\n")]
    [InlineData("a\n| b\n---\n")]
    [InlineData("a\n: b\n---\n")]
    [InlineData("[foo]: /url{#a .b}\n===\n")]
    [InlineData("a `b\n---\n", "`")]
    [InlineData("a <b c=\"\n---\n", "\">")]
    [InlineData("# a{ `b\n", "`")]
    public void GenericAttributesRoundtripThrowsWhenTheHeadingTextWouldReadTheAttributes(string markdown, string title = "a b")
    {
        // The text "{b=" would read the attributes as its value, the last line of a setext heading would start a block, and a code
        // span or raw HTML of the text would end in them
        var exception = Assert.Throws<InvalidOperationException>(() => RoundtripWithChangedHeadingAttributes(markdown, new MarkdownPipelineBuilder().UseGenericAttributes(), attributes =>
        {
            attributes.Id = "x";
            attributes.AddProperty("title", title);
        }));
        Assert.StartsWith("The attributes of the HeadingBlock cannot be written: ", exception.Message);
    }

    [Theory]
    [InlineData("*a*{.c}\n===\n", "*a*{.c} {#x}\n===\n", "<h1 id=\"x\"><em class=\"c\">a</em></h1>\n")]
    [InlineData("**{*\n===\n", "**{* {#x}\n===\n", "<h1 id=\"x\">*<em>{</em></h1>\n")]
    [InlineData("a { b\n===\n", "a { b {#x}\n===\n", "<h1 id=\"x\">a { b</h1>\n")]
    [InlineData("*a* b\n===\n", "*a* b {#x}\n===\n", "<h1 id=\"x\"><em>a</em> b</h1>\n")]
    [InlineData("   a\n===\n", "   a {#x}\n===\n", "<h1 id=\"x\">a</h1>\n")]
    [InlineData("> {.b}\n> # H\n", "> {#x .b}\n> # H\n", "<blockquote>\n<h1 id=\"x\" class=\"b\">H</h1>\n</blockquote>\n")]
    [InlineData("> a\n> b\n> ---\n", "> a\n> b {#x}\n> ---\n", "<blockquote>\n<h2 id=\"x\">a\nb</h2>\n</blockquote>\n")]
    [InlineData("# a { b\n", "# a { b {#x}\n", "<h1 id=\"x\">a { b</h1>\n")]
    [InlineData("# a {b='\n", "# a {b=' {#x}\n", "<h1 id=\"x\">a {b='</h1>\n")]
    [InlineData("## # #\n", "## # # {#x}\n", "<h2 id=\"x\">#</h2>\n")]
    [InlineData("# {a #\n", "# {a {#x} #\n", "<h1 id=\"x\">{a</h1>\n")]
    [InlineData("# a {b=\"\n", "# a {b=\" {#x}\n", "<h1 id=\"x\">a {b=&quot;</h1>\n")]
    [InlineData("a {b=\nc\n===\n", "a {b=\nc {#x}\n===\n", "<h1 id=\"x\">a {b=\nc</h1>\n")]
    public void GenericAttributesRoundtripWritesTheAttributesAfterATextWithBraces(string markdown, string expected, string expectedHtml)
    {
        var result = RoundtripWithChangedHeadingAttributes(markdown, new MarkdownPipelineBuilder().UseGenericAttributes(), attributes => attributes.Id = "x");

        Assert.Equal(expected, result);
        Assert.Equal(expectedHtml, MarkdownConverter.ToHtml(result, new MarkdownPipelineBuilder().UseGenericAttributes().Build()));
    }

    [Fact]
    public void GenericAttributesRoundtripDoesNotWriteUnchangedAttributesAddedByTheParsing()
    {
        // The identifiers generated from the content are added again when the text is parsed
        var pipeline = new MarkdownPipelineBuilder().UseAutoIdentifiers().UseGenericAttributes();

        Assert.Equal("# Hello {.c}\n", RoundtripWithChangedHeadingAttributes("# Hello\n", pipeline, attributes => attributes.AddClass("c")));
        Assert.Equal("# Hello {#a .c}\n", RoundtripWithChangedHeadingAttributes("# Hello {#a}\n", pipeline, attributes => attributes.AddClass("c")));
        Assert.Equal("# Hello {#b}\n", RoundtripWithChangedHeadingAttributes("# Hello\n", pipeline, attributes => attributes.Id = "b"));
        Assert.Equal("# Hello\n", RoundtripWithChangedHeadingAttributes("# Hello\n", pipeline, _ => { }));
    }

    [Fact]
    public void GenericAttributesRoundtripSeesTheAttributesAddedByDocumentProcessedHandlers()
    {
        // The handler is added before the generic attributes extension registers its own
        var builder = new MarkdownPipelineBuilder();
        builder.DocumentProcessed += document =>
        {
            foreach (var heading in document.Descendants<HeadingBlock>())
            {
                heading.GetAttributes().AddClass("added");
            }
        };

        Assert.Equal("# H\n", RoundtripWithChangedHeadingAttributes("# H\n", builder.UseGenericAttributes(), _ => { }));
    }

    [Fact]
    public void RoundtripWithoutGenericAttributesDoesNotWriteHeadingAttributes()
    {
        // Without the extension, the text "{#x}" would be part of the content of the heading
        Assert.Equal("# H\n", RoundtripWithChangedHeadingAttributes("# H\n", new MarkdownPipelineBuilder(), attributes => attributes.Id = "x"));
    }

    [Theory]
    [InlineData("text\n", nameof(ParagraphBlock), "text {#x}\n", "<p id=\"x\">text</p>\n")]
    [InlineData("text {.a}\n", nameof(ParagraphBlock), "text {#x .a}\n", "<p id=\"x\" class=\"a\">text</p>\n")]
    [InlineData("a\nb\n", nameof(ParagraphBlock), "a\nb {#x}\n", "<p id=\"x\">a\nb</p>\n")]
    [InlineData("> a\n> b\n", nameof(ParagraphBlock), "> a\n> b {#x}\n", "<blockquote>\n<p id=\"x\">a\nb</p>\n</blockquote>\n")]
    [InlineData("```js\ncode\n```\n", nameof(FencedCodeBlock), "```js {#x}\ncode\n```\n", "<pre><code id=\"x\" class=\"language-js\">code\n</code></pre>\n")]
    [InlineData("```js {.a}\ncode\n```\n", nameof(FencedCodeBlock), "```js {#x .a}\ncode\n```\n", "<pre><code id=\"x\" class=\"a language-js\">code\n</code></pre>\n")]
    [InlineData("~~~\ncode\n~~~\n", nameof(FencedCodeBlock), "~~~ {#x}\ncode\n~~~\n", "<pre><code id=\"x\">code\n</code></pre>\n")]
    [InlineData("> q\n", nameof(QuoteBlock), "{#x}\n> q\n", "<blockquote id=\"x\">\n<p>q</p>\n</blockquote>\n")]
    [InlineData("p\n\n> q\n", nameof(QuoteBlock), "p\n\n{#x}\n> q\n", "<p>p</p>\n<blockquote id=\"x\">\n<p>q</p>\n</blockquote>\n")]
    [InlineData("{.a}\n> q\n", nameof(QuoteBlock), "{#x .a}\n> q\n", "<blockquote id=\"x\" class=\"a\">\n<p>q</p>\n</blockquote>\n")]
    [InlineData("- a\n- b\n", nameof(ListBlock), "{#x}\n- a\n- b\n", "<ul id=\"x\">\n<li>a</li>\n<li>b</li>\n</ul>\n")]
    [InlineData("1. a\n", nameof(ListBlock), "{#x}\n1. a\n", "<ol id=\"x\">\n<li>a</li>\n</ol>\n")]
    [InlineData("***\n", nameof(ThematicBreakBlock), "{#x}\n***\n", "<hr id=\"x\" />\n")]
    [InlineData("a [l](u) b\n", nameof(LinkInline), "a [l](u){#x} b\n", "<p>a <a href=\"u\" id=\"x\">l</a> b</p>\n")]
    [InlineData("a ![i](u) b\n", nameof(LinkInline), "a ![i](u){#x} b\n", "<p>a <img src=\"u\" id=\"x\" alt=\"i\" /> b</p>\n")]
    [InlineData("a [l](u)\n", nameof(LinkInline), "a [l](u){#x}\n", "<p>a <a href=\"u\" id=\"x\">l</a></p>\n")]
    [InlineData("a `c` b\n", nameof(CodeInline), "a `c`{#x} b\n", "<p>a <code id=\"x\">c</code> b</p>\n")]
    [InlineData("a <http://x.y> b\n", nameof(AutolinkInline), "a <http://x.y>{#x} b\n", "<p>a <a href=\"http://x.y\" id=\"x\">http://x.y</a> b</p>\n")]
    [InlineData("a [l](u){.c} b\n", nameof(LinkInline), "a [l](u){#x .c} b\n", "<p>a <a href=\"u\" id=\"x\" class=\"c\">l</a> b</p>\n")]
    [InlineData("a *e*{.c} b\n", nameof(EmphasisInline), "a *e*{#x .c} b\n", "<p>a <em id=\"x\" class=\"c\">e</em> b</p>\n")]
    [InlineData("Setext [l](u)\n===\n", nameof(LinkInline), "Setext [l](u){#x}\n===\n", "<h1>Setext <a href=\"u\" id=\"x\">l</a></h1>\n")]
    public void GenericAttributesRoundtripChangedAttributes(string markdown, string type, string expected, string expectedHtml)
    {
        var result = RoundtripWithChangedAttributes(markdown, new MarkdownPipelineBuilder().UseGenericAttributes(), type, attributes => attributes.Id = "x");

        Assert.Equal(expected, result);
        Assert.Equal(expectedHtml, MarkdownConverter.ToHtml(result, new MarkdownPipelineBuilder().UseGenericAttributes().Build()));
    }

    [Fact]
    public void GenericAttributesRoundtripChangedAttributesOfCustomContainer()
    {
        var result = RoundtripWithChangedAttributes(":::note\nx\n:::\n", new MarkdownPipelineBuilder().UseCustomContainers().UseGenericAttributes(), "CustomContainer", attributes => attributes.Id = "x");

        Assert.Equal(":::note {#x}\nx\n:::\n", result);
        Assert.Equal("<div id=\"x\" class=\"note\"><p>x</p>\n</div>\n", MarkdownConverter.ToHtml(result, new MarkdownPipelineBuilder().UseCustomContainers().UseGenericAttributes().Build()));
    }

    [Theory]
    [InlineData("{.a}\n> q\n", nameof(QuoteBlock), "{}\n> q\n")]
    [InlineData("{.a}\n{.b}\n> q\n", nameof(QuoteBlock), "{}\n{}\n> q\n")]
    [InlineData("a [l](u){.c} b\n", nameof(LinkInline), "a [l](u){} b\n")]
    [InlineData("a {.c}\n", nameof(ParagraphBlock), "a {}\n")]
    [InlineData("```js {.a}\ncode\n```\n", nameof(FencedCodeBlock), "```js {}\ncode\n```\n")]
    [InlineData("a {.c\n.b}\n", nameof(ParagraphBlock), "a {\n}\n")]
    public void GenericAttributesRoundtripRemovedAttributes(string markdown, string type, string expected)
    {
        var result = RoundtripWithChangedAttributes(markdown, new MarkdownPipelineBuilder().UseGenericAttributes(), type, attributes =>
        {
            attributes.Classes = null;
            attributes.Properties = null;
        });

        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData("- a\n", nameof(ListItemBlock), "attributes")]
    [InlineData("    code\n", nameof(CodeBlock), "attributes")]
    [InlineData("| a |\n|---|\n| b |\n", "Table", "pipetables+attributes")]
    [InlineData("| a |\n|---|\n| [l](u) |\n", nameof(LinkInline), "pipetables+attributes")]
    [InlineData("p\n> q\n", nameof(QuoteBlock), "attributes")]
    [InlineData("- a\n\n  > q\n", nameof(QuoteBlock), "attributes")]
    [InlineData("---\n", nameof(ThematicBreakBlock), "attributes")]
    [InlineData("<span>\n", nameof(HtmlBlock), "attributes")]
    [InlineData("2. a\n", nameof(ListBlock), "attributes")]
    [InlineData("-\n  a\n", nameof(ListBlock), "attributes")]
    [InlineData("- | --\n", nameof(ListBlock), "pipetables+attributes")]
    [InlineData("a [l](u)\nb\n", nameof(LinkInline), "attributes")]
    [InlineData("# a [l](u)\n", nameof(LinkInline), "attributes")]
    [InlineData("a *e* b\n", nameof(EmphasisInline), "attributes")]
    [InlineData("a www.x.y b\n", nameof(LinkInline), "autolinks+attributes")]
    [InlineData("a tel:1[l](u) b\n", nameof(LinkInline), "autolinks+attributes")]
    [InlineData("a | b\n", nameof(ParagraphBlock), "attributes")]
    [InlineData("a\n| b\n|---|\n", nameof(ParagraphBlock), "pipetables+attributes")]
    [InlineData("::: {#a}\nx\n:::\n", "CustomContainer", "attributes+customcontainers")]
    public void GenericAttributesRoundtripThrowsForAttributesThatCannotBeWritten(string markdown, string type, string extensions)
    {
        var exception = Assert.Throws<InvalidOperationException>(() => RoundtripWithChangedAttributes(markdown, new MarkdownPipelineBuilder().Configure(extensions), type, attributes => attributes.Id = "x"));
        Assert.StartsWith("The attributes of the " + type + " cannot be written: ", exception.Message);
    }

    private static string RoundtripWithChangedAttributes(string markdown, MarkdownPipelineBuilder pipelineBuilder, string type, Action<HtmlAttributes> change)
    {
        var pipeline = pipelineBuilder.EnableTrackTrivia().Build();
        var document = MarkdownConverter.Parse(markdown, pipeline);
        change(document.Descendants().First(obj => obj.GetType().Name == type).GetAttributes());

        var writer = new StringWriter();
        var renderer = new RoundtripRenderer(writer);
        pipeline.Setup(renderer);
        renderer.Write(document);
        return writer.ToString();
    }

    private static string RoundtripWithChangedHeadingAttributes(string markdown, MarkdownPipelineBuilder pipelineBuilder, Action<HtmlAttributes> change)
    {
        var pipeline = pipelineBuilder.EnableTrackTrivia().Build();
        var document = MarkdownConverter.Parse(markdown, pipeline);
        foreach (var heading in document.Descendants<HeadingBlock>())
        {
            change(heading.GetAttributes());
        }

        var writer = new StringWriter();
        var renderer = new RoundtripRenderer(writer);
        pipeline.Setup(renderer);
        renderer.Write(document);
        return writer.ToString();
    }

    [Fact]
    public void DocumentationExample_GenericAttributes()
    {
        #region MarkdownExtensions_UseGenericAttributes
        var pipeline = new MarkdownPipelineBuilder()
            .UseGenericAttributes()
            .Build();

        var html = MarkdownConverter.ToHtml("# Introduction {#intro .title}\n\nRead [the guide](guide.html){target=_blank}.", pipeline);
        Assert.Equal("<h1 id=\"intro\" class=\"title\">Introduction</h1>\n<p>Read <a href=\"guide.html\" target=\"_blank\">the guide</a>.</p>\n", html);
        #endregion
    }

    [Fact]
    public void DocumentationExample_AttributesFromCode()
    {
        #region HtmlAttributes_FromCode
        var pipeline = new MarkdownPipelineBuilder().Build();
        var document = MarkdownConverter.Parse("> Prefer the async methods.", pipeline);

        // Attributes can be set from code, with or without UseGenericAttributes
        var quote = document.Descendants<QuoteBlock>().Single();
        var attributes = quote.GetAttributes();
        attributes.AddClass("note");
        attributes.AddProperty("style", "border-left: 4px solid gray"); // Not filtered: the code is trusted

        var html = document.ToHtml(pipeline);
        Assert.Equal("<blockquote class=\"note\" style=\"border-left: 4px solid gray\">\n<p>Prefer the async methods.</p>\n</blockquote>\n", html);
        #endregion
    }
}
