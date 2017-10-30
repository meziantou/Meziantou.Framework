using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;

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
        public void CSharpCodeGenerator_ClassDeclaration()
        {
            var type = new CodeClassDeclaration("Sample");

            var generator = new CSharpCodeGenerator();
            var result = generator.Write(type);

            Assert.AreEqual(@"class Sample
{
}
", result);
        }

        [TestMethod]
        public void CSharpCodeGenerator_ClassDeclaration_Generic()
        {
            var type = new CodeClassDeclaration("Sample");
            type.Parameters.Add(new CodeTypeParameter("T1") { Constraints = { new CodeValueTypeTypeParameterConstraint() } });
            type.Parameters.Add(new CodeTypeParameter("T2")
            {
                Constraints = {
                        new CodeConstructorParameterConstraint(),
                        new CodeClassTypeParameterConstraint(),
                        new CodeBaseTypeParameterConstraint(typeof(ICloneable))
                }
            });

            var generator = new CSharpCodeGenerator();
            var result = generator.Write(type);

            Assert.AreEqual(@"class Sample<T1, T2>
    where T1 : struct
    where T2 : class, System.ICloneable, new()
{
}
", result);
        }

        [TestMethod]
        public void CSharpCodeGenerator_InterfaceDeclaration()
        {
            var type = new CodeInterfaceDeclaration("Sample");

            var generator = new CSharpCodeGenerator();
            var result = generator.Write(type);

            Assert.AreEqual(@"interface Sample
{
}
", result);
        }

        [TestMethod]
        public void CSharpCodeGenerator_InterfaceDeclaration_Generic()
        {
            var type = new CodeInterfaceDeclaration("Sample");
            type.Parameters.Add(new CodeTypeParameter("T1") { Constraints = { new CodeValueTypeTypeParameterConstraint() } });
            type.Parameters.Add(new CodeTypeParameter("T2")
            {
                Constraints = {
                        new CodeConstructorParameterConstraint(),
                        new CodeClassTypeParameterConstraint(),
                        new CodeBaseTypeParameterConstraint(typeof(ICloneable))
                }
            });

            var generator = new CSharpCodeGenerator();
            var result = generator.Write(type);

            Assert.AreEqual(@"interface Sample<T1, T2>
    where T1 : struct
    where T2 : class, System.ICloneable, new()
{
}
", result);
        }

        [TestMethod]
        public void CSharpCodeGenerator_EnumDeclaration()
        {
            var type = new CodeEnumerationDeclaration("Sample");
            type.BaseType = typeof(uint);
            type.Modifiers = Modifiers.Internal;
            type.Members.Add(new CodeEnumerationMember("A", 1));
            type.Members.Add(new CodeEnumerationMember("B", 2));
            type.CustomAttributes.Add(new CodeCustomAttribute(typeof(FlagsAttribute)));

            var generator = new CSharpCodeGenerator();
            var result = generator.Write(type);

            Assert.AreEqual(@"[System.FlagsAttribute]
internal enum Sample : uint
{
    A = 1,
    B = 2
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
            ctor.Modifiers = Modifiers.Internal;
            var generator = new CSharpCodeGenerator();
            var result = generator.Write(type);

            Assert.AreEqual(@"class Sample
{
    internal Sample()
    {
    }
}
", result);
        }

        [TestMethod]
        public void CSharpCodeGenerator_Constructor_Base()
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

        [TestMethod]
        public void CSharpCodeGenerator_Constructor_This()
        {
            var type = new CodeClassDeclaration("Sample");
            var ctor = type.AddMember(new CodeConstructorDeclaration());
            ctor.Initializer = new CodeConstructorThisInitializer();
            ctor.Modifiers = Modifiers.Public;

            var generator = new CSharpCodeGenerator();
            var result = generator.Write(type);

            Assert.AreEqual(@"class Sample
{
    public Sample()
        : this()
    {
    }
}
", result);
        }

        [TestMethod]
        public void CSharpCodeGenerator_Constructor_ThisWithArgs()
        {
            var type = new CodeClassDeclaration("Sample");
            var ctor = type.AddMember(new CodeConstructorDeclaration());
            ctor.Initializer = new CodeConstructorThisInitializer(new CodeLiteralExpression("arg"));
            ctor.Modifiers = Modifiers.Public;

            var generator = new CSharpCodeGenerator();
            var result = generator.Write(type);

            Assert.AreEqual(@"class Sample
{
    public Sample()
        : this(""arg"")
    {
    }
}
", result);
        }

        [TestMethod]
        public void CSharpCodeGenerator_ArrayIndexer()
        {
            var array = new CodeVariableReference("array");
            CodeExpression expression = new CodeArrayIndexerExpression(array, 10);

            var generator = new CSharpCodeGenerator();
            var result = generator.Write(expression);

            Assert.AreEqual(@"array[10]", result);
        }

        [TestMethod]
        public void CSharpCodeGenerator_ArrayIndexer_Multiple()
        {
            var array = new CodeVariableReference("array");
            CodeExpression expression = new CodeArrayIndexerExpression(array, 10, "test");

            var generator = new CSharpCodeGenerator();
            var result = generator.Write(expression);

            Assert.AreEqual(@"array[10, ""test""]", result);
        }

        [TestMethod]
        public void CSharpCodeGenerator_Assign()
        {
            var statement = new CodeAssignStatement(new CodeVariableReference("a"), 10);

            var generator = new CSharpCodeGenerator();
            var result = generator.Write(statement);

            Assert.AreEqual(@"a = 10;
", result);
        }

        [TestMethod]
        public void CSharpCodeGenerator_If()
        {
            var statement = new CodeConditionStatement();
            statement.Condition = new CodeLiteralExpression(true);
            statement.TrueStatements = new CodeSnippetStatement("TrueSnippet");

            var generator = new CSharpCodeGenerator();
            var result = generator.Write(statement);

            Assert.AreEqual(@"if (true)
{
    TrueSnippet
}
", result);
        }

        [TestMethod]
        public void CSharpCodeGenerator_If_Else()
        {
            var statement = new CodeConditionStatement();
            statement.Condition = new CodeLiteralExpression(true);
            statement.TrueStatements = new CodeSnippetStatement("TrueSnippet");
            statement.FalseStatements = new CodeSnippetStatement("FalseSnippet");

            var generator = new CSharpCodeGenerator();
            var result = generator.Write(statement);

            Assert.AreEqual(@"if (true)
{
    TrueSnippet
}
else
{
    FalseSnippet
}
", result);
        }

        [TestMethod]
        public void CSharpCodeGenerator_If_Empty()
        {
            var statement = new CodeConditionStatement();
            statement.Condition = new CodeLiteralExpression(true);

            var generator = new CSharpCodeGenerator();
            var result = generator.Write(statement);

            Assert.AreEqual(@"if (true)
{
}
", result);
        }

        [TestMethod]
        public void CSharpCodeGenerator_Try_Catch()
        {
            var statement = new CodeTryCatchFinallyStatement();
            statement.Try = new CodeSnippetStatement("TrySnippet");
            statement.Catch = new CodeCatchClauseCollection();
            statement.Catch.Add(new CodeCatchClause() { Body = new CodeSnippetStatement("Catch1") });

            var generator = new CSharpCodeGenerator();
            var result = generator.Write(statement);

            Assert.AreEqual(@"try
{
    TrySnippet
}
catch
{
    Catch1
}
", result);
        }

        [TestMethod]
        public void CSharpCodeGenerator_Try_Catch_WithException()
        {
            var statement = new CodeTryCatchFinallyStatement();
            statement.Try = new CodeSnippetStatement("TrySnippet");
            statement.Catch = new CodeCatchClauseCollection();
            statement.Catch.Add(new CodeCatchClause()
            {
                ExceptionType = typeof(NotImplementedException),
                ExceptionVariableName = "nie",
                Body = new CodeSnippetStatement("Catch1")
            });
            statement.Catch.Add(new CodeCatchClause()
            {
                ExceptionType = typeof(Exception),
                ExceptionVariableName = "ex",
                Body = new CodeThrowStatement()
            });

            var generator = new CSharpCodeGenerator();
            var result = generator.Write(statement);

            Assert.AreEqual(@"try
{
    TrySnippet
}
catch (System.NotImplementedException nie)
{
    Catch1
}
catch (System.Exception ex)
{
    throw;
}
", result);
        }

        [TestMethod]
        public void CSharpCodeGenerator_Try_Finally()
        {
            var statement = new CodeTryCatchFinallyStatement();
            statement.Try = new CodeSnippetStatement("TrySnippet");
            statement.Finally = new CodeSnippetStatement("FinallyStatement");

            var generator = new CSharpCodeGenerator();
            var result = generator.Write(statement);

            Assert.AreEqual(@"try
{
    TrySnippet
}
finally
{
    FinallyStatement
}
", result);
        }

        [TestMethod]
        public void CSharpCodeGenerator_Literal_String()
        {
            var literal = new CodeLiteralExpression("test");
            var generator = new CSharpCodeGenerator();
            var result = generator.Write(literal);

            Assert.AreEqual("\"test\"", result);
        }

        [TestMethod]
        public void CSharpCodeGenerator_Literal_StringWithNewLine()
        {
            var literal = new CodeLiteralExpression("line1\r\nline2");
            var generator = new CSharpCodeGenerator();
            var result = generator.Write(literal);

            Assert.AreEqual("\"line1\\r\\nline2\"", result);
        }

        [TestMethod]
        public void CSharpCodeGenerator_Default()
        {
            var expr = new CodeDefaultValueExpression(typeof(string));
            var generator = new CSharpCodeGenerator();
            var result = generator.Write(expr);

            Assert.AreEqual("default(string)", result);
        }

        [DataTestMethod]
        [DataRow(BinaryOperator.Add, "+")]
        [DataRow(BinaryOperator.And, "&&")]
        [DataRow(BinaryOperator.BitwiseAnd, "&")]
        [DataRow(BinaryOperator.BitwiseOr, "|")]
        [DataRow(BinaryOperator.Divide, "/")]
        [DataRow(BinaryOperator.Equals, "==")]
        [DataRow(BinaryOperator.GreaterThan, ">")]
        [DataRow(BinaryOperator.GreaterThanOrEqual, ">=")]
        [DataRow(BinaryOperator.LessThan, "<")]
        [DataRow(BinaryOperator.LessThanOrEqual, "<=")]
        [DataRow(BinaryOperator.Modulo, "%")]
        [DataRow(BinaryOperator.Multiply, "*")]
        [DataRow(BinaryOperator.NotEquals, "!=")]
        [DataRow(BinaryOperator.Or, "||")]
        [DataRow(BinaryOperator.ShiftLeft, "<<")]
        [DataRow(BinaryOperator.ShiftRight, ">>")]
        [DataRow(BinaryOperator.Substract, "-")]
        public void CSharpCodeGenerator_BinaryExpression(BinaryOperator op, string symbol)
        {
            var expression = new CodeBinaryExpression(op, 1, 2);
            var generator = new CSharpCodeGenerator();
            var result = generator.Write(expression);

            Assert.AreEqual($"(1 {symbol} 2)", result);
        }

        [DataTestMethod]
        [DataRow(UnaryOperator.Complement, "~")]
        [DataRow(UnaryOperator.Minus, "-")]
        [DataRow(UnaryOperator.Not, "!")]
        [DataRow(UnaryOperator.Plus, "+")]
        [DataRow(UnaryOperator.PreDecrement, "--")]
        [DataRow(UnaryOperator.PreIncrement, "++")]
        public void CSharpCodeGenerator_UnaryExpression_Pre(UnaryOperator op, string symbol)
        {
            var expression = new CodeUnaryExpression(op, 1);
            var generator = new CSharpCodeGenerator();
            var result = generator.Write(expression);

            Assert.AreEqual($"({symbol}1)", result);
        }

        [DataTestMethod]
        [DataRow(UnaryOperator.PostIncrement, "++")]
        [DataRow(UnaryOperator.PostDecrement, "--")]
        public void CSharpCodeGenerator_UnaryExpression_Post(UnaryOperator op, string symbol)
        {
            var expression = new CodeUnaryExpression(op, 1);
            var generator = new CSharpCodeGenerator();
            var result = generator.Write(expression);

            Assert.AreEqual($"(1{symbol})", result);
        }
    }
}
