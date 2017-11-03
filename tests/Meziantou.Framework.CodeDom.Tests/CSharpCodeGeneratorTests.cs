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

            Assert.That.StringEquals(@"namespace Meziantou.Framework.CodeDom
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

            Assert.That.StringEquals(@"class Sample
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

            Assert.That.StringEquals(@"class Sample<T1, T2>
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

            Assert.That.StringEquals(@"interface Sample
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

            Assert.That.StringEquals(@"interface Sample<T1, T2>
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

            Assert.That.StringEquals(@"[System.FlagsAttribute]
internal enum Sample : uint
{
    A = 1,
    B = 2
}
", result);
        }

        [TestMethod]
        public void CSharpCodeGenerator_DelegateDeclaration()
        {
            var d = new CodeDelegateDeclaration("Sample");
            d.ReturnType = typeof(void);
            d.Arguments.Add(new CodeMethodArgumentDeclaration(typeof(string), "a"));
            d.Modifiers = Modifiers.Public;

            var generator = new CSharpCodeGenerator();
            var result = generator.Write(d);

            Assert.That.StringEquals(@"public delegate void Sample(string a);
", result);
        }

        [TestMethod]
        public void CSharpCodeGenerator_FieldDeclaration()
        {
            var type = new CodeClassDeclaration("Sample");
            type.AddMember(new CodeFieldDeclaration("_a", typeof(int)));
            type.AddMember(new CodeFieldDeclaration("_b", typeof(Type), Modifiers.Private));
            type.AddMember(new CodeFieldDeclaration("_c", typeof(int), Modifiers.Protected, 10));

            var generator = new CSharpCodeGenerator();
            var result = generator.Write(type);

            Assert.That.StringEquals(@"class Sample
{
    int _a;

    private System.Type _b;

    protected int _c = 10;
}
", result);
        }

        [TestMethod]
        public void CSharpCodeGenerator_EventDeclaration()
        {
            var type = new CodeClassDeclaration("Sample");
            type.AddMember(new CodeEventFieldDeclaration("A", typeof(EventHandler), Modifiers.Public));

            var generator = new CSharpCodeGenerator();
            var result = generator.Write(type);

            Assert.That.StringEquals(@"class Sample
{
    public event System.EventHandler A;
}
", result);
        }

        [TestMethod]
        public void CSharpCodeGenerator_AddEventHandler()
        {
            var statement = new CodeAddEventHandlerStatement(
                new CodeMemberReferenceExpression(new CodeThisExpression(), "A"),
                new CodeMemberReferenceExpression(new CodeThisExpression(), "Handler"));

            var generator = new CSharpCodeGenerator();
            var result = generator.Write(statement);

            Assert.That.StringEquals(@"this.A += this.Handler;
", result);
        }

        [TestMethod]
        public void CSharpCodeGenerator_RemoveEventHandler()
        {
            var statement = new CodeRemoveEventHandlerStatement(
                new CodeMemberReferenceExpression(new CodeThisExpression(), "A"),
                new CodeMemberReferenceExpression(new CodeThisExpression(), "Handler"));

            var generator = new CSharpCodeGenerator();
            var result = generator.Write(statement);

            Assert.That.StringEquals(@"this.A -= this.Handler;
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

            Assert.That.StringEquals(@"while (true)
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

            Assert.That.StringEquals(@"class Sample
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

            Assert.That.StringEquals(@"class Sample
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

            Assert.That.StringEquals(@"class Sample
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

            Assert.That.StringEquals(@"class Sample
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

            Assert.That.StringEquals(@"array[10]", result);
        }

        [TestMethod]
        public void CSharpCodeGenerator_ArrayIndexer_Multiple()
        {
            var array = new CodeVariableReference("array");
            CodeExpression expression = new CodeArrayIndexerExpression(array, 10, "test");

            var generator = new CSharpCodeGenerator();
            var result = generator.Write(expression);

            Assert.That.StringEquals(@"array[10, ""test""]", result);
        }

        [TestMethod]
        public void CSharpCodeGenerator_Assign()
        {
            var statement = new CodeAssignStatement(new CodeVariableReference("a"), 10);

            var generator = new CSharpCodeGenerator();
            var result = generator.Write(statement);

            Assert.That.StringEquals(@"a = 10;
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

            Assert.That.StringEquals(@"if (true)
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

            Assert.That.StringEquals(@"if (true)
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

            Assert.That.StringEquals(@"if (true)
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

            Assert.That.StringEquals(@"try
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

            Assert.That.StringEquals(@"try
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

            Assert.That.StringEquals(@"try
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

            Assert.That.StringEquals("\"test\"", result);
        }

        [TestMethod]
        public void CSharpCodeGenerator_Literal_StringWithNewLine()
        {
            var literal = new CodeLiteralExpression("line1\r\nline2");
            var generator = new CSharpCodeGenerator();
            var result = generator.Write(literal);

            Assert.That.StringEquals("\"line1\\r\\nline2\"", result);
        }

        [TestMethod]
        public void CSharpCodeGenerator_Default()
        {
            var expr = new CodeDefaultValueExpression(typeof(string));
            var generator = new CSharpCodeGenerator();
            var result = generator.Write(expr);

            Assert.That.StringEquals("default(string)", result);
        }

        [TestMethod]
        public void CSharpCodeGenerator_Cast()
        {
            var expr = new CodeCastExpression(new CodeVariableReference("a"), typeof(string));
            var generator = new CSharpCodeGenerator();
            var result = generator.Write(expr);

            Assert.That.StringEquals("((string)a)", result);
        }

        [TestMethod]
        public void CSharpCodeGenerator_Convert()
        {
            var expr = new CodeConvertExpression(new CodeVariableReference("a"), typeof(string));
            var generator = new CSharpCodeGenerator();
            var result = generator.Write(expr);

            Assert.That.StringEquals("(a as string)", result);
        }

        [TestMethod]
        public void CSharpCodeGenerator_Throw()
        {
            var expr = new CodeThrowStatement(new CodeNewObjectExpression(typeof(Exception)));
            var generator = new CSharpCodeGenerator();
            var result = generator.Write(expr);

            Assert.That.StringEquals(@"throw new System.Exception();
", result);
        }

        [TestMethod]
        public void CSharpCodeGenerator_CustomAttributes_WithoutArgument()
        {
            var method = new CodeMethodDeclaration("Sample");
            method.Statements = new CodeStatementCollection();
            method.CustomAttributes.Add(new CodeCustomAttribute(new CodeTypeReference("TestAttribute")));

            var generator = new CSharpCodeGenerator();
            var result = generator.Write(method);

            Assert.That.StringEquals(@"[TestAttribute]
void Sample()
{
}
", result);
        }

        [TestMethod]
        public void CSharpCodeGenerator_CustomAttributes_WithArgument()
        {
            var method = new CodeMethodDeclaration("Sample");
            method.Statements = new CodeStatementCollection();
            method.CustomAttributes.Add(
                new CodeCustomAttribute(new CodeTypeReference("TestAttribute"))
                {
                    Arguments =
                    {
                        new CodeCustomAttributeArgument("arg1")
                    }
                });

            var generator = new CSharpCodeGenerator();
            var result = generator.Write(method);

            Assert.That.StringEquals(@"[TestAttribute(""arg1"")]
void Sample()
{
}
", result);
        }

        [TestMethod]
        public void CSharpCodeGenerator_CustomAttributes_WithArguments()
        {
            var method = new CodeMethodDeclaration("Sample");
            method.Statements = new CodeStatementCollection();
            method.CustomAttributes.Add(
                new CodeCustomAttribute(new CodeTypeReference("TestAttribute"))
                {
                    Arguments =
                    {
                        new CodeCustomAttributeArgument("arg1"),
                        new CodeCustomAttributeArgument("arg2"),
                    }
                });

            var generator = new CSharpCodeGenerator();
            var result = generator.Write(method);

            Assert.That.StringEquals(@"[TestAttribute(""arg1"", ""arg2"")]
void Sample()
{
}
", result);
        }

        [TestMethod]
        public void CSharpCodeGenerator_CustomAttributes_WithNamedArgument()
        {
            var method = new CodeMethodDeclaration("Sample");
            method.Statements = new CodeStatementCollection();
            method.CustomAttributes.Add(
                new CodeCustomAttribute(new CodeTypeReference("TestAttribute"))
                {
                    Arguments =
                    {
                        new CodeCustomAttributeArgument("Name1", "arg1")
                    }
                });

            var generator = new CSharpCodeGenerator();
            var result = generator.Write(method);

            Assert.That.StringEquals(@"[TestAttribute(Name1 = ""arg1"")]
void Sample()
{
}
", result);
        }

        [TestMethod]
        public void CSharpCodeGenerator_CustomAttributes_WithNamedArguments()
        {
            var method = new CodeMethodDeclaration("Sample");
            method.Statements = new CodeStatementCollection();
            method.CustomAttributes.Add(
                new CodeCustomAttribute(new CodeTypeReference("TestAttribute"))
                {
                    Arguments =
                    {
                        new CodeCustomAttributeArgument("Name1", "arg1"),
                        new CodeCustomAttributeArgument("Name2", "arg2")
                    }
                });

            var generator = new CSharpCodeGenerator();
            var result = generator.Write(method);

            Assert.That.StringEquals(@"[TestAttribute(Name1 = ""arg1"", Name2 = ""arg2"")]
void Sample()
{
}
", result);
        }

        [TestMethod]
        public void CSharpCodeGenerator_CustomAttributes_WithMixedUnnamedAndNamedArguments()
        {
            var method = new CodeMethodDeclaration("Sample");
            method.Statements = new CodeStatementCollection();
            method.CustomAttributes.Add(
                new CodeCustomAttribute(new CodeTypeReference("TestAttribute"))
                {
                    Arguments =
                    {
                        new CodeCustomAttributeArgument("arg1"),
                        new CodeCustomAttributeArgument("Name2", "arg2"),
                        new CodeCustomAttributeArgument("arg3"),
                    }
                });

            var generator = new CSharpCodeGenerator();
            var result = generator.Write(method);

            Assert.That.StringEquals(@"[TestAttribute(""arg1"", ""arg3"", Name2 = ""arg2"")]
void Sample()
{
}
", result);
        }

        [TestMethod]
        public void CSharpCodeGenerator_CustomAttributes_MultipleAttributes()
        {
            var method = new CodeMethodDeclaration("Sample");
            method.Statements = new CodeStatementCollection();
            method.CustomAttributes.Add(new CodeCustomAttribute(new CodeTypeReference("TestAttribute1")));
            method.CustomAttributes.Add(new CodeCustomAttribute(new CodeTypeReference("TestAttribute2")));

            var generator = new CSharpCodeGenerator();
            var result = generator.Write(method);

            Assert.That.StringEquals(@"[TestAttribute1]
[TestAttribute2]
void Sample()
{
}
", result);
        }

        [TestMethod]
        public void CSharpCodeGenerator_Method_GenericParameter()
        {
            var method = new CodeMethodDeclaration("Sample");
            method.Statements = new CodeStatementCollection();
            method.Parameters.Add(new CodeTypeParameter("T"));

            var generator = new CSharpCodeGenerator();
            var result = generator.Write(method);

            Assert.That.StringEquals(@"void Sample<T>()
{
}
", result);
        }

        [TestMethod]
        public void CSharpCodeGenerator_Method_GenericParameterWithConstraint()
        {
            var method = new CodeMethodDeclaration("Sample");
            method.Statements = new CodeStatementCollection();
            method.Parameters.Add(new CodeTypeParameter("T") { Constraints = { new CodeClassTypeParameterConstraint() } });

            var generator = new CSharpCodeGenerator();
            var result = generator.Write(method);

            Assert.That.StringEquals(@"void Sample<T>()
    where T : class
{
}
", result);
        }

        [TestMethod]
        public void CSharpCodeGenerator_Method_Abstract()
        {
            var method = new CodeMethodDeclaration("Sample");
            method.Modifiers = Modifiers.Protected | Modifiers.Abstract;

            var generator = new CSharpCodeGenerator();
            var result = generator.Write(method);

            Assert.That.StringEquals(@"protected abstract void Sample();
", result);
        }

        [TestMethod]
        public void CSharpCodeGenerator_Method_AbstractWithGenericParameterWithConstraint()
        {
            var method = new CodeMethodDeclaration("Sample");
            method.Modifiers = Modifiers.Protected | Modifiers.Abstract;
            method.Parameters.Add(new CodeTypeParameter("T") { Constraints = { new CodeClassTypeParameterConstraint() } });

            var generator = new CSharpCodeGenerator();
            var result = generator.Write(method);

            Assert.That.StringEquals(@"protected abstract void Sample<T>()
    where T : class
    ;
", result);
        }

        [TestMethod]
        public void CSharpCodeGenerator_ExpressionStatement()
        {
            var statement = new CodeExpressionStatement(new CodeNewObjectExpression(new CodeTypeReference("Disposable")));

            var generator = new CSharpCodeGenerator();
            var result = generator.Write(statement);

            Assert.That.StringEquals(@"new Disposable();
", result);
        }

        [TestMethod]
        public void CSharpCodeGenerator_UsingDirective()
        {
            var directive = new CodeUsingDirective("System");

            var generator = new CSharpCodeGenerator();
            var result = generator.Write(directive);

            Assert.That.StringEquals(@"using System;", result);
        }

        [TestMethod]
        public void CSharpCodeGenerator_UsingStatement_WithoutBody()
        {
            var statement = new CodeUsingStatement();
            statement.Statement = new CodeNewObjectExpression(new CodeTypeReference("Disposable"));

            var generator = new CSharpCodeGenerator();
            var result = generator.Write(statement);

            Assert.That.StringEquals(@"using (new Disposable())
{
}
", result);
        }

        [TestMethod]
        public void CSharpCodeGenerator_UsingStatement_WithBody()
        {
            var statement = new CodeUsingStatement();
            statement.Statement = new CodeVariableDeclarationStatement(null, "disposable", new CodeNewObjectExpression(new CodeTypeReference("Disposable")));
            statement.Body = (CodeStatement)new CodeMethodInvokeExpression(new CodeVariableReference("disposable"));

            var generator = new CSharpCodeGenerator();
            var result = generator.Write(statement);

            Assert.That.StringEquals(@"using (var disposable = new Disposable())
{
    disposable();
}
", result);
        }

        [TestMethod]
        public void CSharpCodeGenerator_Iteration()
        {
            var statement = new CodeIterationStatement();
            var variable = new CodeVariableDeclarationStatement(null, "i", 0);
            statement.Initialization = variable;
            statement.Condition = new CodeBinaryExpression(BinaryOperator.LessThan, variable, 10);
            statement.IncrementStatement = new CodeUnaryExpression(UnaryOperator.PostIncrement, variable);
            statement.Body = new CodeMethodInvokeExpression(
                new CodeMemberReferenceExpression(new CodeTypeReference("Console"), "Write"),
                variable);

            var generator = new CSharpCodeGenerator();
            var result = generator.Write(statement);

            Assert.That.StringEquals(@"for (var i = 0; (i < 10); (i++))
{
    Console.Write(i);
}
", result);
        }

        [TestMethod]
        public void CSharpCodeGenerator_Iteration_Empty()
        {
            var statement = new CodeIterationStatement();
            var generator = new CSharpCodeGenerator();
            var result = generator.Write(statement);

            Assert.That.StringEquals(@"for (; ; )
{
}
", result);
        }

        [TestMethod]
        public void CSharpCodeGenerator_TypeOf()
        {
            var statement = new CodeTypeOfExpression(typeof(string));
            var generator = new CSharpCodeGenerator();
            var result = generator.Write(statement);

            Assert.That.StringEquals(@"typeof(string)", result);
        }

        [TestMethod]
        public void CSharpCodeGenerator_NextLoopIteration()
        {
            var statement = new CodeGotoNextLoopIterationStatement();
            var generator = new CSharpCodeGenerator();
            var result = generator.Write(statement);

            Assert.That.StringEquals(@"continue;
", result);
        }

        [TestMethod]
        public void CSharpCodeGenerator_CodeExitLoop()
        {
            var statement = new CodeExitLoopStatement();
            var generator = new CSharpCodeGenerator();
            var result = generator.Write(statement);

            Assert.That.StringEquals(@"break;
", result);
        }

        [TestMethod]
        public void CSharpCodeGenerator_Return()
        {
            var statement = new CodeReturnStatement();
            var generator = new CSharpCodeGenerator();
            var result = generator.Write(statement);

            Assert.That.StringEquals(@"return;
", result);
        }

        [TestMethod]
        public void CSharpCodeGenerator_ReturnExpression()
        {
            var statement = new CodeReturnStatement(10);
            var generator = new CSharpCodeGenerator();
            var result = generator.Write(statement);

            Assert.That.StringEquals(@"return 10;
", result);
        }

        [TestMethod]
        public void CSharpCodeGenerator_YieldReturnExpression()
        {
            var statement = new CodeYieldReturnStatement(10);
            var generator = new CSharpCodeGenerator();
            var result = generator.Write(statement);

            Assert.That.StringEquals(@"yield return 10;
", result);
        }

        [TestMethod]
        public void CSharpCodeGenerator_YieldBreak()
        {
            var statement = new CodeYieldBreakStatement();
            var generator = new CSharpCodeGenerator();
            var result = generator.Write(statement);

            Assert.That.StringEquals(@"yield break;
", result);
        }

        [TestMethod]
        public void CSharpCodeGenerator_Await()
        {
            var statement = new CodeAwaitExpression(new CodeVariableReference("awaitable"));
            var generator = new CSharpCodeGenerator();
            var result = generator.Write(statement);

            Assert.That.StringEquals(@"await awaitable", result);
        }

        [TestMethod]
        public void CSharpCodeGenerator_Comment()
        {
            var statement = new CodeCommentStatement("test");
            var generator = new CSharpCodeGenerator();
            var result = generator.Write(statement);

            Assert.That.StringEquals(@"// test
", result);
        }

        [TestMethod]
        public void CSharpCodeGenerator_CommentMultiLine()
        {
            var statement = new CodeCommentStatement("test1" + Environment.NewLine + Environment.NewLine + "test2");
            var generator = new CSharpCodeGenerator();
            var result = generator.Write(statement);

            Assert.That.StringEquals(@"// test1
//
// test2
", result);
        }

        [TestMethod]
        public void CSharpCodeGenerator_CommentNull()
        {
            var statement = new CodeCommentStatement();
            var generator = new CSharpCodeGenerator();
            var result = generator.Write(statement);

            Assert.That.StringEquals(@"//
", result);
        }

        [TestMethod]
        public void CSharpCodeGenerator_ExpressionCommentBeforeAndAfter()
        {
            var expression = new CodeSnippetExpression("code");
            expression.CommentsBefore.Add("comment1");
            expression.CommentsAfter.Add("comment2");
            var generator = new CSharpCodeGenerator();
            var result = generator.Write(expression);

            Assert.That.StringEquals(@"/* comment1 */ code /* comment2 */", result);
        }

        [TestMethod]
        public void CSharpCodeGenerator_ExpressionCommentBefore()
        {
            var expression = new CodeSnippetExpression("code");
            expression.CommentsBefore.Add("comment1");
            var generator = new CSharpCodeGenerator();
            var result = generator.Write(expression);

            Assert.That.StringEquals(@"/* comment1 */ code", result);
        }

        [TestMethod]
        public void CSharpCodeGenerator_ExpressionCommentAfter()
        {
            var expression = new CodeSnippetExpression("code");
            expression.CommentsAfter.Add("comment2");
            var generator = new CSharpCodeGenerator();
            var result = generator.Write(expression);

            Assert.That.StringEquals(@"code /* comment2 */", result);
        }

        [TestMethod]
        public void CSharpCodeGenerator_ExpressionCommentAfterWithInlineCommentEnd()
        {
            var expression = new CodeSnippetExpression("code");
            expression.CommentsAfter.Add("comment with */ in the middle");
            var generator = new CSharpCodeGenerator();
            var result = generator.Write(expression);

            Assert.That.StringEquals(@"code // comment with */ in the middle
", result);
        }

        [TestMethod]
        public void CSharpCodeGenerator_ExpressionCommentBeforeWithInlineCommentEnd()
        {
            var expression = new CodeSnippetExpression("code");
            expression.CommentsBefore.Add("comment with */ in the middle");
            var generator = new CSharpCodeGenerator();
            var result = generator.Write(expression);

            Assert.That.StringEquals(@"// comment with */ in the middle
code", result);
        }

        [TestMethod]
        public void CSharpCodeGenerator_ExpressionCommentsBefore_Inlines()
        {
            var expression = new CodeSnippetExpression("code");
            expression.CommentsBefore.Add("com1");
            expression.CommentsBefore.Add("com2");
            var generator = new CSharpCodeGenerator();
            var result = generator.Write(expression);

            Assert.That.StringEquals(@"/* com1 */ /* com2 */ code", result);
        }

        [TestMethod]
        public void CSharpCodeGenerator_ExpressionCommentsBefore_LineAndInline()
        {
            var expression = new CodeSnippetExpression("code");
            expression.CommentsBefore.Add("com1", CodeCommentType.LineComment);
            expression.CommentsBefore.Add("com2");
            var generator = new CSharpCodeGenerator();
            var result = generator.Write(expression);

            Assert.That.StringEquals(@"// com1
/* com2 */ code", result);
        }

        [TestMethod]
        public void CSharpCodeGenerator_ExpressionCommentsAfter_LineAndInline()
        {
            var expression = new CodeSnippetExpression("code");
            expression.CommentsAfter.Add("com1", CodeCommentType.LineComment);
            expression.CommentsAfter.Add("com2");
            var generator = new CSharpCodeGenerator();
            var result = generator.Write(expression);

            Assert.That.StringEquals(@"code // com1
/* com2 */", result);
        }

        [TestMethod]
        public void CSharpCodeGenerator_StatementCommentsAfter_Line()
        {
            var expression = new CodeReturnStatement();
            expression.CommentsAfter.Add("com1", CodeCommentType.LineComment);
            expression.CommentsAfter.Add("com2");
            var generator = new CSharpCodeGenerator();
            var result = generator.Write(expression);

            Assert.That.StringEquals(@"return;
// com1
// com2
", result);
        }

        [TestMethod]
        public void CSharpCodeGenerator_MethodXmlDocmentation()
        {
            var method = new CodeMethodDeclaration("Sample");
            method.Statements = new CodeStatementCollection();

            method.CommentsBefore.Add("<summary>Test</summary>", CodeCommentType.DocumentationComment);
            var generator = new CSharpCodeGenerator();
            var result = generator.Write(method);

            Assert.That.StringEquals(@"/// <summary>Test</summary>
void Sample()
{
}
", result);
        }

        [TestMethod]
        public void CSharpCodeGenerator_TypeReference()
        {
            var expression = new CodeTypeReference(typeof(Console));

            var generator = new CSharpCodeGenerator();
            var result = generator.Write(expression);

            Assert.That.StringEquals(@"System.Console", result);
        }

        [TestMethod]
        public void CSharpCodeGenerator_TypeReference_Nested()
        {
            var expression = new CodeTypeReference(typeof(SampleEnum));

            var generator = new CSharpCodeGenerator();
            var result = generator.Write(expression);

            Assert.That.StringEquals(@"Meziantou.Framework.CodeDom.Tests.CSharpCodeGeneratorTests.SampleEnum", result);
        }

        [TestMethod]
        public void CSharpCodeGenerator_TypeReference_Generic()
        {
            var expression = new CodeTypeReference(typeof(Sample<int>));

            var generator = new CSharpCodeGenerator();
            var result = generator.Write(expression);
            
            Assert.That.StringEquals(@"Meziantou.Framework.CodeDom.Tests.CSharpCodeGeneratorTests.Sample<int>", result);
        }

        private class Sample<T>
        {
        }

        [Flags]
        private enum SampleEnum
        {
            A = 1,
            B = 2,
            C = 4,
            All = 7
        }

        [TestMethod]
        public void CSharpCodeGenerator_CodeExpressionFromEnum()
        {
            CodeExpression expression = SampleEnum.A;

            var generator = new CSharpCodeGenerator();
            var result = generator.Write(expression);

            Assert.That.StringEquals(@"Meziantou.Framework.CodeDom.Tests.CSharpCodeGeneratorTests.SampleEnum.A", result);
        }

        [TestMethod]
        public void CSharpCodeGenerator_CodeExpressionFromEnum_DefinedCombinaison()
        {
            CodeExpression expression = SampleEnum.All;

            var generator = new CSharpCodeGenerator();
            var result = generator.Write(expression);

            Assert.That.StringEquals(@"Meziantou.Framework.CodeDom.Tests.CSharpCodeGeneratorTests.SampleEnum.All", result);
        }

        [TestMethod]
        public void CSharpCodeGenerator_CodeExpressionFromEnum_Combinaison()
        {
            CodeExpression expression = (SampleEnum)3;

            var generator = new CSharpCodeGenerator();
            var result = generator.Write(expression);

            Assert.That.StringEquals(@"((Meziantou.Framework.CodeDom.Tests.CSharpCodeGeneratorTests.SampleEnum)3)", result);
        }

        [TestMethod]
        public void CSharpCodeGenerator_CodeExpressionFromEnum_UndefinedValue()
        {
            CodeExpression expression = (SampleEnum)10;

            var generator = new CSharpCodeGenerator();
            var result = generator.Write(expression);

            Assert.That.StringEquals(@"((Meziantou.Framework.CodeDom.Tests.CSharpCodeGeneratorTests.SampleEnum)10)", result);
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

            Assert.That.StringEquals($"(1 {symbol} 2)", result);
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

            Assert.That.StringEquals($"({symbol}1)", result);
        }

        [DataTestMethod]
        [DataRow(UnaryOperator.PostIncrement, "++")]
        [DataRow(UnaryOperator.PostDecrement, "--")]
        public void CSharpCodeGenerator_UnaryExpression_Post(UnaryOperator op, string symbol)
        {
            var expression = new CodeUnaryExpression(op, 1);
            var generator = new CSharpCodeGenerator();
            var result = generator.Write(expression);

            Assert.That.StringEquals($"(1{symbol})", result);
        }
    }
}
