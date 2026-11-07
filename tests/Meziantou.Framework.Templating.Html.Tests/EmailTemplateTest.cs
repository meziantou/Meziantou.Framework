using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;

namespace Meziantou.Framework.Templating.Tests;

public class EmailTemplateTest
{
    [Fact]
    public void ReadmeExample_Basic()
    {
        #region HtmlEmailTemplate_Basic
        using var template = new HtmlEmailTemplate();
        template.Load("Hello {{# \"Meziantou\" }}!");

        var result = template.Run(out var metadata);
        Assert.Equal("Hello Meziantou!", result);
        #endregion
    }

    [Fact]
    public void ReadmeExample_Expression()
    {
        #region HtmlEmailTemplate_Expression
        using var template = new HtmlEmailTemplate();
        template.Arguments.Add(new TemplateArgument("userName", typeof(string)));
        template.Load("Hello {{# userName }}!");

        var result = template.Run(out _, new Dictionary<string, object?>(StringComparer.Ordinal) { ["userName"] = "John" });
        Assert.Equal("Hello John!", result);
        #endregion
    }

    [Fact]
    public void ReadmeExample_HtmlEncode()
    {
        #region HtmlEmailTemplate_HtmlEncode
        using var template = new HtmlEmailTemplate();
        template.Load("Hello {{#html \"<Meziantou>\" }}!");

        var result = template.Run(out _);
        Assert.Equal("Hello &lt;Meziantou&gt;!", result);
        #endregion
    }

    [Fact]
    public void ReadmeExample_AttributeEncode()
    {
        #region HtmlEmailTemplate_AttributeEncode
        using var template = new HtmlEmailTemplate();
        template.Load("Hello <a href=\"{{#attr \"Sample&Sample\"}}\">Meziantou</a>!");

        var result = template.Run(out _);
        Assert.Equal("Hello <a href=\"Sample&amp;Sample\">Meziantou</a>!", result);
        #endregion
    }

    [Fact]
    public void ReadmeExample_UrlEncode()
    {
        #region HtmlEmailTemplate_UrlEncode
        using var template = new HtmlEmailTemplate();
        template.Load("Hello <a href=\"http://www.localhost.com/{{#url \"Sample&Url\" }}\">Meziantou</a>!");

        var result = template.Run(out _);
        Assert.Equal("Hello <a href=\"http://www.localhost.com/Sample%26Url\">Meziantou</a>!", result);
        #endregion
    }

    [Fact]
    public void ReadmeExample_ContentIdentifier()
    {
        #region HtmlEmailTemplate_ContentIdentifier
        using var template = new HtmlEmailTemplate();
        template.Load("<img src=\"{{cid logo.png}}\" />");

        var result = template.Run(out var metadata);
        Assert.Equal("<img src=\"cid:logo.png\" />", result);
        Assert.NotNull(metadata);
        Assert.Equal("logo.png", Assert.Single(metadata.ContentIdentifiers));
        #endregion
    }

    [Fact]
    public void ReadmeExample_HtmlCodeBlock()
    {
        #region HtmlEmailTemplate_HtmlCodeBlock
        using var template = new HtmlEmailTemplate();
        template.Load("{{html for(int i = 0; i &lt; 3; i++) { }}{{#i}} {{ } }}");

        var result = template.Run(out _);
        Assert.Equal("0 1 2 ", result);
        #endregion
    }

    [Fact]
    public void ReadmeExample_Section()
    {
        #region HtmlEmailTemplate_Section
        using var template = new HtmlEmailTemplate();
        template.Load("Hello {{@begin_section title}}{{# \"Meziantou\" }}{{@end_section}}!");

        var result = template.Run(out var metadata);
        Assert.Equal("Hello Meziantou!", result);
        Assert.NotNull(metadata);
        Assert.Equal("Meziantou", metadata.Title);
        #endregion
    }

    [Fact]
    public void ReadmeExample_Metadata()
    {
        #region HtmlEmailTemplate_Metadata
        using var template = new HtmlEmailTemplate();
        template.Arguments.Add(new TemplateArgument("userName", typeof(string)));
        template.Load("""
            {{@begin_section title}}Welcome Email{{@end_section}}
            <html>
              <body>
                <img src="{{cid logo.png}}" />
                <h1>Hello {{#html userName}}!</h1>
              </body>
            </html>
            """);

        var result = template.Run(out var metadata,
            new Dictionary<string, object?>(StringComparer.Ordinal) { ["userName"] = "John" });

        Assert.NotNull(metadata);
        Assert.Equal("Welcome Email", metadata.Title);
        Assert.Equal("logo.png", Assert.Single(metadata.ContentIdentifiers));
        #endregion
    }

    [Fact]
    public void ReadmeExample_Parameters()
    {
        #region HtmlEmailTemplate_Parameters
        using var template = new HtmlEmailTemplate();
        template.Arguments.Add(new TemplateArgument("name", typeof(string)));
        template.Arguments.Add(new TemplateArgument("age", typeof(int)));
        template.Load("{{#html name}} is {{# age }}");

        // Using a dictionary
        var result1 = template.Run(out _,
            new Dictionary<string, object?>(StringComparer.Ordinal) { ["name"] = "John", ["age"] = 30 });
        Assert.Equal("John is 30", result1);

        // Using positional parameters
        var result2 = template.Run(out _, "John", 30);
        Assert.Equal("John is 30", result2);
        #endregion
    }

    [Fact]
    public void ReadmeExample_TypedOutput()
    {
        #region HtmlEmailTemplate_TypedOutput
        using var template = new HtmlEmailTemplate { OutputType = typeof(HtmlEmailOutput) };
        template.Arguments.Add(new TemplateArgument("userName", typeof(string)));
        template.Load("Hello {{#html userName}}!");

        var result = template.Run(out _, "<John>");
        Assert.Equal("Hello &lt;John&gt;!", result);
        #endregion
    }

    [Fact]
    public void DocumentationExample_UrlEncodeValue()
    {
        #region HtmlEmailTemplate_UrlEncodeValue
        using var template = new HtmlEmailTemplate();
        template.Arguments.Add(new TemplateArgument("query", typeof(string)));
        template.Load("<a href=\"https://example.com/search?q={{#url query}}\">Search</a>");

        var result = template.Run(out _, "fish & chips");
        Assert.Equal("<a href=\"https://example.com/search?q=fish%20%26%20chips\">Search</a>", result);
        #endregion
    }

    [Fact]
    public void DocumentationExample_NonAsciiCharacters()
    {
        #region HtmlEmailTemplate_NonAsciiCharacters
        using var template = new HtmlEmailTemplate();
        template.Arguments.Add(new TemplateArgument("name", typeof(string)));
        template.Load("<p>Hello {{#html name}}!</p>");

        var result = template.Run(out _, "Zoé");
        Assert.Equal("<p>Hello Zo&#xE9;!</p>", result);
        #endregion
    }

    [Fact]
    public void DocumentationExample_TitleIsPlainText()
    {
        #region HtmlEmailTemplate_TitleIsPlainText
        using var template = new HtmlEmailTemplate();
        template.Arguments.Add(new TemplateArgument("product", typeof(string)));
        template.Load("<title>{{@begin_section title}}Your {{#html product}} order{{@end_section}}</title>");

        var result = template.Run(out var metadata, "Fish & Chips");
        Assert.Equal("<title>Your Fish &amp; Chips order</title>", result);
        Assert.NotNull(metadata);
        Assert.Equal("Your Fish & Chips order", metadata.Title);
        #endregion
    }

    [Fact]
    public void DocumentationExample_UnclosedSection()
    {
        #region HtmlEmailTemplate_UnclosedSection
        using var template = new HtmlEmailTemplate();
        template.Load("<title>{{@begin_section title}}Welcome</title>");

        var exception = Assert.Throws<TemplateException>(() => template.Run(out _));
        Assert.Equal("The template is invalid: no end_section directive for 'title'.", exception.Message);
        #endregion
    }

    [Fact]
    public void HtmlEmailTemplate_Example()
    {
        #region HtmlEmailTemplate_Example
        using var template = new HtmlEmailTemplate();
        template.Load("<title>{{@begin_section title}}Welcome{{@end_section}}</title><h1>Hello {{#html userName}}!</h1>");
        template.Arguments.Add(new TemplateArgument("userName", typeof(string)));

        var result = template.Run(out var metadata, new Dictionary<string, object?> { ["userName"] = "John" });
        Assert.Equal("<title>Welcome</title><h1>Hello John!</h1>", result);
        Assert.NotNull(metadata);
        Assert.Equal("Welcome", metadata.Title);
        #endregion
    }

    [Fact]
    public void HtmlEmailOutput_Example()
    {
        #region HtmlEmailOutput_Example
        using var template = new HtmlEmailTemplate();
        using var writer = new StringWriter();
        var output = new HtmlEmailOutput(template, writer);

        output.WriteHtmlEncode("<b>text</b>");
        Assert.Equal("&lt;b&gt;text&lt;/b&gt;", writer.ToString());

        writer.GetStringBuilder().Clear();
        output.WriteHtmlAttributeEncode("a b");
        Assert.Equal("a&#x20;b", writer.ToString());

        writer.GetStringBuilder().Clear();
        output.WriteUrlEncode("value&param");
        Assert.Equal("value%26param", writer.ToString());

        writer.GetStringBuilder().Clear();
        output.WriteContentIdentifier("logo.png");
        Assert.Equal("cid:logo.png", writer.ToString());
        #endregion
    }

    [Fact]
    public void EmailTemplate_01()
    {
        // Arrange
        using var template = new HtmlEmailTemplate();
        template.Load("Hello {{# \"Meziantou\" }}!");

        // Act 
        var result = template.Run(out var metadata);
        Assert.Equal("Hello Meziantou!", result);
        Assert.NotNull(metadata);
        Assert.Null(metadata.Title);
    }

    [Fact]
    public void EmailTemplate_Section_01()
    {
        // Arrange
        using var template = new HtmlEmailTemplate();
        template.Load("Hello {{@begin_section title}}{{# \"Meziantou\" }}{{@end_section}}!");

        // Act 
        var result = template.Run(out var metadata);
        Assert.Equal("Hello Meziantou!", result);
        Assert.NotNull(metadata);
        Assert.Equal("Meziantou", metadata.Title);
    }

    [Fact]
    public void EmailTemplate_HtmlEncode_01()
    {
        // Arrange
        using var template = new HtmlEmailTemplate();
        template.Load("Hello {{#html \"<Meziantou>\" }}!");

        // Act 
        var result = template.Run(out _);
        Assert.Equal("Hello &lt;Meziantou&gt;!", result);
    }

    [Fact]
    public void EmailTemplate_UrlEncode_01()
    {
        // Arrange
        using var template = new HtmlEmailTemplate();
        template.Load("Hello <a href=\"http://www.localhost.com/{{#url \"Sample&Url\" }}\">Meziantou</a>!");

        // Act 
        var result = template.Run(out _);
        Assert.Equal("Hello <a href=\"http://www.localhost.com/Sample%26Url\">Meziantou</a>!", result);
    }

    [Fact]
    public void EmailTemplate_HtmlAttributeEncode_01()
    {
        // Arrange
        using var template = new HtmlEmailTemplate();
        template.Load("Hello <a href=\"{{#attr \"Sample&Sample\"}}\">Meziantou</a>!");

        // Act 
        var result = template.Run(out _);
        Assert.Equal("Hello <a href=\"Sample&amp;Sample\">Meziantou</a>!", result);
    }

    [Fact]
    public void EmailTemplate_HtmlAttributeEncode_EscapesCharactersThatEndAnUnquotedAttribute()
    {
        using var template = new HtmlEmailTemplate();
        template.Load("<a href={{#attr \"x onmouseover=alert(1)\" }}>");

        var result = template.Run(out _);
        Assert.Equal("<a href=x&#x20;onmouseover&#x3D;alert&#x28;1&#x29;>", result);
    }

    [Fact]
    public void EmailTemplate_HtmlAttributeEncode_EscapesEverythingOutsideAlphanumerics()
    {
        using var template = new HtmlEmailTemplate();
        template.Load("{{#attr \"a-z_0.9 \\t`'\\\"<>&\u00e9\" }}");

        var result = template.Run(out _);
        Assert.Equal("a&#x2D;z&#x5F;0&#x2E;9&#x20;&#x9;&#x60;&#x27;&quot;&lt;&gt;&amp;&#xE9;", result);
    }

    [Fact]
    public void EmailTemplate_HtmlCode_01()
    {
        // Arrange
        using var template = new HtmlEmailTemplate();
        template.Load("{{html for(int i = 0; i &lt; 3; i++) { }}{{#i}} {{ } }}");

        // Act 
        var result = template.Run(out _);
        Assert.Equal("0 1 2 ", result);
    }

    [Fact]
    public void EmailTemplate_Cid_01()
    {
        // Arrange
        using var template = new HtmlEmailTemplate();
        template.Load("<img src=\"{{cid test1.png}}\" /><img src=\"{{cid test2.png}}\" />");

        // Act 
        var result = template.Run(out var metadata);
        Assert.Equal("<img src=\"cid:test1.png\" /><img src=\"cid:test2.png\" />", result);
        Assert.NotNull(metadata);
        Assert.NotNull(metadata.ContentIdentifiers);
        Assert.Collection(metadata.ContentIdentifiers,
             item => Assert.Equal("test1.png", item),
             item => Assert.Equal("test2.png", item));
    }
    [Fact]
    public void EmailTemplate_Section_CapturesPlainText()
    {
        using var template = new HtmlEmailTemplate();
        template.Load("Hello {{@begin_section title}}Plain text{{@end_section}}!");

        var result = template.Run(out var metadata);
        Assert.Equal("Hello Plain text!", result);
        Assert.NotNull(metadata);
        Assert.Equal("Plain text", metadata.Title);
    }

    [Fact]
    public void EmailTemplate_Section_CapturesExpressionValue()
    {
        using var template = new HtmlEmailTemplate();
        template.Load("Hello {{@begin_section title}}{{= 42 }}{{@end_section}}!");

        var result = template.Run(out var metadata);
        Assert.Equal("Hello 42!", result);
        Assert.NotNull(metadata);
        Assert.Equal("42", metadata.Title);
    }

    [Fact]
    public void EmailTemplate_ExpressionBlock_WritesArrayValueWithoutTreatingItAsFormatArguments()
    {
        using var template = new HtmlEmailTemplate();
        template.Load("{{# new object[] { 1, 2 } }}");

        Assert.Equal(new object[] { 1, 2 }.ToString(), template.Run(out _));
    }

    [Fact]
    public void EmailTemplate_ExpressionBlock_WritesEmptyArrayValueWithoutThrowing()
    {
        using var template = new HtmlEmailTemplate();
        template.Load("{{# System.Array.Empty<object>() }}");

        Assert.Equal(System.Array.Empty<object>().ToString(), template.Run(out _));
    }

    [Fact]
    public void EmailTemplate_HtmlEncode_EncodesArrayValueWithoutTreatingItAsFormatArguments()
    {
        using var template = new HtmlEmailTemplate();
        template.Load("{{#html new object[] { 1, 2 } }}");

        Assert.Equal(new object[] { 1, 2 }.ToString(), template.Run(out _));
    }

    [Fact]
    public void EmailTemplate_UnclosedSection_Throws()
    {
        using var template = new HtmlEmailTemplate();
        template.Load("Hello {{@begin_section title}}Subject");

        var exception = Assert.Throws<TemplateException>(() => template.Run(out _));

        Assert.Contains("end_section", exception.Message, ignoreCase: false);
        Assert.Contains("'title'", exception.Message, ignoreCase: false);
    }

    [Fact]
    public void EmailTemplate_SeveralUnclosedSections_AreAllReported()
    {
        using var template = new HtmlEmailTemplate();
        template.Load("{{@begin_section title}}a{{@begin_section footer}}b");

        var exception = Assert.Throws<TemplateException>(() => template.Run(out _));

        Assert.Contains("'title'", exception.Message, ignoreCase: false);
        Assert.Contains("'footer'", exception.Message, ignoreCase: false);
    }

    [Fact]
    public void EmailTemplate_UnclosedSection_ThrowsFromInheritedRunOverloadsToo()
    {
        using var template = new HtmlEmailTemplate();
        template.Load("Hello {{@begin_section title}}Subject");

        Assert.Throws<TemplateException>(() => ((Template)template).Run());
    }

    [Fact]
    public void EmailTemplate_NestedSectionsWithTheSameName_KeepTheOutermostContent()
    {
        using var template = new HtmlEmailTemplate();
        template.Load("{{@begin_section title}}A{{@begin_section title}}B{{@end_section}}C{{@end_section}}");

        var result = template.Run(out var metadata);

        Assert.Equal("ABC", result);
        Assert.NotNull(metadata);
        Assert.Equal("ABC", metadata.Title);
    }
    [Fact]
    public void EmailTemplate_RunWithNamedParameters_MatchesArgumentsByName()
    {
        using var template = new HtmlEmailTemplate();
        template.Load("{{@begin_section title}}Welcome{{@end_section}}: {{#html FirstName}} {{#html LastName}}");
        template.Arguments.Add(new TemplateArgument("FirstName", typeof(string)));
        template.Arguments.Add(new TemplateArgument("LastName", typeof(string)));

        // Declared in the opposite order of the arguments, so a positional binding would swap them
        var result = template.Run(out var metadata, new Dictionary<string, object?>
        {
            ["LastName"] = "Barr<e>",
            ["FirstName"] = "G&rald",
        });

        Assert.Equal("Welcome: G&amp;rald Barr&lt;e&gt;", result);
        Assert.NotNull(metadata);
        Assert.Equal("Welcome", metadata.Title);
    }

    [Fact]
    public void EmailTemplate_RunWithNamedParameters_KeepsTheComparerOfTheDictionary()
    {
        using var template = new HtmlEmailTemplate();
        template.Load("{{#html FirstName}} {{#html LastName}}");
        template.Arguments.Add(new TemplateArgument("FirstName", typeof(string)));
        template.Arguments.Add(new TemplateArgument("LastName", typeof(string)));

        var parameters = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
        {
            ["firstname"] = "Gerald",
            ["lastname"] = "Barre",
        };

        Assert.Equal("Gerald Barre", template.Run(out _, parameters));
    }

    [Fact]
    public void EmailTemplate_SettingTheOutputType_CompilesEveryBlockWithoutDynamic()
    {
        using var template = new HtmlEmailTemplate { OutputType = typeof(HtmlEmailOutput) };
        template.Load("""
            {{@begin_section title}}Welcome{{@end_section}}
            <img src="{{cid logo.png}}" alt="{{#attr AltText}}">
            <a href="{{#url Url}}">{{#html Name}}</a>
            {{# 40 + 2 }}
            """);
        template.Arguments.Add(new TemplateArgument("AltText", typeof(string)));
        template.Arguments.Add(new TemplateArgument("Url", typeof(string)));
        template.Arguments.Add(new TemplateArgument("Name", typeof(string)));

        var result = template.Run(out var metadata, "Logo & co", "https://example.com/", "<Meziantou>");

        Assert.DoesNotContain("dynamic", template.SourceCode);
        Assert.Contains("""<img src="cid:logo.png" alt="Logo&#x20;&amp;&#x20;co">""", result);
        Assert.Contains("""&lt;Meziantou&gt;</a>""", result);
        Assert.Contains("42", result);
        Assert.NotNull(metadata);
        Assert.Equal("Welcome", metadata.Title);
        Assert.Equal(["logo.png"], metadata.ContentIdentifiers);
    }

    [Fact]
    public void EmailTemplate_Dispose_UnloadsTheCompiledAssembly_WhenTheOutputTypeIsSet()
    {
        var loadContext = BuildAndReleaseTemplate();

        // Unloading is not synchronous: it completes on a later collection, once the runtime has
        // finished walking everything that referenced the assembly.
        for (var i = 0; i < 20 && loadContext.IsAlive; i++)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
        }

        Assert.False(loadContext.IsAlive, "The compiled assembly was not unloaded");
    }

    // The template must not be reachable from the caller frame when the collection runs, so keep it
    // in a non-inlined method and only return a weak reference to the load context it created.
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference BuildAndReleaseTemplate()
    {
        using var template = new TemplateTrackingItsLoadContext { OutputType = typeof(HtmlEmailOutput) };
        template.Load("Hello {{#html Name}}!");
        template.Arguments.Add(new TemplateArgument("Name", typeof(string)));

        Assert.Equal("Hello &lt;Meziantou&gt;!", template.Run(out _, "<Meziantou>"));

        var loadContext = template.LoadContext!;
        Assert.True(loadContext.IsAlive, "The template was not loaded in its own load context");

        return loadContext;
    }

    private sealed class TemplateTrackingItsLoadContext : HtmlEmailTemplate
    {
        public WeakReference? LoadContext { get; private set; }

        protected override Assembly LoadAssembly(MemoryStream peStream, MemoryStream pdbStream)
        {
            var assembly = base.LoadAssembly(peStream, pdbStream);
            LoadContext = new WeakReference(AssemblyLoadContext.GetLoadContext(assembly));
            return assembly;
        }
    }
}