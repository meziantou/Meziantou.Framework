namespace Meziantou.Framework.Templating.Tests;

public class TemplateTest
{
    [Fact]
    public void Template_TextOnly()
    {
        // Arrange
        var template = new Template();
        template.Load("Sample");
        template.OutputType = typeof(Output);

        // Act
        var result = template.Run();
        Assert.Equal("Sample", result);
    }

    [Fact]
    public void Template_CodeOnly()
    {
        // Arrange
        var template = new Template();
        template.Load("<% " + template.OutputParameterName + ".Write(\"Sample\"); %>");

        // Act
        var result = template.Run();
        Assert.Equal("Sample", result);
    }

    [Fact]
    public void Template_CodeEval()
    {
        // Arrange
        var template = new Template();
        template.Load("<%= \"Sample\" %>");

        // Act
        var result = template.Run();
        Assert.Equal("Sample", result);
    }

    [Fact]
    public void Template_CodeEval_VerbatimString()
    {
        var template = new Template();
        template.Load("<%= @\"Sample\" %>");

        var result = template.Run();

        Assert.Equal("Sample", result);
    }

    [Fact]
    public void Template_CodeEvalParameter01()
    {
        // Arrange
        var template = new Template();
        template.Load("Hello <%=Name%>!");
        template.AddArgument("Name", typeof(string));

        // Act
        var result = template.Run("Meziantou");
        Assert.Equal("Hello Meziantou!", result);
    }

    [Fact]
    public void Template_CodeEvalParameter02()
    {
        // Arrange
        var template = new Template();
        template.Load("Hello <%=Name%>!");
        var arguments = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            { "Name", "Meziantou" },
        };
        template.AddArguments(arguments);

        // Act
        var result = template.Run(arguments);
        Assert.Equal("Hello Meziantou!", result);
    }

    [Fact]
    public void Template_Loop01()
    {
        // Arrange
        var template = new Template();
        template.Load("Hello <% for(int i = 1; i <= 5; i++ ) { %><%= i %><% } %>!");

        // Act
        var result = template.Run();
        Assert.Equal("Hello 12345!", result);
    }

    [Fact]
    public void Template_UntypedArgument()
    {
        // Arrange
        var template = new Template();
        template.AddArgument("Name");
        template.Load("Hello <%= Name %>!");

        // Act
        var result = template.Run("John");
        Assert.Equal("Hello John!", result);
    }

    [Fact]
    public void Template_Debug()
    {
        // Arrange
        var template = new Template
        {
            Debug = true,
        };
        template.Load("Hello <%= \n" +
                      "#if DEBUG\n" +
                      "\"debug\"\n" +
                      "#elif RELEASE\n" +
                      "\"release\"\n" +
                      "#else\n" +
                      "#error Error\n" +
                      "#endif\n" +
                      "%>!");

        // Act
        var result = template.Run();
        Assert.Equal("Hello debug!", result);
    }

    [Fact]
    public void Template_Release()
    {
        // Arrange
        var template = new Template
        {
            Debug = false,
        };
        template.Load("Hello <%= \n" +
                      "#if DEBUG\n" +
                      "\"debug\"\n" +
                      "#elif RELEASE\n" +
                      "\"release\"\n" +
                      "#else\n" +
                      "#error Error\n" +
                      "#endif\n" +
                      "%>!");

        // Act
        var result = template.Run();
        Assert.Equal("Hello release!", result);
    }

    [Fact]
    public void Template_Directives_AreParsed_AndWellKnownDirectivesAreApplied()
    {
        var template = new TemplateWithoutCompilation();
        template.Load("""
            <%@ USING System.Linq %>
            <%@ InHeRiTs CustomBase %>
            <%@implements IFoo, IBar %>
            <%@ ReFeReNcE /tmp/folder/assembly with spaces.dll %>
            <%@ outputextension .cs %>
            """);

        var directives = template.Blocks.OfType<DirectiveBlock>().ToList();
        Assert.Collection(directives,
            directive =>
            {
                Assert.Equal("USING", directive.Name);
                Assert.Equal("System.Linq", directive.Value);
            },
            directive =>
            {
                Assert.Equal("InHeRiTs", directive.Name);
                Assert.Equal("CustomBase", directive.Value);
            },
            directive =>
            {
                Assert.Equal("implements", directive.Name);
                Assert.Equal("IFoo, IBar", directive.Value);
            },
            directive =>
            {
                Assert.Equal("ReFeReNcE", directive.Name);
                Assert.Equal("/tmp/folder/assembly with spaces.dll", directive.Value);
            },
            directive =>
            {
                Assert.Equal("outputextension", directive.Name);
                Assert.Equal(".cs", directive.Value);
            });

        template.Build(CancellationToken.None);

        Assert.Equal("CustomBase", template.BaseClassFullTypeName);
        Assert.Contains("System.Linq", template.Usings);
        Assert.Equal(["IFoo", "IBar"], template.ImplementedInterfaces);
        Assert.Contains("/tmp/folder/assembly with spaces.dll", template.ReferencePaths);
    }

    [Fact]
    public void Template_Directives_HavePositionMetadata()
    {
        const string source = """
            line1
            <%@ outputextension .cs %>
            line3
            """;
        var template = new Template();
        template.Load(source);

        var directive = Assert.Single(template.Blocks!.OfType<DirectiveBlock>());
        Assert.Equal(2, directive.Start.Line);
        Assert.Equal(3, directive.Start.Column);
        Assert.Equal(source.IndexOf(directive.Text, StringComparison.Ordinal), directive.Start.Index);
        Assert.Equal(2, directive.End.Line);
        Assert.True(directive.End.Column > directive.Start.Column);
        Assert.Equal(directive.Text.Length, directive.Span.Length);
        Assert.Equal(directive.Text, source.AsSpan(directive.Start.Index, directive.Span.Length).ToString());
    }

    [Fact]
    public void Template_UnknownDirective_DoesNotEmitCode()
    {
        var template = new Template();
        template.Load("Hello <%@ outputextension .cs %> World");

        var result = template.Run();

        Assert.Equal("Hello  World", result);
        Assert.Contains(template.Blocks!.OfType<DirectiveBlock>(), directive => string.Equals(directive.Name, "outputextension", StringComparison.Ordinal));
    }

    [Fact]
    public void Template_Blocks_HaveSpanMatchingOriginalText()
    {
        const string source = "A<%= 1 %>B";
        var template = new Template();
        template.Load(source);

        foreach (var block in template.Blocks!)
        {
            Assert.Equal(block.Text.Length, block.Span.Length);
            Assert.Equal(block.Text, source.AsSpan(block.Start.Index, block.Span.Length).ToString());
        }
    }

    [Fact]
    public void Template_Blocks_HaveCorrectSpanWithCRLF()
    {
        const string source = "line1\r\n<%= 1 %>\r\nline3";
        var template = new Template();
        template.Load(source);

        var codeBlock = Assert.Single(template.Blocks!.OfType<CodeBlock>());
        Assert.Equal(2, codeBlock.Start.Line);
        Assert.Equal(3, codeBlock.Start.Column);
        Assert.Equal(codeBlock.Text.Length, codeBlock.Span.Length);
        Assert.Equal(codeBlock.Text, source.AsSpan(codeBlock.Start.Index, codeBlock.Span.Length).ToString());
    }

    [Fact]
    public void TextPosition_HasValueSemantics()
    {
        var position1 = new TextPosition(1, 1, 0);
        var position2 = new TextPosition(1, 1, 0);
        var position3 = new TextPosition(1, 2, 1);

        Assert.Equal(position1, position2);
        Assert.True(position1 == position2);
        Assert.True(position1 != position3);
        Assert.True(position1 < position3);
        Assert.True(position3 > position1);
        Assert.Equal(0, position1.CompareTo((object)position2));
        Assert.Contains("L1", position1.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void TextSpan_HasValueSemantics()
    {
        var start = new TextPosition(1, 1, 0);
        var middle = new TextPosition(1, 3, 2);
        var end = new TextPosition(1, 5, 4);
        var span1 = new TextSpan(start, middle);
        var span2 = new TextSpan(start, middle);
        var span3 = new TextSpan(start, end);

        Assert.Equal(span1, span2);
        Assert.True(span1 == span2);
        Assert.True(span1 != span3);
        Assert.True(span1 < span3);
        Assert.True(span3 > span1);
        Assert.Equal(2, span1.Length);
        Assert.Contains("..", span1.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Template_ImplementsDirective_IsAddedToClassSignature()
    {
        var template = new TemplateWithoutCompilation
        {
            BaseClassFullTypeName = "BaseClass",
        };
        template.Load("<%@implements IFoo, IBar %>");

        template.Build(CancellationToken.None);

        Assert.Contains("public class Template : BaseClass, IFoo, IBar", template.SourceCode, StringComparison.Ordinal);
    }

    private sealed class TemplateWithoutCompilation : Template
    {
        protected override void Compile(string source, CancellationToken cancellationToken)
        {
        }
    }
}
