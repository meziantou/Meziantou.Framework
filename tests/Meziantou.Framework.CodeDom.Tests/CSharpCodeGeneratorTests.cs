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
            var unit = new CompilationUnit();
            var ns = unit.AddNamespace("Meziantou.Framework.CodeDom");
            var c = ns.AddType(new ClassDeclaration("Sample"));
            var method = c.AddMember(new MethodDeclaration("Factorial"));
            method.ReturnType = typeof(int);
            var n = method.AddArgument("n", typeof(int));
            method.Modifiers = Modifiers.Public | Modifiers.Static;

            method.Statements = new ConditionStatement()
            {
                Condition = new BinaryExpression(BinaryOperator.LessThanOrEqual, 1, n),
                TrueStatements = new ReturnStatement(1),
                FalseStatements = new ReturnStatement(new BinaryExpression(
                    BinaryOperator.Multiply,
                    n,
                    new MethodInvokeExpression(method, new BinaryExpression(BinaryOperator.Substract, n, 1))))
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
            var type = new ClassDeclaration("Sample");

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
            var type = new ClassDeclaration("Sample");
            type.Parameters.Add(new TypeParameter("T1") { Constraints = { new ValueTypeTypeParameterConstraint() } });
            type.Parameters.Add(new TypeParameter("T2")
            {
                Constraints = {
                        new ConstructorParameterConstraint(),
                        new ClassTypeParameterConstraint(),
                        new BaseTypeParameterConstraint(typeof(ICloneable))
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
            var type = new InterfaceDeclaration("Sample");

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
            var type = new InterfaceDeclaration("Sample");
            type.Parameters.Add(new TypeParameter("T1") { Constraints = { new ValueTypeTypeParameterConstraint() } });
            type.Parameters.Add(new TypeParameter("T2")
            {
                Constraints = {
                        new ConstructorParameterConstraint(),
                        new ClassTypeParameterConstraint(),
                        new BaseTypeParameterConstraint(typeof(ICloneable))
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
            var type = new EnumerationDeclaration("Sample");
            type.BaseType = typeof(uint);
            type.Modifiers = Modifiers.Internal;
            type.Members.Add(new EnumerationMember("A", 1));
            type.Members.Add(new EnumerationMember("B", 2));
            type.CustomAttributes.Add(new CustomAttribute(typeof(FlagsAttribute)));

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
            var d = new DelegateDeclaration("Sample");
            d.ReturnType = typeof(void);
            d.Arguments.Add(new MethodArgumentDeclaration(typeof(string), "a"));
            d.Modifiers = Modifiers.Public;

            var generator = new CSharpCodeGenerator();
            var result = generator.Write(d);

            Assert.That.StringEquals(@"public delegate void Sample(string a);
", result);
        }

        [TestMethod]
        public void CSharpCodeGenerator_FieldDeclaration()
        {
            var type = new ClassDeclaration("Sample");
            type.AddMember(new FieldDeclaration("_a", typeof(int)));
            type.AddMember(new FieldDeclaration("_b", typeof(Type), Modifiers.Private));
            type.AddMember(new FieldDeclaration("_c", typeof(int), Modifiers.Protected, 10));

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
            var type = new ClassDeclaration("Sample");
            type.AddMember(new EventFieldDeclaration("A", typeof(EventHandler), Modifiers.Public));

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
            var statement = new AddEventHandlerStatement(
                new MemberReferenceExpression(new ThisExpression(), "A"),
                new MemberReferenceExpression(new ThisExpression(), "Handler"));

            var generator = new CSharpCodeGenerator();
            var result = generator.Write(statement);

            Assert.That.StringEquals(@"this.A += this.Handler;
", result);
        }

        [TestMethod]
        public void CSharpCodeGenerator_RemoveEventHandler()
        {
            var statement = new RemoveEventHandlerStatement(
                new MemberReferenceExpression(new ThisExpression(), "A"),
                new MemberReferenceExpression(new ThisExpression(), "Handler"));

            var generator = new CSharpCodeGenerator();
            var result = generator.Write(statement);

            Assert.That.StringEquals(@"this.A -= this.Handler;
", result);
        }

        [TestMethod]
        public void CSharpCodeGenerator_WhileLoop()
        {
            var loop = new WhileStatement();
            loop.Condition = new LiteralExpression(true);
            loop.Body = new StatementCollection();

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
            var type = new ClassDeclaration("Sample");
            var ctor = type.AddMember(new ConstructorDeclaration());
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
            var type = new ClassDeclaration("Sample");
            var ctor = type.AddMember(new ConstructorDeclaration());
            ctor.Initializer = new ConstructorBaseInitializer();
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
            var type = new ClassDeclaration("Sample");
            var ctor = type.AddMember(new ConstructorDeclaration());
            ctor.Initializer = new ConstructorThisInitializer();
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
            var type = new ClassDeclaration("Sample");
            var ctor = type.AddMember(new ConstructorDeclaration());
            ctor.Initializer = new ConstructorThisInitializer(new LiteralExpression("arg"));
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
            var array = new VariableReference("array");
            Expression expression = new ArrayIndexerExpression(array, 10);

            var generator = new CSharpCodeGenerator();
            var result = generator.Write(expression);

            Assert.That.StringEquals(@"array[10]", result);
        }

        [TestMethod]
        public void CSharpCodeGenerator_ArrayIndexer_Multiple()
        {
            var array = new VariableReference("array");
            Expression expression = new ArrayIndexerExpression(array, 10, "test");

            var generator = new CSharpCodeGenerator();
            var result = generator.Write(expression);

            Assert.That.StringEquals(@"array[10, ""test""]", result);
        }

        [TestMethod]
        public void CSharpCodeGenerator_Assign()
        {
            var statement = new AssignStatement(new VariableReference("a"), 10);

            var generator = new CSharpCodeGenerator();
            var result = generator.Write(statement);

            Assert.That.StringEquals(@"a = 10;
", result);
        }

        [TestMethod]
        public void CSharpCodeGenerator_If()
        {
            var statement = new ConditionStatement();
            statement.Condition = new LiteralExpression(true);
            statement.TrueStatements = new SnippetStatement("TrueSnippet");

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
            var statement = new ConditionStatement();
            statement.Condition = new LiteralExpression(true);
            statement.TrueStatements = new SnippetStatement("TrueSnippet");
            statement.FalseStatements = new SnippetStatement("FalseSnippet");

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
            var statement = new ConditionStatement();
            statement.Condition = new LiteralExpression(true);

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
            var statement = new TryCatchFinallyStatement();
            statement.Try = new SnippetStatement("TrySnippet");
            statement.Catch = new CatchClauseCollection();
            statement.Catch.Add(new CatchClause() { Body = new SnippetStatement("Catch1") });

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
            var statement = new TryCatchFinallyStatement();
            statement.Try = new SnippetStatement("TrySnippet");
            statement.Catch = new CatchClauseCollection();
            statement.Catch.Add(new CatchClause()
            {
                ExceptionType = typeof(NotImplementedException),
                ExceptionVariableName = "nie",
                Body = new SnippetStatement("Catch1")
            });
            statement.Catch.Add(new CatchClause()
            {
                ExceptionType = typeof(Exception),
                ExceptionVariableName = "ex",
                Body = new ThrowStatement()
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
            var statement = new TryCatchFinallyStatement();
            statement.Try = new SnippetStatement("TrySnippet");
            statement.Finally = new SnippetStatement("FinallyStatement");

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
            var literal = new LiteralExpression("test");
            var generator = new CSharpCodeGenerator();
            var result = generator.Write(literal);

            Assert.That.StringEquals("\"test\"", result);
        }

        [TestMethod]
        public void CSharpCodeGenerator_Literal_StringWithNewLine()
        {
            var literal = new LiteralExpression("line1\r\nline2");
            var generator = new CSharpCodeGenerator();
            var result = generator.Write(literal);

            Assert.That.StringEquals("\"line1\\r\\nline2\"", result);
        }

        [TestMethod]
        public void CSharpCodeGenerator_Default()
        {
            var expr = new DefaultValueExpression(typeof(string));
            var generator = new CSharpCodeGenerator();
            var result = generator.Write(expr);

            Assert.That.StringEquals("default(string)", result);
        }

        [TestMethod]
        public void CSharpCodeGenerator_Cast()
        {
            var expr = new CastExpression(new VariableReference("a"), typeof(string));
            var generator = new CSharpCodeGenerator();
            var result = generator.Write(expr);

            Assert.That.StringEquals("((string)a)", result);
        }

        [TestMethod]
        public void CSharpCodeGenerator_Convert()
        {
            var expr = new ConvertExpression(new VariableReference("a"), typeof(string));
            var generator = new CSharpCodeGenerator();
            var result = generator.Write(expr);

            Assert.That.StringEquals("(a as string)", result);
        }

        [TestMethod]
        public void CSharpCodeGenerator_Throw()
        {
            var expr = new ThrowStatement(new NewObjectExpression(typeof(Exception)));
            var generator = new CSharpCodeGenerator();
            var result = generator.Write(expr);

            Assert.That.StringEquals(@"throw new System.Exception();
", result);
        }

        [TestMethod]
        public void CSharpCodeGenerator_CustomAttributes_WithoutArgument()
        {
            var method = new MethodDeclaration("Sample");
            method.Statements = new StatementCollection();
            method.CustomAttributes.Add(new CustomAttribute(new TypeReference("TestAttribute")));

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
            var method = new MethodDeclaration("Sample");
            method.Statements = new StatementCollection();
            method.CustomAttributes.Add(
                new CustomAttribute(new TypeReference("TestAttribute"))
                {
                    Arguments =
                    {
                        new CustomAttributeArgument("arg1")
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
            var method = new MethodDeclaration("Sample");
            method.Statements = new StatementCollection();
            method.CustomAttributes.Add(
                new CustomAttribute(new TypeReference("TestAttribute"))
                {
                    Arguments =
                    {
                        new CustomAttributeArgument("arg1"),
                        new CustomAttributeArgument("arg2"),
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
            var method = new MethodDeclaration("Sample");
            method.Statements = new StatementCollection();
            method.CustomAttributes.Add(
                new CustomAttribute(new TypeReference("TestAttribute"))
                {
                    Arguments =
                    {
                        new CustomAttributeArgument("Name1", "arg1")
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
            var method = new MethodDeclaration("Sample");
            method.Statements = new StatementCollection();
            method.CustomAttributes.Add(
                new CustomAttribute(new TypeReference("TestAttribute"))
                {
                    Arguments =
                    {
                        new CustomAttributeArgument("Name1", "arg1"),
                        new CustomAttributeArgument("Name2", "arg2")
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
            var method = new MethodDeclaration("Sample");
            method.Statements = new StatementCollection();
            method.CustomAttributes.Add(
                new CustomAttribute(new TypeReference("TestAttribute"))
                {
                    Arguments =
                    {
                        new CustomAttributeArgument("arg1"),
                        new CustomAttributeArgument("Name2", "arg2"),
                        new CustomAttributeArgument("arg3"),
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
            var method = new MethodDeclaration("Sample");
            method.Statements = new StatementCollection();
            method.CustomAttributes.Add(new CustomAttribute(new TypeReference("TestAttribute1")));
            method.CustomAttributes.Add(new CustomAttribute(new TypeReference("TestAttribute2")));

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
            var method = new MethodDeclaration("Sample");
            method.Statements = new StatementCollection();
            method.Parameters.Add(new TypeParameter("T"));

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
            var method = new MethodDeclaration("Sample");
            method.Statements = new StatementCollection();
            method.Parameters.Add(new TypeParameter("T") { Constraints = { new ClassTypeParameterConstraint() } });

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
            var method = new MethodDeclaration("Sample");
            method.Modifiers = Modifiers.Protected | Modifiers.Abstract;

            var generator = new CSharpCodeGenerator();
            var result = generator.Write(method);

            Assert.That.StringEquals(@"protected abstract void Sample();
", result);
        }

        [TestMethod]
        public void CSharpCodeGenerator_Method_AbstractWithGenericParameterWithConstraint()
        {
            var method = new MethodDeclaration("Sample");
            method.Modifiers = Modifiers.Protected | Modifiers.Abstract;
            method.Parameters.Add(new TypeParameter("T") { Constraints = { new ClassTypeParameterConstraint() } });

            var generator = new CSharpCodeGenerator();
            var result = generator.Write(method);

            Assert.That.StringEquals(@"protected abstract void Sample<T>()
    where T : class
    ;
", result);
        }
                
        [TestMethod]
        public void CSharpCodeGenerator_Method_ExplicitImplementation()
        {
            var method = new MethodDeclaration("A");
            method.PrivateImplementationType = new TypeReference("Foo.IBar");
            method.Statements = new StatementCollection();

            var generator = new CSharpCodeGenerator();
            var result = generator.Write(method);

            Assert.That.StringEquals(@"void Foo.IBar.A()
{
}
", result);
        }

        [TestMethod]
        public void CSharpCodeGenerator_Property_ExplicitImplementation()
        {
            var method = new PropertyDeclaration("A", typeof(int));
            method.PrivateImplementationType = new TypeReference("Foo.IBar");
            method.Getter = new ReturnStatement(10);       

            var generator = new CSharpCodeGenerator();
            var result = generator.Write(method);

            Assert.That.StringEquals(@"int Foo.IBar.A
{
    get
    {
        return 10;
    }
}
", result);
        }

        [TestMethod]
        public void CSharpCodeGenerator_Event_ExplicitImplementation()
        {
            var method = new EventFieldDeclaration("A", typeof(EventHandler));
            method.PrivateImplementationType = new TypeReference("Foo.IBar");
            method.AddAccessor = new StatementCollection();
            method.RemoveAccessor = new StatementCollection();

            var generator = new CSharpCodeGenerator();
            var result = generator.Write(method);

            Assert.That.StringEquals(@"event System.EventHandler Foo.IBar.A
{
    add
    {
    }
    remove
    {
    }
}
", result);
        }

        [TestMethod]
        public void CSharpCodeGenerator_ExpressionStatement()
        {
            var statement = new ExpressionStatement(new NewObjectExpression(new TypeReference("Disposable")));

            var generator = new CSharpCodeGenerator();
            var result = generator.Write(statement);

            Assert.That.StringEquals(@"new Disposable();
", result);
        }

        [TestMethod]
        public void CSharpCodeGenerator_UsingDirective()
        {
            var directive = new UsingDirective("System");

            var generator = new CSharpCodeGenerator();
            var result = generator.Write(directive);

            Assert.That.StringEquals(@"using System;", result);
        }

        [TestMethod]
        public void CSharpCodeGenerator_UsingStatement_WithoutBody()
        {
            var statement = new UsingStatement();
            statement.Statement = new NewObjectExpression(new TypeReference("Disposable"));

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
            var statement = new UsingStatement();
            statement.Statement = new VariableDeclarationStatement(null, "disposable", new NewObjectExpression(new TypeReference("Disposable")));
            statement.Body = (Statement)new MethodInvokeExpression(new VariableReference("disposable"));

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
            var statement = new IterationStatement();
            var variable = new VariableDeclarationStatement(null, "i", 0);
            statement.Initialization = variable;
            statement.Condition = new BinaryExpression(BinaryOperator.LessThan, variable, 10);
            statement.IncrementStatement = new UnaryExpression(UnaryOperator.PostIncrement, variable);
            statement.Body = new MethodInvokeExpression(
                new MemberReferenceExpression(new TypeReference("Console"), "Write"),
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
            var statement = new IterationStatement();
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
            var statement = new TypeOfExpression(typeof(string));
            var generator = new CSharpCodeGenerator();
            var result = generator.Write(statement);

            Assert.That.StringEquals(@"typeof(string)", result);
        }

        [TestMethod]
        public void CSharpCodeGenerator_NextLoopIteration()
        {
            var statement = new GotoNextLoopIterationStatement();
            var generator = new CSharpCodeGenerator();
            var result = generator.Write(statement);

            Assert.That.StringEquals(@"continue;
", result);
        }

        [TestMethod]
        public void CSharpCodeGenerator_CodeExitLoop()
        {
            var statement = new ExitLoopStatement();
            var generator = new CSharpCodeGenerator();
            var result = generator.Write(statement);

            Assert.That.StringEquals(@"break;
", result);
        }

        [TestMethod]
        public void CSharpCodeGenerator_Return()
        {
            var statement = new ReturnStatement();
            var generator = new CSharpCodeGenerator();
            var result = generator.Write(statement);

            Assert.That.StringEquals(@"return;
", result);
        }

        [TestMethod]
        public void CSharpCodeGenerator_ReturnExpression()
        {
            var statement = new ReturnStatement(10);
            var generator = new CSharpCodeGenerator();
            var result = generator.Write(statement);

            Assert.That.StringEquals(@"return 10;
", result);
        }

        [TestMethod]
        public void CSharpCodeGenerator_YieldReturnExpression()
        {
            var statement = new YieldReturnStatement(10);
            var generator = new CSharpCodeGenerator();
            var result = generator.Write(statement);

            Assert.That.StringEquals(@"yield return 10;
", result);
        }

        [TestMethod]
        public void CSharpCodeGenerator_YieldBreak()
        {
            var statement = new YieldBreakStatement();
            var generator = new CSharpCodeGenerator();
            var result = generator.Write(statement);

            Assert.That.StringEquals(@"yield break;
", result);
        }

        [TestMethod]
        public void CSharpCodeGenerator_Await()
        {
            var statement = new AwaitExpression(new VariableReference("awaitable"));
            var generator = new CSharpCodeGenerator();
            var result = generator.Write(statement);

            Assert.That.StringEquals(@"await awaitable", result);
        }

        [TestMethod]
        public void CSharpCodeGenerator_Comment()
        {
            var statement = new CommentStatement("test");
            var generator = new CSharpCodeGenerator();
            var result = generator.Write(statement);

            Assert.That.StringEquals(@"// test
", result);
        }

        [TestMethod]
        public void CSharpCodeGenerator_CommentMultiLine()
        {
            var statement = new CommentStatement("test1" + Environment.NewLine + Environment.NewLine + "test2");
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
            var statement = new CommentStatement();
            var generator = new CSharpCodeGenerator();
            var result = generator.Write(statement);

            Assert.That.StringEquals(@"//
", result);
        }

        [TestMethod]
        public void CSharpCodeGenerator_ExpressionCommentBeforeAndAfter()
        {
            var expression = new SnippetExpression("code");
            expression.CommentsBefore.Add("comment1");
            expression.CommentsAfter.Add("comment2");
            var generator = new CSharpCodeGenerator();
            var result = generator.Write(expression);

            Assert.That.StringEquals(@"/* comment1 */ code /* comment2 */", result);
        }

        [TestMethod]
        public void CSharpCodeGenerator_ExpressionCommentBefore()
        {
            var expression = new SnippetExpression("code");
            expression.CommentsBefore.Add("comment1");
            var generator = new CSharpCodeGenerator();
            var result = generator.Write(expression);

            Assert.That.StringEquals(@"/* comment1 */ code", result);
        }

        [TestMethod]
        public void CSharpCodeGenerator_ExpressionCommentAfter()
        {
            var expression = new SnippetExpression("code");
            expression.CommentsAfter.Add("comment2");
            var generator = new CSharpCodeGenerator();
            var result = generator.Write(expression);

            Assert.That.StringEquals(@"code /* comment2 */", result);
        }

        [TestMethod]
        public void CSharpCodeGenerator_ExpressionCommentAfterWithInlineCommentEnd()
        {
            var expression = new SnippetExpression("code");
            expression.CommentsAfter.Add("comment with */ in the middle");
            var generator = new CSharpCodeGenerator();
            var result = generator.Write(expression);

            Assert.That.StringEquals(@"code // comment with */ in the middle
", result);
        }

        [TestMethod]
        public void CSharpCodeGenerator_ExpressionCommentBeforeWithInlineCommentEnd()
        {
            var expression = new SnippetExpression("code");
            expression.CommentsBefore.Add("comment with */ in the middle");
            var generator = new CSharpCodeGenerator();
            var result = generator.Write(expression);

            Assert.That.StringEquals(@"// comment with */ in the middle
code", result);
        }

        [TestMethod]
        public void CSharpCodeGenerator_ExpressionCommentsBefore_Inlines()
        {
            var expression = new SnippetExpression("code");
            expression.CommentsBefore.Add("com1");
            expression.CommentsBefore.Add("com2");
            var generator = new CSharpCodeGenerator();
            var result = generator.Write(expression);

            Assert.That.StringEquals(@"/* com1 */ /* com2 */ code", result);
        }

        [TestMethod]
        public void CSharpCodeGenerator_ExpressionCommentsBefore_LineAndInline()
        {
            var expression = new SnippetExpression("code");
            expression.CommentsBefore.Add("com1", CommentType.LineComment);
            expression.CommentsBefore.Add("com2");
            var generator = new CSharpCodeGenerator();
            var result = generator.Write(expression);

            Assert.That.StringEquals(@"// com1
/* com2 */ code", result);
        }

        [TestMethod]
        public void CSharpCodeGenerator_ExpressionCommentsAfter_LineAndInline()
        {
            var expression = new SnippetExpression("code");
            expression.CommentsAfter.Add("com1", CommentType.LineComment);
            expression.CommentsAfter.Add("com2");
            var generator = new CSharpCodeGenerator();
            var result = generator.Write(expression);

            Assert.That.StringEquals(@"code // com1
/* com2 */", result);
        }

        [TestMethod]
        public void CSharpCodeGenerator_StatementCommentsAfter_Line()
        {
            var expression = new ReturnStatement();
            expression.CommentsAfter.Add("com1", CommentType.LineComment);
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
            var method = new MethodDeclaration("Sample");
            method.Statements = new StatementCollection();

            method.CommentsBefore.Add("<summary>Test</summary>", CommentType.DocumentationComment);
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
            var expression = new TypeReference(typeof(Console));

            var generator = new CSharpCodeGenerator();
            var result = generator.Write(expression);

            Assert.That.StringEquals(@"System.Console", result);
        }

        [TestMethod]
        public void CSharpCodeGenerator_TypeReference_Nested()
        {
            var expression = new TypeReference(typeof(SampleEnum));

            var generator = new CSharpCodeGenerator();
            var result = generator.Write(expression);

            Assert.That.StringEquals(@"Meziantou.Framework.CodeDom.Tests.CSharpCodeGeneratorTests.SampleEnum", result);
        }

        [TestMethod]
        public void CSharpCodeGenerator_TypeReference_Generic()
        {
            var expression = new TypeReference(typeof(Sample<int>));

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
            Expression expression = SampleEnum.A;

            var generator = new CSharpCodeGenerator();
            var result = generator.Write(expression);

            Assert.That.StringEquals(@"Meziantou.Framework.CodeDom.Tests.CSharpCodeGeneratorTests.SampleEnum.A", result);
        }

        [TestMethod]
        public void CSharpCodeGenerator_CodeExpressionFromEnum_DefinedCombinaison()
        {
            Expression expression = SampleEnum.All;

            var generator = new CSharpCodeGenerator();
            var result = generator.Write(expression);

            Assert.That.StringEquals(@"Meziantou.Framework.CodeDom.Tests.CSharpCodeGeneratorTests.SampleEnum.All", result);
        }

        [TestMethod]
        public void CSharpCodeGenerator_CodeExpressionFromEnum_Combinaison()
        {
            Expression expression = (SampleEnum)3;

            var generator = new CSharpCodeGenerator();
            var result = generator.Write(expression);

            Assert.That.StringEquals(@"((Meziantou.Framework.CodeDom.Tests.CSharpCodeGeneratorTests.SampleEnum)3)", result);
        }

        [TestMethod]
        public void CSharpCodeGenerator_CodeExpressionFromEnum_UndefinedValue()
        {
            Expression expression = (SampleEnum)10;

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
        [DataRow(BinaryOperator.Xor, "^")]
        public void CSharpCodeGenerator_BinaryExpression(BinaryOperator op, string symbol)
        {
            var expression = new BinaryExpression(op, 1, 2);
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
            var expression = new UnaryExpression(op, 1);
            var generator = new CSharpCodeGenerator();
            var result = generator.Write(expression);

            Assert.That.StringEquals($"({symbol}1)", result);
        }

        [DataTestMethod]
        [DataRow(UnaryOperator.PostIncrement, "++")]
        [DataRow(UnaryOperator.PostDecrement, "--")]
        public void CSharpCodeGenerator_UnaryExpression_Post(UnaryOperator op, string symbol)
        {
            var expression = new UnaryExpression(op, 1);
            var generator = new CSharpCodeGenerator();
            var result = generator.Write(expression);

            Assert.That.StringEquals($"(1{symbol})", result);
        }
    }
}
