using Xunit;

namespace Meziantou.Framework.CodeDom.Tests
{
    public class ExpressionTests
    {
        [Fact]
        public void CodeExpression_Op_Binary_Add()
        {
            var v = new VariableReferenceExpression("a");
            var result = v + 1;

            Assert.Equal(BinaryOperator.Add, result.Operator);
            Assert.Equal("a", ((VariableReferenceExpression)result.LeftExpression).Name);
            Assert.Equal(1, ((LiteralExpression)result.RightExpression).Value);
        }

        [Fact]
        public void CodeExpression_Op_Binary_Multiply()
        {
            var result = 2 * (Expression)1;

            Assert.Equal(BinaryOperator.Multiply, result.Operator);
            Assert.Equal(2, ((LiteralExpression)result.LeftExpression).Value);
            Assert.Equal(1, ((LiteralExpression)result.RightExpression).Value);
        }

        [Fact]
        public void CodeExpression_Op_Unary_Add()
        {
            var result = -(Expression)5;

            Assert.Equal(5, ((LiteralExpression)result.Expression).Value);
            Assert.Equal(UnaryOperator.Minus, result.Operator);
        }

        [Fact]
        public void CodeExpression_Indexer_OneIndex()
        {
            var result = new VariableReferenceExpression("a")[1];

            Assert.Equal("a", ((VariableReferenceExpression)result.ArrayExpression).Name);
            Assert.Single(result.Indices);
            Assert.Equal(1, ((LiteralExpression)result.Indices[0]).Value);
        }

        [Fact]
        public void CodeExpression_Indexer_MultipleIndices()
        {
            var result = new VariableReferenceExpression("a")[1, "test"];

            Assert.Equal("a", ((VariableReferenceExpression)result.ArrayExpression).Name);
            Assert.Equal(2, result.Indices.Count);
            Assert.Equal(1, ((LiteralExpression)result.Indices[0]).Value);
            Assert.Equal("test", ((LiteralExpression)result.Indices[1]).Value);
        }
    }
}
