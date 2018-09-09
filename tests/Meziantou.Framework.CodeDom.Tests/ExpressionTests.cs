using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Meziantou.Framework.CodeDom.Tests
{
    [TestClass]
    public class ExpressionTests
    {
        [TestMethod]
        public void CodeExpression_Op_Binary_Add()
        {
            var v = new VariableReferenceExpression("a");
            var result = v + 1;

            Assert.AreEqual(BinaryOperator.Add, result.Operator);
            Assert.AreEqual("a", ((VariableReferenceExpression)result.LeftExpression).Name);
            Assert.AreEqual(1, ((LiteralExpression)result.RightExpression).Value);
        }

        [TestMethod]
        public void CodeExpression_Op_Binary_Multiply()
        {
            var result = 2 * (Expression)1;

            Assert.AreEqual(BinaryOperator.Multiply, result.Operator);
            Assert.AreEqual(2, ((LiteralExpression)result.LeftExpression).Value);
            Assert.AreEqual(1, ((LiteralExpression)result.RightExpression).Value);
        }

        [TestMethod]
        public void CodeExpression_Op_Unary_Add()
        {
            var result = -((Expression)5);

            Assert.AreEqual(5, ((LiteralExpression)result.Expression).Value);
            Assert.AreEqual(UnaryOperator.Minus, result.Operator);
        }

        [TestMethod]
        public void CodeExpression_Indexer_OneIndex()
        {
            var result = new VariableReferenceExpression("a")[1];

            Assert.AreEqual("a", ((VariableReferenceExpression)result.ArrayExpression).Name);
            Assert.AreEqual(1, result.Indices.Count);
            Assert.AreEqual(1, ((LiteralExpression)result.Indices[0]).Value);
        }

        [TestMethod]
        public void CodeExpression_Indexer_MultipleIndices()
        {
            var result = new VariableReferenceExpression("a")[1, "test"];

            Assert.AreEqual("a", ((VariableReferenceExpression)result.ArrayExpression).Name);
            Assert.AreEqual(2, result.Indices.Count);
            Assert.AreEqual(1, ((LiteralExpression)result.Indices[0]).Value);
            Assert.AreEqual("test", ((LiteralExpression)result.Indices[1]).Value);
        }
    }
}
