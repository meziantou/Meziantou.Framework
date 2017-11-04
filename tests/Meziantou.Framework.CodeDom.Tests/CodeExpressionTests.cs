using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Meziantou.Framework.CodeDom.Tests
{
    [TestClass]
    public class CodeExpressionTests
    {
        [TestMethod]
        public void CodeExpression_Op_Binary_Add()
        {
            var v = new CodeVariableReference("a");
            var result = v + 1;

            Assert.AreEqual(BinaryOperator.Add, result.Operator);
            Assert.AreEqual("a", ((CodeVariableReference)result.LeftExpression).Name);
            Assert.AreEqual(1, ((CodeLiteralExpression)result.RightExpression).Value);
        }

        [TestMethod]
        public void CodeExpression_Op_Binary_Multiply()
        {
            var result = 2 * (CodeExpression)1;

            Assert.AreEqual(BinaryOperator.Multiply, result.Operator);
            Assert.AreEqual(2, ((CodeLiteralExpression)result.LeftExpression).Value);
            Assert.AreEqual(1, ((CodeLiteralExpression)result.RightExpression).Value);
        }

        [TestMethod]
        public void CodeExpression_Op_Unary_Add()
        {
            var result = -((CodeExpression)5);

            Assert.AreEqual(5, ((CodeLiteralExpression)result.Expression).Value);
            Assert.AreEqual(UnaryOperator.Minus, result.Operator);
        }
    }
}
