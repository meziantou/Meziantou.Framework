using Meziantou.Framework.WebPlatform.Dom;

namespace Meziantou.Framework.WebPlatform.Tests.Html;

/// <summary>https://w3c.github.io/trusted-types/dist/spec/</summary>
public sealed class TrustedTypesTests
{
    private static Document CreateDocument(bool enforce)
    {
        var document = HtmlDocumentParser.ParseDocument("<!DOCTYPE html><div></div>");
        if (enforce)
        {
            document.TrustedTypesDirectives.Add(TrustedTypesDirectives.Parse("require-trusted-types-for 'script'"));
        }

        return document;
    }

    private static TrustedTypePolicy CreatePassThroughPolicy(Document document, string name = "passthrough") => document.TrustedTypes.CreatePolicy(name, new TrustedTypePolicyOptions
    {
        CreateHtml = (input, _) => input,
        CreateScript = (input, _) => input,
        CreateScriptUrl = (input, _) => input,
    });

    [Fact]
    public void NotEnforcedByDefault()
    {
        var document = CreateDocument(enforce: false);
        var div = document.QuerySelector("div")!;
        div.InnerHtml = "<b>a</b>";
        div.SetAttribute("onclick", "x()");
        Assert.Equal("<b>a</b>", div.InnerHtml);
        Assert.Equal("x()", div.GetAttribute("onclick"));
    }

    [Fact]
    public void Policy_CreatesTrustedTypes()
    {
        var document = CreateDocument(enforce: false);
        object?[]? receivedArguments = null;
        var policy = document.TrustedTypes.CreatePolicy("p", new TrustedTypePolicyOptions
        {
            CreateHtml = (input, arguments) =>
            {
                receivedArguments = arguments;
                return input.ToUpperInvariant();
            },
            CreateScriptUrl = (_, _) => null,
        });

        Assert.Equal("p", policy.Name);
        var html = policy.CreateHtml("<b>", 1, "a");
        Assert.Equal("<B>", html.ToString());
        Assert.Equal("<B>", html.ToJson());
        Assert.Equal(new object?[] { 1, "a" }, receivedArguments);

        // A callback returning null creates an empty value, a missing callback throws
        Assert.Equal("", policy.CreateScriptUrl("x").ToString());
        Assert.Throws<TypeErrorException>(() => policy.CreateScript("x"));

        Assert.True(document.TrustedTypes.IsHtml(html));
        Assert.False(document.TrustedTypes.IsHtml("<B>"));
        Assert.False(document.TrustedTypes.IsScript(html));
        Assert.Equal("", document.TrustedTypes.EmptyHtml.ToString());
        Assert.Equal("", document.TrustedTypes.EmptyScript.ToString());
    }

    [Fact]
    public void DefaultPolicy_IsUniqueAndReturnedByTheFactory()
    {
        var document = CreateDocument(enforce: false);
        Assert.Null(document.TrustedTypes.DefaultPolicy);
        var policy = document.TrustedTypes.CreatePolicy("default");
        Assert.Same(policy, document.TrustedTypes.DefaultPolicy);
        Assert.Throws<TypeErrorException>(() => document.TrustedTypes.CreatePolicy("default"));

        // Without a trusted-types directive, the other names can be used several times
        document.TrustedTypes.CreatePolicy("a");
        document.TrustedTypes.CreatePolicy("a");
    }

    [Fact]
    public void TrustedTypesDirective_RestrictsThePolicyNames()
    {
        var document = CreateDocument(enforce: false);
        document.TrustedTypesDirectives.Add(TrustedTypesDirectives.Parse("trusted-types a b"));
        document.TrustedTypes.CreatePolicy("a");
        Assert.Throws<TypeErrorException>(() => document.TrustedTypes.CreatePolicy("a"));
        Assert.Throws<TypeErrorException>(() => document.TrustedTypes.CreatePolicy("c"));
        document.TrustedTypes.CreatePolicy("b");

        var duplicates = CreateDocument(enforce: false);
        duplicates.TrustedTypesDirectives.Add(TrustedTypesDirectives.Parse("trusted-types * 'allow-duplicates'"));
        duplicates.TrustedTypes.CreatePolicy("a");
        duplicates.TrustedTypes.CreatePolicy("a");

        var none = CreateDocument(enforce: false);
        none.TrustedTypesDirectives.Add(TrustedTypesDirectives.Parse("trusted-types 'none'"));
        Assert.Throws<TypeErrorException>(() => none.TrustedTypes.CreatePolicy("a"));

        // Report-only policies do not block anything
        var reportOnly = CreateDocument(enforce: false);
        reportOnly.TrustedTypesDirectives.Add(TrustedTypesDirectives.Parse("trusted-types 'none'", reportOnly: true));
        reportOnly.TrustedTypes.CreatePolicy("a");
    }

    [Theory]
    [InlineData("require-trusted-types-for 'script'", true, null)]
    [InlineData("REQUIRE-TRUSTED-TYPES-FOR 'SCRIPT'; trusted-types a  b", true, "a b")]
    [InlineData("require-trusted-types-for 'other'; trusted-types", false, "")]
    [InlineData("script-src 'none'; trusted-types a; trusted-types b", false, "a")]
    public void Directives_Parse(string policy, bool requireTrustedTypesForScript, string? trustedTypes)
    {
        var directives = TrustedTypesDirectives.Parse(policy);
        Assert.Equal(requireTrustedTypesForScript, directives.RequireTrustedTypesForScript);
        Assert.Equal(trustedTypes, directives.TrustedTypes is null ? null : string.Join(' ', directives.TrustedTypes));
    }

    [Fact]
    public void Enforced_HtmlSinksRequireTrustedHtml()
    {
        var document = CreateDocument(enforce: true);
        var div = document.QuerySelector("div")!;
        Assert.Throws<TypeErrorException>(() => div.InnerHtml = "<b>a</b>");
        Assert.Throws<TypeErrorException>(() => div.InsertAdjacentHtml("beforeend", "<b>a</b>"));
        Assert.Throws<TypeErrorException>(() => div.SetHtmlUnsafe("<b>a</b>"));
        Assert.Equal("", div.InnerHtml);

        var policy = CreatePassThroughPolicy(document);
        div.SetInnerHtml(policy.CreateHtml("<b>a</b>"));
        Assert.Equal("<b>a</b>", div.InnerHtml);
        div.InsertAdjacentHtml("beforeend", policy.CreateHtml("<i>b</i>"));
        Assert.Equal("<b>a</b><i>b</i>", div.InnerHtml);

        // The sanitizing methods are not injection sinks
        div.SetHtml("<u>c</u>");
        Assert.Equal("<u>c</u>", div.InnerHtml);
    }

    [Fact]
    public void Enforced_DefaultPolicyConvertsStrings()
    {
        var document = CreateDocument(enforce: true);
        var calls = new List<string>();
        document.TrustedTypes.CreatePolicy("default", new TrustedTypePolicyOptions
        {
            CreateHtml = (input, arguments) =>
            {
                calls.Add($"{arguments[0]}|{arguments[1]}");
                return input.Contains("fail", StringComparison.Ordinal) ? null : input.Replace("a", "b", StringComparison.Ordinal);
            },
        });

        var div = document.QuerySelector("div")!;
        div.InnerHtml = "<i>a</i>";
        Assert.Equal("<i>b</i>", div.InnerHtml);
        Assert.Equal(["TrustedHTML|Element innerHTML"], calls);

        // The default policy rejects the value when it returns null
        Assert.Throws<TypeErrorException>(() => div.InnerHtml = "fail");
        Assert.Equal("<i>b</i>", div.InnerHtml);
    }

    [Fact]
    public void ParserOptions_CreatedByAPolicy()
    {
        var document = CreateDocument(enforce: false);
        SetHtmlUnsafeOptions? receivedOptions = null;
        object?[]? receivedArguments = null;
        var policy = document.TrustedTypes.CreatePolicy("p", new TrustedTypePolicyOptions
        {
            CreateParserOptions = (options, arguments) =>
            {
                receivedOptions = options;
                receivedArguments = arguments;
                return new SetHtmlUnsafeOptions { Sanitizer = new SanitizerConfig { RemoveElements = ["i"] } };
            },
        });

        var trustedOptions = policy.CreateParserOptions(new SetHtmlUnsafeOptions { RunScripts = true, Sanitizer = SanitizerPresets.Default }, "a");
        Assert.True(receivedOptions!.RunScripts);
        Assert.IsType<Sanitizer>(receivedOptions.Sanitizer?.Value);
        Assert.Equal(new object?[] { "a" }, receivedArguments);

        // The options received by the callback have a sanitizer only when the given options have one
        policy.CreateParserOptions();
        Assert.False(receivedOptions.RunScripts);
        Assert.Null(receivedOptions.Sanitizer);

        var div = document.QuerySelector("div")!;
        div.SetHtmlUnsafe("<b>a</b><i>b</i>", trustedOptions);
        Assert.Equal("<b>a</b>", div.InnerHtml);

        // A missing callback, or a callback returning null, throws
        Assert.Throws<TypeErrorException>(() => CreatePassThroughPolicy(document).CreateParserOptions());
        var nullPolicy = document.TrustedTypes.CreatePolicy("null", new TrustedTypePolicyOptions { CreateParserOptions = (_, _) => null });
        Assert.Throws<TypeErrorException>(() => nullPolicy.CreateParserOptions());
    }

    [Fact]
    public void Enforced_ParserOptionsSatisfyTheSinks()
    {
        var document = CreateDocument(enforce: true);
        var policy = document.TrustedTypes.CreatePolicy("p", new TrustedTypePolicyOptions
        {
            CreateParserOptions = (options, _) => options,
        });

        var div = document.QuerySelector("div")!;
        Assert.Throws<TypeErrorException>(() => div.SetHtmlUnsafe("<b>a</b>"));
        Assert.Throws<TypeErrorException>(() => div.AppendHtmlUnsafe("<b>a</b>"));
        Assert.Throws<TypeErrorException>(() => div.StreamAppendHtmlUnsafe());

        var options = policy.CreateParserOptions();
        div.SetHtmlUnsafe("<b>a</b>", options);
        div.AppendHtmlUnsafe("<i>b</i>", options);
        var writer = div.StreamAppendHtmlUnsafe(options);
        writer.Write("<u>c</u>");
        writer.Close();

        Assert.Equal("<b>a</b><i>b</i><u>c</u>", div.InnerHtml);

        // Options that need vetting (scripts, or a sanitizer that is not an empty configuration) are rejected with a TrustedHTML
        var html = CreatePassThroughPolicy(document).CreateHtml("<s>d</s>");
        div.SetHtmlUnsafe(html, new SetHtmlUnsafeOptions { Sanitizer = new SanitizerConfig() });
        Assert.Throws<TypeErrorException>(() => div.SetHtmlUnsafe(html, new SetHtmlUnsafeOptions { RunScripts = true }));
        Assert.Throws<TypeErrorException>(() => div.SetHtmlUnsafe(html, new SetHtmlUnsafeOptions { Sanitizer = SanitizerPresets.Default }));
        Assert.Equal("<s>d</s>", div.InnerHtml);
    }

    [Fact]
    public void Enforced_DefaultPolicyCreatesParserOptions()
    {
        var document = CreateDocument(enforce: true);
        var calls = new List<string>();
        document.TrustedTypes.CreatePolicy("default", new TrustedTypePolicyOptions
        {
            CreateParserOptions = (options, arguments) =>
            {
                calls.Add($"{arguments[0]}|{arguments[1]}|{options.RunScripts}");
                return new SetHtmlUnsafeOptions { Sanitizer = new SanitizerConfig { RemoveElements = ["i"] } };
            },
        });

        // The strings are compliant without createHTML, and sanitized by the options of the policy
        var div = document.QuerySelector("div")!;
        div.InnerHtml = "<b>a</b><i>b</i>";
        Assert.Equal("<b>a</b>", div.InnerHtml);
        div.SetHtmlUnsafe("<u>c</u><i>d</i>", new SetHtmlUnsafeOptions { RunScripts = true });
        Assert.Equal("<u>c</u>", div.InnerHtml);
        div.StreamAppendHtmlUnsafe().Close();
        var range = document.CreateRange();
        range.SelectNodeContents(div);
        var fragment = range.CreateContextualFragment("<s>e</s><i>f</i>");
        Assert.Equal("s", Assert.Single(fragment.ChildNodes).NodeName.ToLowerInvariant());
        Assert.Equal(
            [
                "TrustedHTMLParserOptions|Element innerHTML|False",
                "TrustedHTMLParserOptions|Element setHTMLUnsafe|True",
                "TrustedHTMLParserOptions|Element streamAppendHTMLUnsafe|False",
                "TrustedHTMLParserOptions|Range createContextualFragment|True",
            ],
            calls);

        // The parser options are not used in XML documents: the string requires createHTML
        var xml = document.Implementation.CreateDocument(null, "root", null);
        Assert.Throws<TypeErrorException>(() => xml.DocumentElement!.InnerHtml = "<a/>");
    }

    [Fact]
    public void Enforced_ReportOnlyAllowsStrings()
    {
        var document = CreateDocument(enforce: false);
        document.TrustedTypesDirectives.Add(TrustedTypesDirectives.Parse("require-trusted-types-for 'script'", reportOnly: true));
        var div = document.QuerySelector("div")!;
        div.InnerHtml = "<b>a</b>";
        Assert.Equal("<b>a</b>", div.InnerHtml);
    }

    [Fact]
    public void Enforced_Attributes()
    {
        var document = CreateDocument(enforce: true);
        var policy = CreatePassThroughPolicy(document);
        var div = document.QuerySelector("div")!;

        // Event handler content attributes require TrustedScript
        Assert.Throws<TypeErrorException>(() => div.SetAttribute("onclick", "x()"));
        Assert.Throws<TypeErrorException>(() => div.SetAttributeNS(null, "onclick", "x()"));
        Assert.Throws<TypeErrorException>(() => div.SetAttribute("onclick", policy.CreateHtml("x()")));
        Assert.Null(div.GetAttribute("onclick"));
        div.SetAttribute("onclick", policy.CreateScript("x()"));
        Assert.Equal("x()", div.GetAttribute("onclick"));

        // Setting the value of an existing attribute node applies Trusted Types
        Assert.Throws<TypeErrorException>(() => div.GetAttributeNode("onclick")!.Value = "y()");
        Assert.Equal("x()", div.GetAttribute("onclick"));

        // The other attributes accept strings
        div.SetAttribute("title", "t");
        div.SetAttribute("data-x", policy.CreateScriptUrl("u"));
        Assert.Equal("u", div.GetAttribute("data-x"));

        var script = (HtmlScriptElement)document.CreateElement("script");
        Assert.Throws<TypeErrorException>(() => script.SetAttribute("src", "a.js"));
        Assert.Throws<TypeErrorException>(() => script.Src = "a.js");
        script.SetSrc(policy.CreateScriptUrl("https://example.com/a.js"));
        Assert.Equal("https://example.com/a.js", script.Src);

        var attr = document.CreateAttribute("src");
        attr.Value = "b.js";
        Assert.Throws<TypeErrorException>(() => script.SetAttributeNode(attr));

        var iframe = (HtmlIFrameElement)document.CreateElement("iframe");
        Assert.Throws<TypeErrorException>(() => iframe.Srcdoc = "<b>");
        iframe.SetSrcdoc(policy.CreateHtml("<b>"));
        Assert.Equal("<b>", iframe.Srcdoc);
    }

    [Fact]
    public void Enforced_ScriptText()
    {
        var engine = new Scripting.RecordingScriptEngine();
        var prepared = engine.Scripts;
        using var context = new BrowsingContext(new BrowsingContextOptions { ScriptEngine = engine });
        var document = context.Navigate("https://example.com/", _ => "<!DOCTYPE html><script>parsed()</script><body>");
        document.TrustedTypesDirectives.Add(TrustedTypesDirectives.Parse("require-trusted-types-for 'script'"));
        var policy = CreatePassThroughPolicy(document);
        Assert.Equal("parsed()", Assert.Single(prepared).SourceText);

        var script = (HtmlScriptElement)document.CreateElement("script");
        Assert.Throws<TypeErrorException>(() => script.Text = "a()");
        Assert.Throws<TypeErrorException>(() => script.TextContent = "a()");
        Assert.Throws<TypeErrorException>(() => script.InnerText = "a()");

        // A script whose text was not set by a Trusted Types compliant sink does not run
        var untrusted = (HtmlScriptElement)document.CreateElement("script");
        untrusted.AppendChild(document.CreateTextNode("untrusted()"));
        document.Body!.AppendChild(untrusted);
        Assert.Single(prepared);

        script.SetText(policy.CreateScript("trusted()"));
        document.Body.AppendChild(script);
        Assert.Equal("trusted()", prepared[^1].SourceText);

        // The default policy is applied to the text of the script when it is prepared
        document.TrustedTypes.CreatePolicy("default", new TrustedTypePolicyOptions { CreateScript = (input, arguments) => $"{input} // {arguments[1]}" });
        var converted = (HtmlScriptElement)document.CreateElement("script");
        ((Node)converted).TextContent = "converted()";
        document.Body.AppendChild(converted);
        Assert.Equal("converted() // HTMLScriptElement text", prepared[^1].SourceText);
    }

    [Fact]
    public void GetAttributeTypeAndGetPropertyType()
    {
        var factory = CreateDocument(enforce: false).TrustedTypes;
        Assert.Equal("TrustedScriptURL", factory.GetAttributeType("SCRIPT", "SRC"));
        Assert.Equal("TrustedHTML", factory.GetAttributeType("iframe", "srcdoc"));
        Assert.Equal("TrustedScript", factory.GetAttributeType("div", "onclick"));
        Assert.Null(factory.GetAttributeType("div", "onclick", attrNs: "http://www.w3.org/1999/xlink"));
        Assert.Equal("TrustedScriptURL", factory.GetAttributeType("script", "href", "http://www.w3.org/2000/svg", "http://www.w3.org/1999/xlink"));
        Assert.Null(factory.GetAttributeType("div", "title"));

        Assert.Equal("TrustedHTML", factory.GetPropertyType("div", "innerHTML"));
        Assert.Equal("TrustedScript", factory.GetPropertyType("script", "textContent"));
        Assert.Equal("TrustedScriptURL", factory.GetPropertyType("script", "src"));
        Assert.Null(factory.GetPropertyType("div", "textContent"));
    }

    [Fact]
    public void DocumentsCreatedByADocument_UseItsGlobalObject()
    {
        var document = CreateDocument(enforce: true);
        var created = document.Implementation.CreateHtmlDocument("t");
        Assert.Same(document.TrustedTypes, created.TrustedTypes);
        Assert.Throws<TypeErrorException>(() => created.Body!.InnerHtml = "<b>");
    }

    [Fact]
    public void Window_TrustedTypes()
    {
        using var context = new BrowsingContext(new BrowsingContextOptions());
        var document = context.Navigate("https://example.com/", _ => "<body>");
        Assert.Same(context.Window.TrustedTypes, document.TrustedTypes);
        document.TrustedTypesDirectives.Add(TrustedTypesDirectives.Parse("require-trusted-types-for 'script'"));
        Assert.Throws<TypeErrorException>(() => document.Body!.InnerHtml = "<b>");
    }

    [Fact]
    public void ExecCommand_InsertHtml_IsASink()
    {
        var document = CreateDocument(enforce: true);
        var policy = CreatePassThroughPolicy(document);

        // The value is checked before the command is tested (the command is not enabled: there is no editable selection)
        Assert.Throws<TypeErrorException>(() => document.ExecCommand("insertHTML", showUI: false, "<b>a</b>"));
        Assert.Throws<TypeErrorException>(() => document.ExecCommand("INSERTHTML", showUI: false, "<b>a</b>"));
        Assert.False(document.ExecCommand("insertHTML", showUI: false, policy.CreateHtml("<b>a</b>")));

        // The values of the other commands are not sinks
        Assert.False(document.ExecCommand("insertText", showUI: false, "<b>a</b>"));

        string? sink = null;
        document.TrustedTypes.CreatePolicy("default", new TrustedTypePolicyOptions
        {
            CreateHtml = (input, arguments) =>
            {
                sink = (string?)arguments[1];
                return input;
            },
        });
        Assert.False(document.ExecCommand("insertHTML", showUI: false, "<b>a</b>"));
        Assert.Equal("Document execCommand", sink);
    }

    [Fact]
    public void SetTimeout_StringHandler_IsCheckedWhenTheTimerIsCreated()
    {
        using var context = new BrowsingContext();
        var document = context.Navigate("https://example.com/", _ => "<!DOCTYPE html>");
        document.TrustedTypesDirectives.Add(TrustedTypesDirectives.Parse("require-trusted-types-for 'script'"));
        var window = document.DefaultView!;

        Assert.Throws<TypeErrorException>(() => window.SetTimeout("1"));
        Assert.Throws<TypeErrorException>(() => window.SetInterval("1"));

        var policy = CreatePassThroughPolicy(document);
        window.ClearTimeout(window.SetTimeout(policy.CreateScript("1")));

        var sinks = new List<string?>();
        document.TrustedTypes.CreatePolicy("default", new TrustedTypePolicyOptions
        {
            CreateScript = (input, arguments) =>
            {
                sinks.Add((string?)arguments[1]);
                return input;
            },
        });
        window.ClearTimeout(window.SetTimeout("1"));
        window.ClearInterval(window.SetInterval("1"));
        Assert.Equal(["Window setTimeout", "Window setInterval"], sinks);
    }
}
