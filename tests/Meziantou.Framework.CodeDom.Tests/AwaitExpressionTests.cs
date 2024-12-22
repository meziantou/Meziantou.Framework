using Xunit;

namespace Meziantou.Framework.CodeDom.Tests;

public class AwaitExpressionTests
{
    [Fact]
    public void CodeAwaitExpression_ConfigureAwait()
    {
        var expression = new AwaitExpression(new SnippetExpression("test"));
        var configuredExpression = expression.ConfigureAwait(continueOnCapturedContext: true);

        Assert.Same(expression, configuredExpression);
        Assert.True(configuredExpression.Expression is MethodInvokeExpression methodInvokeExpression && methodInvokeExpression.Arguments[0] is LiteralExpression literalExpression && literalExpression.Value.Equals(true));
    }

    [Fact]
    public void CodeAwaitExpression_ConfigureAwait_NullExpression()
    {
        var expression = new AwaitExpression();
        var configuredExpression = expression.ConfigureAwait(continueOnCapturedContext: true);

        Assert.Same(expression, configuredExpression);
        Assert.Null(configuredExpression.Expression);
    }
}
