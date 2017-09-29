using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Meziantou.Framework.CodeDom.Tests
{
    [TestClass]
    public class CSharpCodeGeneratorTests
    {
        [TestMethod]
        public void CSharpCodeGenerator_Factorial()
        {
            var unit = new CodeCompilationUnit();
            var ns = unit.AddNamespace("Meziantou.Framework.CodeDom");
            var c = ns.AddType(new CodeClassDeclaration("Sample"));
            var method = c.AddMember(new CodeMethodDeclaration("Factorial"));
            method.ReturnType = typeof(int);
            var n = method.AddArgument("n", typeof(int));
            method.Modifiers = Modifiers.Public | Modifiers.Static;

            method.Statements = new CodeConditionStatement()
            {
                Condition = new CodeBinaryExpression(BinaryOperator.LessThanOrEqual, 1, n),
                TrueStatements = new CodeReturnStatement(1),
                FalseStatements = new CodeReturnStatement(new CodeBinaryExpression(
                    BinaryOperator.Multiply,
                    n,
                    new CodeMethodInvokeExpression(method, new CodeBinaryExpression(BinaryOperator.Substract, n, 1))))
            };

            var generator = new CSharpCodeGenerator();
            var result = generator.Write(unit);

            Assert.AreEqual(@"namespace Meziantou.Framework.CodeDom
{
    class Sample
    {
        public static int Factorial(int n)
        {
            if ((1 <= n))
            {
                return 1;
            }
            else
            {
                return (n * this.Factorial((n - 1)));
            }
        }
    }
}
", result);
        }

        [TestMethod]
        public void CSharpCodeGenerator_WhileLoop()
        {
            var loop = new CodeWhileStatement();
            loop.Condition = new CodeLiteralExpression(true);
            loop.Body = new CodeStatementCollection();

            var generator = new CSharpCodeGenerator();
            var result = generator.Write(loop);

            Assert.AreEqual(@"while (true)
{
}
", result);
        }

        [TestMethod]
        public void CSharpCodeGenerator_Constructor()
        {
            var type = new CodeClassDeclaration("Sample");
            var ctor = type.AddMember(new CodeConstructorDeclaration());
            ctor.Initializer = new CodeConstructorBaseInitializer();
            ctor.Modifiers = Modifiers.Public;

            var generator = new CSharpCodeGenerator();
            var result = generator.Write(type);

            Assert.AreEqual(@"class Sample
{
    public Sample()
        : base()
    {
    }
}
", result);
        }
    }
}
