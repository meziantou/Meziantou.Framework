using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Meziantou.Framework.CodeDom.Tests
{
    [TestClass]
    public class CodeAwaitExpressionTests
    {
        [TestMethod]
        public void CodeAwaitExpression_ConfigureAwait()
        {
            var expression = new CodeAwaitExpression(new CodeSnippetExpression("test"));
            var configuredExpression = expression.ConfigureAwait(true);

            Assert.AreEqual(expression, configuredExpression);
            Assert.AreEqual(true, configuredExpression.Expression.As<CodeMethodInvokeExpression>().Arguments[0].As<CodeLiteralExpression>().Value);
        }

        [TestMethod]
        public void CodeAwaitExpression_ConfigureAwait_NullExpression()
        {
            var expression = new CodeAwaitExpression();
            var configuredExpression = expression.ConfigureAwait(true);

            Assert.AreEqual(expression, configuredExpression);
            Assert.AreEqual(null, configuredExpression.Expression);
        }
    }
}
