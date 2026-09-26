// Copyright (c) Alexandre Mutel. All rights reserved.
// This file is licensed under the BSD-Clause 2 license.
// See the license.txt file in the project root for more information.

using Meziantou.Framework.Markdown.Extensions.GenericAttributes;
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
    [InlineData("[click](http://ok){ONCLICK=\"alert(1)\" style=\"color:red\" data-x=1}", "<p><a href=\"http://ok\" style=\"color:red\" data-x=\"1\">click</a></p>\n")]
    [InlineData("[click](http://ok){href=javascript:alert(1) title=t}", "<p><a href=\"http://ok\" title=\"t\">click</a></p>\n")]
    [InlineData("{#id .cls srcdoc=x formaction=javascript:alert(1) xmlns:x=y}\nparagraph", "<p id=\"id\" class=\"cls\">paragraph</p>\n")]
    [InlineData("# Title {onmouseover=alert(1) lang=en}", "<h1 id=\"title\" lang=\"en\">Title</h1>\n")]
    [InlineData("```js {onclick=alert(1) data-lang=js}\ncode\n```", "<pre><code class=\"language-js\" data-lang=\"js\">code\n</code></pre>\n")]
    public void GenericAttributesRemoveUnsafeAttributes(string markdown, string expected)
    {
        var pipeline = new MarkdownPipelineBuilder().UseAdvancedExtensions().DisableHtml().Build();

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

    [Theory]
    [InlineData("onclick", false)]
    [InlineData("OnLoad", false)]
    [InlineData("href", false)]
    [InlineData("SRC", false)]
    [InlineData("xlink:href", false)]
    [InlineData("xmlns:svg", false)]
    [InlineData("style", true)]
    [InlineData("title", true)]
    [InlineData("data-src", true)]
    public void GenericAttributesIsSafeAttributeName(string name, bool expected)
    {
        Assert.Equal(expected, GenericAttributesExtension.IsSafeAttributeName(name));
    }
}