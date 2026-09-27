// Copyright (c) Alexandre Mutel. All rights reserved.
// This file is licensed under the BSD-Clause 2 license.
// See the license.txt file in the project root for more information.

using System.Diagnostics;

using Meziantou.Framework.Markdown.Extensions.GenericAttributes;
using Meziantou.Framework.Markdown.Helpers;
using Meziantou.Framework.Markdown.Renderers.Html;

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
    [InlineData("hello {x-data x-init=\"alert(document.domain)\" hx-get=/delete hx-on:click=alert(1) data-bind=x v-html=x ng-click=x is=x lang=en}", "<p lang=\"en\">hello </p>\n")]
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
    [InlineData("title", true)]
    [InlineData("LANG", true)]
    [InlineData("aria-label", true)]
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

        var stopwatch = Stopwatch.StartNew();
        var html = MarkdownConverter.ToHtml(markdown, pipeline);
        stopwatch.Stop();

        Assert.Equal("<p>" + markdown + "</p>\n", html);
        Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(10), $"Parsing took {stopwatch.Elapsed}");
    }

    [Theory]
    [InlineData("{#a{#a{#a", "<p>{#a{#a{#a</p>\n")]
    [InlineData("={a={a={a", "<p>={a={a={a</p>\n")]
    [InlineData("{#ab{#ab{#ab !}", "<p>{#ab{#ab{#ab !}</p>\n")]
    [InlineData("={a={a={a !}", "<p>={a={a={a !}</p>\n")]
    [InlineData("{#a{#a{#ab}", "<p id=\"a{#a{#ab\"></p>\n")]
    [InlineData("text {#a {#ab}", "<p id=\"ab\">text {#a </p>\n")]
    [InlineData("{#a{#a{#a{#a{#a{#a{#a{#a{#a{#a{#a{#a{#a{#a{#a{#a{#a{#a{#a{#a {.cls}", "<p class=\"cls\">{#a{#a{#a{#a{#a{#a{#a{#a{#a{#a{#a{#a{#a{#a{#a{#a{#a{#a{#a{#a </p>\n")]
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
        const string Alphabet = "{{}}##..=='\" \t\n \0aZ1_:-!";
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
}