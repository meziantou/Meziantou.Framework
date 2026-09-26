// Copyright (c) Alexandre Mutel. All rights reserved.
// This file is licensed under the BSD-Clause 2 license.
// See the license.txt file in the project root for more information.

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
}