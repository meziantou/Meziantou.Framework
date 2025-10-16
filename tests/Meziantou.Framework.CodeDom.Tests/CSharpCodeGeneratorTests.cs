#pragma warning disable MA0101 // String contains an implicit end of line character
namespace Meziantou.Framework.CodeDom.Tests;

public class CSharpCodeGeneratorTests
{
    private static void AssertCsharp<T>(T obj, string expectedCsharpCode, bool ignoreNewLines = true) where T : CodeObject
    {
        var generator = new CSharpCodeGenerator();
        var actual = generator.Write(obj);

        if (ignoreNewLines)
        {
            expectedCsharpCode = expectedCsharpCode.Replace("\r\n", "\n", StringComparison.Ordinal);
            actual = actual.Replace("\r\n", "\n", StringComparison.Ordinal);
        }

        Assert.Equal(expectedCsharpCode, actual);
    }

    [Fact]
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
                new MethodInvokeExpression(method, new BinaryExpression(BinaryOperator.Substract, n, 1)))),
        };

        AssertCsharp(unit, @"namespace Meziantou.Framework.CodeDom
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
");
    }

    [Fact]
    public void CSharpCodeGenerator_ClassDeclaration()
    {
        var type = new ClassDeclaration("Sample");

        AssertCsharp(type, @"class Sample
{
}
");
    }

    [Fact]
    public void CSharpCodeGenerator_ClassDeclarations()
    {
        var unit = new CompilationUnit();
        unit.CommentsBefore.Add("test");
        var ns = unit.AddNamespace("test");

        ns.AddType(new ClassDeclaration("Sample1"));
        ns.AddType(new ClassDeclaration("Sample2"));

        AssertCsharp(unit, @"// test
namespace test
{
    class Sample1
    {
    }

    class Sample2
    {
    }
}
");
    }

    [Fact]
    public void CSharpCodeGenerator_ClassDeclaration_Generic()
    {
        var type = new ClassDeclaration("Sample");
        type.Parameters.Add(new TypeParameter("T1") { Constraints = { new ValueTypeTypeParameterConstraint() } });
        type.Parameters.Add(new TypeParameter("T2")
        {
            Constraints = {
                    new ConstructorParameterConstraint(),
                    new ClassTypeParameterConstraint(),
                    new BaseTypeParameterConstraint(typeof(ICloneable)),
            },
        });

        AssertCsharp(type, @"class Sample<T1, T2>
    where T1 : struct
    where T2 : class, global::System.ICloneable, new()
{
}
");
    }

    [Fact]
    public void CSharpCodeGenerator_StructDeclaration()
    {
        var type = new StructDeclaration("Sample");

        AssertCsharp(type, @"struct Sample
{
}
");
    }

    [Fact]
    public void CSharpCodeGenerator_InterfaceDeclaration()
    {
        var type = new InterfaceDeclaration("Sample");

        AssertCsharp(type, @"interface Sample
{
}
");
    }

    [Fact]
    public void CSharpCodeGenerator_InterfaceDeclaration_Generic()
    {
        var type = new InterfaceDeclaration("Sample");
        type.Parameters.Add(new TypeParameter("T1") { Constraints = { new ValueTypeTypeParameterConstraint() } });
        type.Parameters.Add(new TypeParameter("T2")
        {
            Constraints = {
                    new ConstructorParameterConstraint(),
                    new ClassTypeParameterConstraint(),
                    new BaseTypeParameterConstraint(typeof(ICloneable)),
            },
        });

        AssertCsharp(type, @"interface Sample<T1, T2>
    where T1 : struct
    where T2 : class, global::System.ICloneable, new()
{
}
");
    }

    [Fact]
    public void CSharpCodeGenerator_EnumDeclaration()
    {
        var type = new EnumerationDeclaration("Sample")
        {
            BaseType = typeof(uint),
            Modifiers = Modifiers.Internal,
            Members =
            {
                new EnumerationMember("A", 1),
                new EnumerationMember("B", 2),
            },
            CustomAttributes =
            {
                new CustomAttribute(typeof(FlagsAttribute)),
            },
        };

        AssertCsharp(type, @"[global::System.FlagsAttribute]
internal enum Sample : uint
{
    A = 1,
    B = 2
}
");
    }

    [Fact]
    public void CSharpCodeGenerator_DelegateDeclaration()
    {
        var d = new DelegateDeclaration("Sample")
        {
            ReturnType = typeof(void),
            Modifiers = Modifiers.Public,
            Arguments =
            {
                new MethodArgumentDeclaration(typeof(string), "a"),
            },
        };

        AssertCsharp(d, @"public delegate void Sample(string a);
");
    }

    [Fact]
    public void CSharpCodeGenerator_FieldDeclaration()
    {
        var type = new ClassDeclaration("Sample");
        type.AddMember(new FieldDeclaration("_a", typeof(int)));
        type.AddMember(new FieldDeclaration("_b", typeof(Type), Modifiers.Private));
        type.AddMember(new FieldDeclaration("_c", typeof(int), Modifiers.Protected, 10));

        AssertCsharp(type, @"class Sample
{
    int _a;

    private global::System.Type _b;

    protected int _c = 10;
}
");
    }

    [Fact]
    public void CSharpCodeGenerator_EventDeclaration()
    {
        var type = new ClassDeclaration("Sample");
        type.AddMember(new EventFieldDeclaration("A", typeof(EventHandler), Modifiers.Public));

        AssertCsharp(type, @"class Sample
{
    public event global::System.EventHandler A;
}
");
    }

    [Fact]
    public void CSharpCodeGenerator_AddEventHandler()
    {
        var statement = new AddEventHandlerStatement(
            new MemberReferenceExpression(new ThisExpression(), "A"),
            new MemberReferenceExpression(new ThisExpression(), "Handler"));

        AssertCsharp(statement, @"this.A += this.Handler;
");
    }

    [Fact]
    public void CSharpCodeGenerator_RemoveEventHandler()
    {
        var statement = new RemoveEventHandlerStatement(
            new MemberReferenceExpression(new ThisExpression(), "A"),
            new MemberReferenceExpression(new ThisExpression(), "Handler"));

        AssertCsharp(statement, @"this.A -= this.Handler;
");
    }

    [Fact]
    public void CSharpCodeGenerator_WhileLoop()
    {
        var loop = new WhileStatement
        {
            Condition = new LiteralExpression(value: true),
            Body = [],
        };

        AssertCsharp(loop, @"while (true)
{
}
");
    }

    [Fact]
    public void CSharpCodeGenerator_Constructor()
    {
        var type = new ClassDeclaration("Sample");
        var ctor = type.AddMember(new ConstructorDeclaration());
        ctor.Modifiers = Modifiers.Internal;

        AssertCsharp(type, @"class Sample
{
    internal Sample()
    {
    }
}
");
    }

    [Fact]
    public void CSharpCodeGenerator_Constructor_Base()
    {
        var type = new ClassDeclaration("Sample");
        var ctor = type.AddMember(new ConstructorDeclaration());
        ctor.Initializer = new ConstructorBaseInitializer();
        ctor.Modifiers = Modifiers.Public;

        AssertCsharp(type, @"class Sample
{
    public Sample()
        : base()
    {
    }
}
");
    }

    [Fact]
    public void CSharpCodeGenerator_Constructor_This()
    {
        var type = new ClassDeclaration("Sample");
        var ctor = type.AddMember(new ConstructorDeclaration());
        ctor.Initializer = new ConstructorThisInitializer();
        ctor.Modifiers = Modifiers.Public;

        AssertCsharp(type, @"class Sample
{
    public Sample()
        : this()
    {
    }
}
");
    }

    [Fact]
    public void CSharpCodeGenerator_Constructor_ThisWithArgs()
    {
        var type = new ClassDeclaration("Sample");
        var ctor = type.AddMember(new ConstructorDeclaration());
        ctor.Initializer = new ConstructorThisInitializer(new LiteralExpression("arg"));
        ctor.Modifiers = Modifiers.Public;

        AssertCsharp(type, @"class Sample
{
    public Sample()
        : this(""arg"")
    {
    }
}
");
    }

    [Fact]
    public void CSharpCodeGenerator_ArrayIndexer()
    {
        var array = new VariableReferenceExpression("array");
        Expression expression = new ArrayIndexerExpression(array, 10);

        AssertCsharp(expression, "array[10]");
    }

    [Fact]
    public void CSharpCodeGenerator_ArrayIndexer_Multiple()
    {
        var array = new VariableReferenceExpression("array");
        Expression expression = new ArrayIndexerExpression(array, 10, "test");

        AssertCsharp(expression, @"array[10, ""test""]");
    }

    [Fact]
    public void CSharpCodeGenerator_Assign()
    {
        var statement = new AssignStatement(new VariableReferenceExpression("a"), 10);

        AssertCsharp(statement, @"a = 10;
");
    }

    [Fact]
    public void CSharpCodeGenerator_If()
    {
        var statement = new ConditionStatement
        {
            Condition = new LiteralExpression(value: true),
            TrueStatements = new SnippetStatement("TrueSnippet"),
        };

        AssertCsharp(statement, @"if (true)
{
    TrueSnippet
}
");
    }

    [Fact]
    public void CSharpCodeGenerator_If_Else()
    {
        var statement = new ConditionStatement
        {
            Condition = new LiteralExpression(value: true),
            TrueStatements = new SnippetStatement("TrueSnippet"),
            FalseStatements = new SnippetStatement("FalseSnippet"),
        };

        AssertCsharp(statement, @"if (true)
{
    TrueSnippet
}
else
{
    FalseSnippet
}
");
    }

    [Fact]
    public void CSharpCodeGenerator_If_Empty()
    {
        var statement = new ConditionStatement
        {
            Condition = new LiteralExpression(value: true),
        };

        AssertCsharp(statement, @"if (true)
{
}
");
    }

    [Fact]
    public void CSharpCodeGenerator_Try_Catch()
    {
        var statement = new TryCatchFinallyStatement
        {
            Try = new SnippetStatement("TrySnippet"),
            Catch =
            [
                new CatchClause() { Body = new SnippetStatement("Catch1") },
            ],
        };

        AssertCsharp(statement, @"try
{
    TrySnippet
}
catch
{
    Catch1
}
");
    }

    [Fact]
    public void CSharpCodeGenerator_Try_Catch_WithException()
    {
        var statement = new TryCatchFinallyStatement
        {
            Try = new SnippetStatement("TrySnippet"),
            Catch =
            [
                new CatchClause()
                {
                    ExceptionType = typeof(NotImplementedException),
                    ExceptionVariableName = "nie",
                    Body = new SnippetStatement("Catch1"),
                },
                new CatchClause()
                {
                    ExceptionType = typeof(Exception),
                    ExceptionVariableName = "ex",
                    Body = new ThrowStatement(),
                },
            ],
        };

        AssertCsharp(statement, @"try
{
    TrySnippet
}
catch (global::System.NotImplementedException nie)
{
    Catch1
}
catch (global::System.Exception ex)
{
    throw;
}
");
    }

    [Fact]
    public void CSharpCodeGenerator_Try_Finally()
    {
        var statement = new TryCatchFinallyStatement
        {
            Try = new SnippetStatement("TrySnippet"),
            Finally = new SnippetStatement("FinallyStatement"),
        };

        AssertCsharp(statement, @"try
{
    TrySnippet
}
finally
{
    FinallyStatement
}
");
    }

    [Fact]
    public void CSharpCodeGenerator_Literal_String()
    {
        var literal = new LiteralExpression("test");

        AssertCsharp(literal, "\"test\"");
    }

    [Fact]
    public void CSharpCodeGenerator_Literal_StringWithNewLine()
    {
        var literal = new LiteralExpression("line1\r\nline2");
        AssertCsharp(literal, "\"line1\\r\\nline2\"", ignoreNewLines: false);
    }

    [Fact]
    public void CSharpCodeGenerator_Default()
    {
        var expr = new DefaultValueExpression(typeof(string));
        AssertCsharp(expr, "default(string)");
    }

    [Fact]
    public void CSharpCodeGenerator_Cast()
    {
        var expr = new CastExpression(new VariableReferenceExpression("a"), typeof(string));
        AssertCsharp(expr, "((string)a)");
    }

    [Fact]
    public void CSharpCodeGenerator_Convert()
    {
        var expr = new ConvertExpression(new VariableReferenceExpression("a"), typeof(string));
        AssertCsharp(expr, "(a as string)");
    }

    [Fact]
    public void CSharpCodeGenerator_Throw()
    {
        var expr = new ThrowStatement(new NewObjectExpression(typeof(Exception)));
        AssertCsharp(expr, @"throw new global::System.Exception();
");
    }

    [Fact]
    public void CSharpCodeGenerator_CustomAttributes_WithoutArgument()
    {
        var method = new MethodDeclaration("Sample")
        {
            Statements = [],
            CustomAttributes =
            {
                new CustomAttribute(new TypeReference("TestAttribute")),
            },
        };

        AssertCsharp(method, @"[global::TestAttribute]
void Sample()
{
}
");
    }

    [Fact]
    public void CSharpCodeGenerator_CustomAttributes_WithArgument()
    {
        var method = new MethodDeclaration("Sample")
        {
            Statements = [],
            CustomAttributes =
            {
                new CustomAttribute(new TypeReference("TestAttribute"))
                {
                    Arguments =
                    {
                        new CustomAttributeArgument("arg1"),
                    },
                },
            },
        };

        AssertCsharp(method, @"[global::TestAttribute(""arg1"")]
void Sample()
{
}
");
    }

    [Fact]
    public void CSharpCodeGenerator_CustomAttributes_WithArguments()
    {
        var method = new MethodDeclaration("Sample")
        {
            Statements = [],
            CustomAttributes =
            {
                new CustomAttribute(new TypeReference("TestAttribute"))
                {
                    Arguments =
                    {
                        new CustomAttributeArgument("arg1"),
                        new CustomAttributeArgument("arg2"),
                    },
                },
            },
        };

        AssertCsharp(method, @"[global::TestAttribute(""arg1"", ""arg2"")]
void Sample()
{
}
");
    }

    [Fact]
    public void CSharpCodeGenerator_CustomAttributes_WithNamedArgument()
    {
        var method = new MethodDeclaration("Sample")
        {
            Statements = [],
            CustomAttributes =
            {
                new CustomAttribute(new TypeReference("TestAttribute"))
                {
                    Arguments =
                    {
                        new CustomAttributeArgument("Name1", "arg1"),
                    },
                },
            },
        };

        AssertCsharp(method, @"[global::TestAttribute(Name1 = ""arg1"")]
void Sample()
{
}
");
    }

    [Fact]
    public void CSharpCodeGenerator_CustomAttributes_WithNamedArguments()
    {
        var method = new MethodDeclaration("Sample")
        {
            Statements = [],
            CustomAttributes =
            {
                new CustomAttribute(new TypeReference("TestAttribute"))
                {
                    Arguments =
                    {
                        new CustomAttributeArgument("Name1", "arg1"),
                        new CustomAttributeArgument("Name2", "arg2"),
                    },
                },
            },
        };

        AssertCsharp(method, @"[global::TestAttribute(Name1 = ""arg1"", Name2 = ""arg2"")]
void Sample()
{
}
");
    }

    [Fact]
    public void CSharpCodeGenerator_CustomAttributes_WithMixedUnnamedAndNamedArguments()
    {
        var method = new MethodDeclaration("Sample")
        {
            Statements = [],
            CustomAttributes =
            {
                new CustomAttribute(new TypeReference("TestAttribute"))
                {
                    Arguments =
                    {
                        new CustomAttributeArgument("arg1"),
                        new CustomAttributeArgument("Name2", "arg2"),
                        new CustomAttributeArgument("arg3"),
                    },
                },
            },
        };

        AssertCsharp(method, @"[global::TestAttribute(""arg1"", ""arg3"", Name2 = ""arg2"")]
void Sample()
{
}
");
    }

    [Fact]
    public void CSharpCodeGenerator_CustomAttributes_MultipleAttributes()
    {
        var method = new MethodDeclaration("Sample")
        {
            Statements = [],
            CustomAttributes =
            {
                new CustomAttribute(new TypeReference("TestAttribute1")),
                new CustomAttribute(new TypeReference("TestAttribute2")),
            },
        };

        AssertCsharp(method, @"[global::TestAttribute1]
[global::TestAttribute2]
void Sample()
{
}
");
    }

    [Fact]
    public void CSharpCodeGenerator_Method_GenericParameter()
    {
        var method = new MethodDeclaration("Sample")
        {
            Statements = [],
            Parameters =
            {
                new TypeParameter("T"),
            },
        };

        AssertCsharp(method, @"void Sample<T>()
{
}
");
    }

    [Fact]
    public void CSharpCodeGenerator_Method_GenericParameterWithConstraint()
    {
        var method = new MethodDeclaration("Sample")
        {
            Statements = [],
            Parameters =
            {
                new TypeParameter("T") { Constraints = { new ClassTypeParameterConstraint() } },
            },
        };

        AssertCsharp(method, @"void Sample<T>()
    where T : class
{
}
");
    }

    [Fact]
    public void CSharpCodeGenerator_Method_Abstract()
    {
        var method = new MethodDeclaration("Sample")
        {
            Modifiers = Modifiers.Protected | Modifiers.Abstract,
        };

        AssertCsharp(method, @"protected abstract void Sample();
");
    }

    [Fact]
    public void CSharpCodeGenerator_Method_AbstractWithGenericParameterWithConstraint()
    {
        var method = new MethodDeclaration("Sample")
        {
            Modifiers = Modifiers.Protected | Modifiers.Abstract,
            Parameters =
            {
                new TypeParameter("T") { Constraints = { new ClassTypeParameterConstraint() } },
            },
        };

        AssertCsharp(method, @"protected abstract void Sample<T>()
    where T : class
    ;
");
    }

    [Fact]
    public void CSharpCodeGenerator_Method_ExplicitImplementation()
    {
        var method = new MethodDeclaration("A")
        {
            PrivateImplementationType = new TypeReference("Foo.IBar"),
            Statements = [],
        };

        AssertCsharp(method, @"void global::Foo.IBar.A()
{
}
");
    }

    [Fact]
    public void CSharpCodeGenerator_Property_ExplicitImplementation()
    {
        var prop = new PropertyDeclaration("A", typeof(int))
        {
            PrivateImplementationType = new TypeReference("Foo.IBar"),
            Getter = new ReturnStatement(10),
        };

        AssertCsharp(prop, @"int global::Foo.IBar.A
{
    get
    {
        return 10;
    }
}
");
    }

    [Fact]
    public void CSharpCodeGenerator_Property_GetterModifiers()
    {
        var prop = new PropertyDeclaration("A", typeof(int))
        {
            Getter = new PropertyAccessorDeclaration
            {
                Modifiers = Modifiers.Private,
                Statements = new ReturnStatement(10),
            },
        };

        AssertCsharp(prop, @"int A
{
    private get
    {
        return 10;
    }
}
");
    }

    [Fact]
    public void CSharpCodeGenerator_Property_SetterModifiers()
    {
        var prop = new PropertyDeclaration("A", typeof(int))
        {
            Setter = new PropertyAccessorDeclaration
            {
                Modifiers = Modifiers.Internal,
            },
        };

        AssertCsharp(prop, @"int A
{
    internal set
    {
    }
}
");
    }

    [Fact]
    public void CSharpCodeGenerator_Property_GenericType()
    {
        var prop = new PropertyDeclaration("A", new TypeReference(typeof(Nullable<>)).MakeGeneric(typeof(int)))
        {
            Setter = new PropertyAccessorDeclaration(),
        };

        AssertCsharp(prop, @"global::System.Nullable<int> A
{
    set
    {
    }
}
");
    }

    [Fact]
    public void CSharpCodeGenerator_Event_ExplicitImplementation()
    {
        var method = new EventFieldDeclaration("A", typeof(EventHandler))
        {
            PrivateImplementationType = new TypeReference("Foo.IBar"),
            AddAccessor = [],
            RemoveAccessor = [],
        };

        AssertCsharp(method, @"event global::System.EventHandler global::Foo.IBar.A
{
    add
    {
    }
    remove
    {
    }
}
");
    }

    [Fact]
    public void CSharpCodeGenerator_ExpressionStatement()
    {
        var statement = new ExpressionStatement(new NewObjectExpression(new TypeReference("Disposable")));

        AssertCsharp(statement, @"new global::Disposable();
");
    }

    [Fact]
    public void CSharpCodeGenerator_UsingDirective()
    {
        var directive = new UsingDirective("System");

        AssertCsharp(directive, "using System;");
    }

    [Fact]
    public void CSharpCodeGenerator_UsingStatement_WithoutBody()
    {
        var statement = new UsingStatement
        {
            Statement = new NewObjectExpression(new TypeReference("Disposable")),
        };

        AssertCsharp(statement, @"using (new global::Disposable())
{
}
");
    }

    [Fact]
    public void CSharpCodeGenerator_UsingStatement_WithBody()
    {
        var statement = new UsingStatement
        {
            Statement = new VariableDeclarationStatement("disposable", type: null, initExpression: new NewObjectExpression(new TypeReference("Disposable"))),
            Body = (Statement)new MethodInvokeExpression(new VariableReferenceExpression("disposable")),
        };

        AssertCsharp(statement, @"using (var disposable = new global::Disposable())
{
    disposable();
}
");
    }

    [Fact]
    public void CSharpCodeGenerator_MethodInvoke()
    {
        var expression = new MethodInvokeExpression(
            new MemberReferenceExpression(new TypeReference("System.Console"), "Write"),
            "test");

        AssertCsharp(expression, @"global::System.Console.Write(""test"")");
    }

    [Fact]
    public void CSharpCodeGenerator_MethodInvokeWithGenericParameters()
    {
        var expression = new MethodInvokeExpression(
            new MemberReferenceExpression(new TypeReference("Console"), "Write"),
            "test");
        expression.Parameters.Add(typeof(string));

        AssertCsharp(expression, @"global::Console.Write<string>(""test"")");
    }

    [Fact]
    public void CSharpCodeGenerator_MethodInvoke_OutArgument()
    {
        var expression = new MethodInvokeExpression(
            new MemberReferenceExpression(new TypeReference("Console"), "Write"),
            new MethodInvokeArgumentExpression(new VariableReferenceExpression("test")) { Direction = Direction.Out });

        AssertCsharp(expression, "global::Console.Write(out test)");
    }

    [Fact]
    public void CSharpCodeGenerator_Iteration()
    {
        var statement = new IterationStatement();
        var variable = new VariableDeclarationStatement("i", type: null, initExpression: 0);
        statement.Initialization = variable;
        statement.Condition = new BinaryExpression(BinaryOperator.LessThan, variable, 10);
        statement.IncrementStatement = new UnaryExpression(UnaryOperator.PostIncrement, variable);
        statement.Body = new MethodInvokeExpression(
            new MemberReferenceExpression(new TypeReference("System.Console"), "Write"),
            variable);

        AssertCsharp(statement, @"for (var i = 0; (i < 10); (i++))
{
    global::System.Console.Write(i);
}
");
    }

    [Fact]
    public void CSharpCodeGenerator_Iteration_Empty()
    {
        var statement = new IterationStatement();

        AssertCsharp(statement, @"for (; ; )
{
}
");
    }

    [Fact]
    public void CSharpCodeGenerator_TypeOf()
    {
        var statement = new TypeOfExpression(typeof(string));

        AssertCsharp(statement, "typeof(string)");
    }

    [Fact]
    public void CSharpCodeGenerator_NextLoopIteration()
    {
        var statement = new GotoNextLoopIterationStatement();

        AssertCsharp(statement, @"continue;
");
    }

    [Fact]
    public void CSharpCodeGenerator_CodeExitLoop()
    {
        var statement = new ExitLoopStatement();

        AssertCsharp(statement, @"break;
");
    }

    [Fact]
    public void CSharpCodeGenerator_Return()
    {
        var statement = new ReturnStatement();

        AssertCsharp(statement, @"return;
");
    }

    [Fact]
    public void CSharpCodeGenerator_ReturnExpression()
    {
        var statement = new ReturnStatement(10);

        AssertCsharp(statement, @"return 10;
");
    }

    [Fact]
    public void CSharpCodeGenerator_YieldReturnExpression()
    {
        var statement = new YieldReturnStatement(10);

        AssertCsharp(statement, @"yield return 10;
");
    }

    [Fact]
    public void CSharpCodeGenerator_YieldBreak()
    {
        var statement = new YieldBreakStatement();

        AssertCsharp(statement, @"yield break;
");
    }

    [Fact]
    public void CSharpCodeGenerator_Await()
    {
        var statement = new AwaitExpression(new VariableReferenceExpression("awaitable"));

        AssertCsharp(statement, "await awaitable");
    }

    [Fact]
    public void CSharpCodeGenerator_Comment()
    {
        var statement = new CommentStatement("test");

        AssertCsharp(statement, @"// test
");
    }

    [Fact]
    public void CSharpCodeGenerator_CommentMultiLine()
    {
        var statement = new CommentStatement("test1" + Environment.NewLine + Environment.NewLine + "test2");

        AssertCsharp(statement, @"// test1
//
// test2
");
    }

    [Fact]
    public void CSharpCodeGenerator_CommentNull()
    {
        var statement = new CommentStatement();

        AssertCsharp(statement, @"//
");
    }

    [Fact]
    public void CSharpCodeGenerator_ExpressionCommentBeforeAndAfter()
    {
        var expression = new SnippetExpression("code");
        expression.CommentsBefore.Add("comment1");
        expression.CommentsAfter.Add("comment2");

        AssertCsharp(expression, "/* comment1 */ code /* comment2 */");
    }

    [Fact]
    public void CSharpCodeGenerator_ExpressionCommentBefore()
    {
        var expression = new SnippetExpression("code");
        expression.CommentsBefore.Add("comment1");

        AssertCsharp(expression, "/* comment1 */ code");
    }

    [Fact]
    public void CSharpCodeGenerator_ExpressionCommentAfter()
    {
        var expression = new SnippetExpression("code");
        expression.CommentsAfter.Add("comment2");

        AssertCsharp(expression, "code /* comment2 */");
    }

    [Fact]
    public void CSharpCodeGenerator_ExpressionCommentAfterWithInlineCommentEnd()
    {
        var expression = new SnippetExpression("code");
        expression.CommentsAfter.Add("comment with */ in the middle");

        AssertCsharp(expression, @"code // comment with */ in the middle
");
    }

    [Fact]
    public void CSharpCodeGenerator_ExpressionCommentBeforeWithInlineCommentEnd()
    {
        var expression = new SnippetExpression("code");
        expression.CommentsBefore.Add("comment with */ in the middle");

        AssertCsharp(expression, @"// comment with */ in the middle
code");
    }

    [Fact]
    public void CSharpCodeGenerator_ExpressionCommentsBefore_Inlines()
    {
        var expression = new SnippetExpression("code");
        expression.CommentsBefore.Add("com1");
        expression.CommentsBefore.Add("com2");

        AssertCsharp(expression, "/* com1 */ /* com2 */ code");
    }

    [Fact]
    public void CSharpCodeGenerator_ExpressionCommentsBefore_LineAndInline()
    {
        var expression = new SnippetExpression("code");
        expression.CommentsBefore.Add("com1", CommentType.LineComment);
        expression.CommentsBefore.Add("com2");

        AssertCsharp(expression, @"// com1
/* com2 */ code");
    }

    [Fact]
    public void CSharpCodeGenerator_ExpressionCommentsAfter_LineAndInline()
    {
        var expression = new SnippetExpression("code");
        expression.CommentsAfter.Add("com1", CommentType.LineComment);
        expression.CommentsAfter.Add("com2");

        AssertCsharp(expression, @"code // com1
/* com2 */");
    }

    [Fact]
    public void CSharpCodeGenerator_StatementCommentsAfter_Line()
    {
        var expression = new ReturnStatement();
        expression.CommentsAfter.Add("com1", CommentType.LineComment);
        expression.CommentsAfter.Add("com2");

        AssertCsharp(expression, @"return;
// com1
// com2
");
    }

    [Fact]
    public void CSharpCodeGenerator_MethodXmlDocmentation()
    {
        var method = new MethodDeclaration("Sample")
        {
            Statements = [],
        };

        method.XmlComments.AddSummary("Test");

        AssertCsharp(method, @"/// <summary>Test</summary>
void Sample()
{
}
");
    }

    [Fact]
    public void CSharpCodeGenerator_TypeReference()
    {
        var expression = new TypeReferenceExpression(typeof(Console));

        AssertCsharp(expression, "global::System.Console");
    }

    [Fact]
    public void CSharpCodeGenerator_TypeReference_Nested()
    {
        var expression = new TypeReferenceExpression(typeof(SampleEnum));

        AssertCsharp(expression, "global::Meziantou.Framework.CodeDom.Tests.CSharpCodeGeneratorTests.SampleEnum");
    }

    [Fact]
    public void CSharpCodeGenerator_TypeReference_Generic()
    {
        var expression = new TypeReferenceExpression(typeof(Sample<int>));

        AssertCsharp(expression, "global::Meziantou.Framework.CodeDom.Tests.CSharpCodeGeneratorTests.Sample<int>");
    }

    [Fact]
    public void CSharpCodeGenerator_OperatorDeclaration_Implicit()
    {
        var member = new OperatorDeclaration()
        {
            Modifiers = Modifiers.Public | Modifiers.Static | Modifiers.Implicit,
            ReturnType = typeof(int),
            Arguments =
            {
                new MethodArgumentDeclaration(typeof(long), "value"),
            },
            Statements =
            {
                new ReturnStatement(new ArgumentReferenceExpression("value")),
            },
        };

        AssertCsharp(member, @"public static implicit operator int(long value)
{
    return value;
}
");
    }

    [Fact]
    public void CSharpCodeGenerator_OperatorDeclaration_Implicit_UseParentType()
    {
        var type = new ClassDeclaration("Test");

        var member = type.AddMember(new OperatorDeclaration()
        {
            Modifiers = Modifiers.Public | Modifiers.Static | Modifiers.Implicit,
            Arguments =
            {
                new MethodArgumentDeclaration(typeof(long), "value"),
            },
            Statements =
            {
                new ReturnStatement(new ArgumentReferenceExpression("value")),
            },
        });

        AssertCsharp(member, @"public static implicit operator Test(long value)
{
    return value;
}
");
    }

    [Fact]
    public void CSharpCodeGenerator_OperatorDeclaration_Explicit()
    {
        var member = new OperatorDeclaration()
        {
            Modifiers = Modifiers.Public | Modifiers.Static | Modifiers.Explicit,
            ReturnType = typeof(int),
            Arguments =
            {
                new MethodArgumentDeclaration(typeof(long), "value"),
            },
            Statements =
            {
                new ReturnStatement(new ArgumentReferenceExpression("value")),
            },
        };

        AssertCsharp(member, @"public static explicit operator int(long value)
{
    return value;
}
");
    }

    [Fact]
    public void CSharpCodeGenerator_OperatorDeclaration_Add()
    {
        var member = new OperatorDeclaration()
        {
            Modifiers = Modifiers.Public | Modifiers.Static,
            ReturnType = typeof(int),
            Name = "+",
            Arguments =
            {
                new MethodArgumentDeclaration(typeof(int), "a"),
                new MethodArgumentDeclaration(typeof(int), "b"),
            },
            Statements =
            {
                new ReturnStatement(new ArgumentReferenceExpression("value")),
            },
        };

        AssertCsharp(member, @"public static int operator +(int a, int b)
{
    return value;
}
");
    }

    private static class Sample<T>
    {
    }

    [Flags]
    private enum SampleEnum
    {
        A = 1,
        B = 2,
        C = 4,
        All = A | B | C,
    }

    [Fact]
    public void CSharpCodeGenerator_CodeExpressionFromEnum()
    {
        Expression expression = SampleEnum.A;

        AssertCsharp(expression, "global::Meziantou.Framework.CodeDom.Tests.CSharpCodeGeneratorTests.SampleEnum.A");
    }

    [Fact]
    public void CSharpCodeGenerator_CodeExpressionFromEnum_DefinedCombinaison()
    {
        Expression expression = SampleEnum.All;

        AssertCsharp(expression, "global::Meziantou.Framework.CodeDom.Tests.CSharpCodeGeneratorTests.SampleEnum.All");
    }

    [Fact]
    public void CSharpCodeGenerator_CodeExpressionFromEnum_Combinaison()
    {
        Expression expression = (SampleEnum)3;

        AssertCsharp(expression, "((global::Meziantou.Framework.CodeDom.Tests.CSharpCodeGeneratorTests.SampleEnum)3)");
    }

    [Fact]
    public void CSharpCodeGenerator_CodeExpressionFromEnum_UndefinedValue()
    {
        Expression expression = (SampleEnum)10;

        AssertCsharp(expression, "((global::Meziantou.Framework.CodeDom.Tests.CSharpCodeGeneratorTests.SampleEnum)10)");
    }

    [Theory]
    [InlineData(BinaryOperator.Add, "+")]
    [InlineData(BinaryOperator.And, "&&")]
    [InlineData(BinaryOperator.BitwiseAnd, "&")]
    [InlineData(BinaryOperator.BitwiseOr, "|")]
    [InlineData(BinaryOperator.Divide, "/")]
    [InlineData(BinaryOperator.Equals, "==")]
    [InlineData(BinaryOperator.GreaterThan, ">")]
    [InlineData(BinaryOperator.GreaterThanOrEqual, ">=")]
    [InlineData(BinaryOperator.LessThan, "<")]
    [InlineData(BinaryOperator.LessThanOrEqual, "<=")]
    [InlineData(BinaryOperator.Modulo, "%")]
    [InlineData(BinaryOperator.Multiply, "*")]
    [InlineData(BinaryOperator.NotEquals, "!=")]
    [InlineData(BinaryOperator.Or, "||")]
    [InlineData(BinaryOperator.ShiftLeft, "<<")]
    [InlineData(BinaryOperator.ShiftRight, ">>")]
    [InlineData(BinaryOperator.Substract, "-")]
    [InlineData(BinaryOperator.Xor, "^")]
    public void CSharpCodeGenerator_BinaryExpression(BinaryOperator op, string symbol)
    {
        var expression = new BinaryExpression(op, 1, 2);

        AssertCsharp(expression, $"(1 {symbol} 2)");
    }

    [Theory]
    [InlineData(UnaryOperator.Complement, "~")]
    [InlineData(UnaryOperator.Minus, "-")]
    [InlineData(UnaryOperator.Not, "!")]
    [InlineData(UnaryOperator.Plus, "+")]
    [InlineData(UnaryOperator.PreDecrement, "--")]
    [InlineData(UnaryOperator.PreIncrement, "++")]
    public void CSharpCodeGenerator_UnaryExpression_Pre(UnaryOperator op, string symbol)
    {
        var expression = new UnaryExpression(op, 1);

        AssertCsharp(expression, $"({symbol}1)");
    }

    [Theory]
    [InlineData(UnaryOperator.PostIncrement, "++")]
    [InlineData(UnaryOperator.PostDecrement, "--")]
    public void CSharpCodeGenerator_UnaryExpression_Post(UnaryOperator op, string symbol)
    {
        var expression = new UnaryExpression(op, 1);

        AssertCsharp(expression, $"(1{symbol})");
    }

    [Fact]
    public void CSharpCodeGenerator_Spaces_MultipleStatements()
    {
        var method = new MethodDeclaration("Test")
        {
            Statements =
            [
                new AssignStatement(new VariableReferenceExpression("a") , 0),
                new AssignStatement(new VariableReferenceExpression("b") , 0),
            ],
        };

        AssertCsharp(method, @"void Test()
{
    a = 0;
    b = 0;
}
");
    }

    [Fact]
    public void CSharpCodeGenerator_Spaces_MultipleMethods()
    {
        var c = new ClassDeclaration("Test");
        c.Members.Add(new MethodDeclaration("A"));
        c.Members.Add(new MethodDeclaration("B"));

        AssertCsharp(c, @"class Test
{
    void A();

    void B();
}
");
    }

    [Fact]
    public void CSharpCodeGenerator_Spaces_Brackets()
    {
        var method = new MethodDeclaration("Test")
        {
            Statements =
            [
                new ConditionStatement()
                {
                    Condition = new LiteralExpression(value: true),
                    TrueStatements =
                    [
                        new ConditionStatement()
                        {
                            Condition = new LiteralExpression(value: true),
                            TrueStatements = [],
                        },
                    ],
                },
                new AssignStatement(new VariableReferenceExpression("a") , 0),
                new AssignStatement(new VariableReferenceExpression("b") , 0),
            ],
        };

        AssertCsharp(method, @"void Test()
{
    if (true)
    {
        if (true)
        {
        }
    }

    a = 0;
    b = 0;
}
");
    }

    [Fact]
    public void CSharpCodeGenerator_Modifiers_PartialReadOnlyStruct()
    {
        var type = new StructDeclaration("Test")
        {
            Modifiers = Modifiers.Partial | Modifiers.ReadOnly,
        };

        AssertCsharp(type, @"readonly partial struct Test
{
}
");
    }

    [Fact]
    public void CSharpCodeGenerator_NewArray()
    {
        var variable = new VariableDeclarationStatement("a", typeof(int[]), new NewArrayExpression(typeof(int), 1, 2));

        AssertCsharp(variable, @"int[] a = new int[1, 2];
");
    }

    [Fact]
    public void CSharpCodeGenerator_EmptyGetter()
    {
        var prop = new PropertyDeclaration("A", typeof(int))
        {
            Getter = new PropertyAccessorDeclaration
            {
                Statements = null,
            },
        };

        AssertCsharp(prop, @"int A
{
    get;
}
");
    }

    [Fact]
    public void CSharpCodeGenerator_NullableType()
    {
        var prop = new VariableDeclarationStatement("a", new TypeReference(typeof(string)).MakeNullable());

        AssertCsharp(prop, @"string? a;
");
    }

    [Fact]
    public void CSharpCodeGenerator_CompilationContextNullableContext()
    {
        var unit = new CompilationUnit
        {
            NullableContext = NullableContext.Enable,
        };

        AssertCsharp(unit, @"#nullable enable
");
    }

    [Fact]
    public void CSharpCodeGenerator_UsingStatementNullableContext()
    {
        var unit = new UsingStatement
        {
            Statement = new ExpressionStatement(Expression.Null()) { NullableContext = NullableContext.Disable },
            //Body = new StatementCollection(),
            NullableContext = NullableContext.Enable,
        };

        AssertCsharp(unit, @"#nullable enable
using (
#nullable disable
null
#nullable enable
)
{
}
#nullable disable
");
    }

    [Fact]
    public void CSharpCodeGenerator_IsInstanceOfType()
    {
        var unit = new IsInstanceOfTypeExpression
        {
            Expression = new VariableReferenceExpression("a"),
            Type = new TypeReference(typeof(string)),
        };

        AssertCsharp(unit, @"(a is string)");
    }

    [Fact]
    public void CSharpCodeGenerator_TypeReference_NestedType()
    {
        var innerStruct = new StructDeclaration("D");
        _ = new NamespaceDeclaration("A")
        {
            Namespaces =
            {
                new NamespaceDeclaration("B")
                {
                    Types =
                    {
                        new ClassDeclaration("C")
                        {
                            Types = { innerStruct },
                        },
                    },
                },
            },
        };

        var variable = new VariableDeclarationStatement("demo", new TypeReference(innerStruct));
        AssertCsharp(variable, @"global::A.B.C.D demo;
");
    }
}
