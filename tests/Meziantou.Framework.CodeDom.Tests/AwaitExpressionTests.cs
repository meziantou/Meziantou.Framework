using Xunit;

namespace Meziantou.Framework.CodeDom.Tests;

public class AwaitExpressionTests
{
    [Fact]
    public void CodeAwaitExpression_ConfigureAwait()
    {
        var expression = new AwaitExpression(new SnippetExpression("test"));
        var configuredExpression = expression.ConfigureAwait(continueOnCapturedContext: true);
        Assert.Equal(expression, configuredExpression);
        Assert.Equal(true, configuredExpression.Expression.As<MethodInvokeExpression>().Arguments[0].As<LiteralExpression>().Value);
    }

    [Fact]
    public void CodeAwaitExpression_ConfigureAwait_NullExpression()
    {
        var expression = new AwaitExpression();
        var configuredExpression = expression.ConfigureAwait(continueOnCapturedContext: true);
        Assert.Equal(expression, configuredExpression);
        Assert.Null(configuredExpression.Expression);
    }
}
