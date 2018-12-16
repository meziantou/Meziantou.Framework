using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Meziantou.Framework.CodeDom.Tests
{
    [TestClass]
    public class AwaitExpressionTests
    {
        [TestMethod]
        public void CodeAwaitExpression_ConfigureAwait()
        {
            var expression = new AwaitExpression(new SnippetExpression("test"));
            var configuredExpression = expression.ConfigureAwait(continueOnCapturedContext: true);

            Assert.AreEqual(expression, configuredExpression);
            Assert.AreEqual(true, configuredExpression.Expression.As<MethodInvokeExpression>().Arguments[0].As<LiteralExpression>().Value);
        }

        [TestMethod]
        public void CodeAwaitExpression_ConfigureAwait_NullExpression()
        {
            var expression = new AwaitExpression();
            var configuredExpression = expression.ConfigureAwait(continueOnCapturedContext: true);

            Assert.AreEqual(expression, configuredExpression);
            Assert.AreEqual(null, configuredExpression.Expression);
        }
    }
}
