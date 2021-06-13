using Xunit;
using FluentAssertions;

namespace Meziantou.Framework.CodeDom.Tests
{
    public class AwaitExpressionTests
    {
        [Fact]
        public void CodeAwaitExpression_ConfigureAwait()
        {
            var expression = new AwaitExpression(new SnippetExpression("test"));
            var configuredExpression = expression.ConfigureAwait(continueOnCapturedContext: true);

            configuredExpression.Should().Be(expression);
            configuredExpression.Expression.As<MethodInvokeExpression>().Arguments[0].As<LiteralExpression>().Value.Should().Be(true);
        }

        [Fact]
        public void CodeAwaitExpression_ConfigureAwait_NullExpression()
        {
            var expression = new AwaitExpression();
            var configuredExpression = expression.ConfigureAwait(continueOnCapturedContext: true);

            configuredExpression.Should().Be(expression);
            configuredExpression.Expression.Should().BeNull();
        }
    }
}
