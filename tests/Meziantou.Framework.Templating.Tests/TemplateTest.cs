using Xunit;

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
        var arguments = new Dictionary<string, object>(StringComparer.Ordinal)
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
}
