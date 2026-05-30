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
        template.Arguments.Add(new TemplateArgument("Name", typeof(string)));

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
        foreach (var argument in arguments)
        {
            template.Arguments.Add(new TemplateArgument(argument.Key, argument.Value?.GetType()));
        }

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
        template.Arguments.Add(new TemplateArgument("Name", type: null));
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
            <%@ reference alias=MyAlias /tmp/folder/assembly.dll %>
            <%@ Include /tmp/folder/Included.cs %>
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
                Assert.Equal("reference", directive.Name);
                Assert.Equal("alias=MyAlias /tmp/folder/assembly.dll", directive.Value);
            },
            directive =>
            {
                Assert.Equal("Include", directive.Name);
                Assert.Equal("/tmp/folder/Included.cs", directive.Value);
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
        Assert.Contains(template.AssemblyReferences, reference => string.Equals(reference.Path, "/tmp/folder/assembly with spaces.dll", StringComparison.Ordinal));
        Assert.Contains(template.AssemblyReferences, reference => string.Equals(reference.Path, "/tmp/folder/assembly.dll", StringComparison.Ordinal) && string.Equals(reference.Alias, "MyAlias", StringComparison.Ordinal));
        Assert.Contains(template.IncludedSourceFiles, fileReference => string.Equals(fileReference.Path, "/tmp/folder/Included.cs", StringComparison.Ordinal));
    }

    [Fact]
    public void Template_IncludeDirective_IncludesSourceFileInCompilation()
    {
        var sourceFilePath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".cs");
        File.WriteAllText(sourceFilePath, """
            public static class IncludedHelper
            {
                public static string GetValue() => "included";
            }
            """);

        try
        {
            var template = new Template();
            template.Load($"""
                <%@ include {sourceFilePath} %>
                <%= IncludedHelper.GetValue() %>
                """);

            var result = template.Run();

            Assert.Equal("included", result.Trim());
        }
        finally
        {
            File.Delete(sourceFilePath);
        }
    }

    [Fact]
    public void Template_Directives_HavePositionMetadata()
    {
        const string Source = """
            line1
            <%@ outputextension .cs %>
            line3
            """;
        var template = new Template();
        template.Load(Source);

        var directive = Assert.Single(template.Blocks!.OfType<DirectiveBlock>());
        Assert.Equal(2, directive.Start.Line);
        Assert.Equal(3, directive.Start.Column);
        Assert.Equal(Source.IndexOf(directive.Text, StringComparison.Ordinal), directive.Start.Index);
        Assert.Equal(2, directive.End.Line);
        Assert.True(directive.End.Column > directive.Start.Column);
        Assert.Equal(directive.Text.Length, directive.Span.Length);
        Assert.Equal(directive.Text, Source.AsSpan(directive.Start.Index, directive.Span.Length).ToString());
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
        const string Source = "A<%= 1 %>B";
        var template = new Template();
        template.Load(Source);

        foreach (var block in template.Blocks!)
        {
            Assert.Equal(block.Text.Length, block.Span.Length);
            Assert.Equal(block.Text, Source.AsSpan(block.Start.Index, block.Span.Length).ToString());
        }
    }

    [Fact]
    public void Template_Blocks_HaveCorrectSpanWithCRLF()
    {
        const string Source = "line1\r\n<%= 1 %>\r\nline3";
        var template = new Template();
        template.Load(Source);

        var codeBlock = Assert.Single(template.Blocks!.OfType<CodeBlock>());
        Assert.Equal(2, codeBlock.Start.Line);
        Assert.Equal(3, codeBlock.Start.Column);
        Assert.Equal(codeBlock.Text.Length, codeBlock.Span.Length);
        Assert.Equal(codeBlock.Text, Source.AsSpan(codeBlock.Start.Index, codeBlock.Span.Length).ToString());
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
    public void Template_Collections_CanBeModifiedBeforeBuild()
    {
        var template = new Template();
        var argument = new TemplateArgument("Value", typeof(int));
        template.Arguments.Add(argument);
        template.Usings.Add("System");
        template.AssemblyReferences.Add("path/to/assembly.dll");
        template.IncludedSourceFiles.Add("path/to/include.cs");
        template.ImplementedInterfaces.Add("IFoo");
        template.Load("Hello");

        var customBlock = new TextBlock(template, "!", index: 10);
        template.Blocks.Add(customBlock);

        Assert.Contains(argument, template.Arguments);
        Assert.Contains("System", template.Usings);
        Assert.Contains(template.AssemblyReferences, reference => string.Equals(reference.Path, "path/to/assembly.dll", StringComparison.Ordinal));
        Assert.Contains(template.IncludedSourceFiles, fileReference => string.Equals(fileReference.Path, "path/to/include.cs", StringComparison.Ordinal));
        Assert.Contains("IFoo", template.ImplementedInterfaces);
        Assert.Contains(customBlock, template.Blocks);

        Assert.True(template.Arguments.Remove(argument));
        Assert.True(template.Usings.Remove("System"));
        Assert.True(template.AssemblyReferences.Remove(new AssemblyReference("path/to/assembly.dll")));
        Assert.True(template.IncludedSourceFiles.Remove(new FileReference("path/to/include.cs")));
        Assert.True(template.ImplementedInterfaces.Remove("IFoo"));
        Assert.True(template.Blocks.Remove(customBlock));
    }

    [Fact]
    public void AssemblyReferenceCollection_AddHelpers_CreateReferences()
    {
        var templateAssemblyPath = typeof(Template).Assembly.Location;
        var modulePath = typeof(Template).Module.Assembly.Location;
        var collection = new AssemblyReferenceCollection();
        collection.Add(typeof(Template));
        collection.Add(typeof(Template).Assembly);
        collection.Add(typeof(Template).Module);
        collection.Add("path/to/assembly.dll");
        collection.Add("path/to/aliased.dll", "AliasedReference");

        Assert.Contains(collection, reference => string.Equals(reference.Path, templateAssemblyPath, StringComparison.Ordinal) && reference.Alias is null);
        Assert.Contains(collection, reference => string.Equals(reference.Path, modulePath, StringComparison.Ordinal) && reference.Alias is null);
        Assert.Contains(collection, reference => string.Equals(reference.Path, "path/to/assembly.dll", StringComparison.Ordinal) && reference.Alias is null);
        Assert.Contains(collection, reference => string.Equals(reference.Path, "path/to/aliased.dll", StringComparison.Ordinal) && string.Equals(reference.Alias, "AliasedReference", StringComparison.Ordinal));
    }

    [Fact]
    public void Template_Load_ThrowsOnceBuilt()
    {
        var template = new Template();
        template.Load("text");
        template.Build(CancellationToken.None);

        Assert.Throws<InvalidOperationException>(() => template.Load("another text"));
    }

    [Fact]
    public void Template_Collections_ValidateNullAndEmptyItems()
    {
        var template = new Template();

        Assert.Throws<ArgumentNullException>(() => template.Arguments.Add(null!));
        Assert.Throws<ArgumentNullException>(() => template.Usings.Add(null!));
        Assert.Throws<ArgumentNullException>(() => template.AssemblyReferences.Add((AssemblyReference)null!));
        Assert.Throws<ArgumentNullException>(() => template.IncludedSourceFiles.Add((FileReference)null!));
        Assert.Throws<ArgumentNullException>(() => template.ImplementedInterfaces.Add(null!));
        Assert.Throws<ArgumentNullException>(() => template.Blocks.Add(null!));

        Assert.Throws<ArgumentException>(() => template.Arguments.Add(new TemplateArgument(string.Empty, typeof(int))));
        Assert.Throws<ArgumentException>(() => template.Usings.Add(string.Empty));
        Assert.Throws<ArgumentException>(() => template.AssemblyReferences.Add(string.Empty));
        Assert.Throws<ArgumentException>(() => template.IncludedSourceFiles.Add(string.Empty));
        Assert.Throws<ArgumentException>(() => template.ImplementedInterfaces.Add(string.Empty));
        Assert.Throws<ArgumentException>(() => new AssemblyReference("path/to/assembly.dll", " "));
    }

    [Fact]
    public void Template_Collections_AreFrozenAfterBuild()
    {
        var template = new Template();
        template.Arguments.Add(new TemplateArgument("Name", typeof(string)));
        template.Load("Hello <%= Name %>!");

        template.Build(CancellationToken.None);

        Assert.True(template.Arguments.IsFrozen);
        Assert.True(template.Usings.IsFrozen);
        Assert.True(template.AssemblyReferences.IsFrozen);
        Assert.True(template.IncludedSourceFiles.IsFrozen);
        Assert.True(template.ImplementedInterfaces.IsFrozen);
        Assert.True(template.Blocks.IsFrozen);

        Assert.Throws<InvalidOperationException>(() => template.Arguments.Add(new TemplateArgument("Other", typeof(string))));
        Assert.Throws<InvalidOperationException>(() => template.Usings.Add("System"));
        Assert.Throws<InvalidOperationException>(() => template.AssemblyReferences.Add("path/to/assembly.dll"));
        Assert.Throws<InvalidOperationException>(() => template.IncludedSourceFiles.Add("path/to/include.cs"));
        Assert.Throws<InvalidOperationException>(() => template.ImplementedInterfaces.Add("IFoo"));
        Assert.Throws<InvalidOperationException>(() => template.Blocks.Add(new TextBlock(template, "!", index: 10)));
    }

    [Fact]
    public void Template_ManualInterfaces_AreNotClearedByDirectiveApplication()
    {
        var template = new TemplateWithoutCompilation();
        template.ImplementedInterfaces.Add("IManual");
        template.Load("<%@implements IDirective %>");

        template.Build(CancellationToken.None);

        Assert.Equal(["IManual", "IDirective"], template.ImplementedInterfaces);
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
