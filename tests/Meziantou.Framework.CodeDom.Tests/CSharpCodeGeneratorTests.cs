using System;
using TestUtilities;
using Xunit;

namespace Meziantou.Framework.CodeDom.Tests
{
    public class CSharpCodeGeneratorTests
    {
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

            var generator = new CSharpCodeGenerator();
            var result = generator.Write(unit);

            AssertExtensions.StringEquals(@"namespace Meziantou.Framework.CodeDom
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

        [Fact]
        public void CSharpCodeGenerator_ClassDeclaration()
        {
            var type = new ClassDeclaration("Sample");

            var generator = new CSharpCodeGenerator();
            var result = generator.Write(type);

            AssertExtensions.StringEquals(@"class Sample
{
}
", result);
        }

        [Fact]
        public void CSharpCodeGenerator_ClassDeclarations()
        {
            var unit = new CompilationUnit();
            var ns = unit.AddNamespace("test");

            ns.AddType(new ClassDeclaration("Sample1"));
            ns.AddType(new ClassDeclaration("Sample2"));

            var generator = new CSharpCodeGenerator();
            var result = generator.Write(unit);

            AssertExtensions.StringEquals(@"namespace test
{
    class Sample1
    {
    }

    class Sample2
    {
    }
}
", result);
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

            var generator = new CSharpCodeGenerator();
            var result = generator.Write(type);

            AssertExtensions.StringEquals(@"class Sample<T1, T2>
    where T1 : struct
    where T2 : class, System.ICloneable, new()
{
}
", result);
        }

        [Fact]
        public void CSharpCodeGenerator_StructDeclaration()
        {
            var type = new StructDeclaration("Sample");

            var generator = new CSharpCodeGenerator();
            var result = generator.Write(type);

            AssertExtensions.StringEquals(@"struct Sample
{
}
", result);
        }

        [Fact]
        public void CSharpCodeGenerator_InterfaceDeclaration()
        {
            var type = new InterfaceDeclaration("Sample");

            var generator = new CSharpCodeGenerator();
            var result = generator.Write(type);

            AssertExtensions.StringEquals(@"interface Sample
{
}
", result);
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

            var generator = new CSharpCodeGenerator();
            var result = generator.Write(type);

            AssertExtensions.StringEquals(@"interface Sample<T1, T2>
    where T1 : struct
    where T2 : class, System.ICloneable, new()
{
}
", result);
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

            var generator = new CSharpCodeGenerator();
            var result = generator.Write(type);

            AssertExtensions.StringEquals(@"[System.FlagsAttribute]
internal enum Sample : uint
{
    A = 1,
    B = 2
}
", result);
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

            var generator = new CSharpCodeGenerator();
            var result = generator.Write(d);

            AssertExtensions.StringEquals(@"public delegate void Sample(string a);
", result);
        }

        [Fact]
        public void CSharpCodeGenerator_FieldDeclaration()
        {
            var type = new ClassDeclaration("Sample");
            type.AddMember(new FieldDeclaration("_a", typeof(int)));
            type.AddMember(new FieldDeclaration("_b", typeof(Type), Modifiers.Private));
            type.AddMember(new FieldDeclaration("_c", typeof(int), Modifiers.Protected, 10));

            var generator = new CSharpCodeGenerator();
            var result = generator.Write(type);

            AssertExtensions.StringEquals(@"class Sample
{
    int _a;

    private System.Type _b;

    protected int _c = 10;
}
", result);
        }

        [Fact]
        public void CSharpCodeGenerator_EventDeclaration()
        {
            var type = new ClassDeclaration("Sample");
            type.AddMember(new EventFieldDeclaration("A", typeof(EventHandler), Modifiers.Public));

            var generator = new CSharpCodeGenerator();
            var result = generator.Write(type);

            AssertExtensions.StringEquals(@"class Sample
{
    public event System.EventHandler A;
}
", result);
        }

        [Fact]
        public void CSharpCodeGenerator_AddEventHandler()
        {
            var statement = new AddEventHandlerStatement(
                new MemberReferenceExpression(new ThisExpression(), "A"),
                new MemberReferenceExpression(new ThisExpression(), "Handler"));

            var generator = new CSharpCodeGenerator();
            var result = generator.Write(statement);

            AssertExtensions.StringEquals(@"this.A += this.Handler;
", result);
        }

        [Fact]
        public void CSharpCodeGenerator_RemoveEventHandler()
        {
            var statement = new RemoveEventHandlerStatement(
                new MemberReferenceExpression(new ThisExpression(), "A"),
                new MemberReferenceExpression(new ThisExpression(), "Handler"));

            var generator = new CSharpCodeGenerator();
            var result = generator.Write(statement);

            AssertExtensions.StringEquals(@"this.A -= this.Handler;
", result);
        }

        [Fact]
        public void CSharpCodeGenerator_WhileLoop()
        {
            var loop = new WhileStatement
            {
                Condition = new LiteralExpression(value: true),
                Body = new StatementCollection(),
            };

            var generator = new CSharpCodeGenerator();
            var result = generator.Write(loop);

            AssertExtensions.StringEquals(@"while (true)
{
}
", result);
        }

        [Fact]
        public void CSharpCodeGenerator_Constructor()
        {
            var type = new ClassDeclaration("Sample");
            var ctor = type.AddMember(new ConstructorDeclaration());
            ctor.Modifiers = Modifiers.Internal;
            var generator = new CSharpCodeGenerator();
            var result = generator.Write(type);

            AssertExtensions.StringEquals(@"class Sample
{
    internal Sample()
    {
    }
}
", result);
        }

        [Fact]
        public void CSharpCodeGenerator_Constructor_Base()
        {
            var type = new ClassDeclaration("Sample");
            var ctor = type.AddMember(new ConstructorDeclaration());
            ctor.Initializer = new ConstructorBaseInitializer();
            ctor.Modifiers = Modifiers.Public;

            var generator = new CSharpCodeGenerator();
            var result = generator.Write(type);

            AssertExtensions.StringEquals(@"class Sample
{
    public Sample()
        : base()
    {
    }
}
", result);
        }

        [Fact]
        public void CSharpCodeGenerator_Constructor_This()
        {
            var type = new ClassDeclaration("Sample");
            var ctor = type.AddMember(new ConstructorDeclaration());
            ctor.Initializer = new ConstructorThisInitializer();
            ctor.Modifiers = Modifiers.Public;

            var generator = new CSharpCodeGenerator();
            var result = generator.Write(type);

            AssertExtensions.StringEquals(@"class Sample
{
    public Sample()
        : this()
    {
    }
}
", result);
        }

        [Fact]
        public void CSharpCodeGenerator_Constructor_ThisWithArgs()
        {
            var type = new ClassDeclaration("Sample");
            var ctor = type.AddMember(new ConstructorDeclaration());
            ctor.Initializer = new ConstructorThisInitializer(new LiteralExpression("arg"));
            ctor.Modifiers = Modifiers.Public;

            var generator = new CSharpCodeGenerator();
            var result = generator.Write(type);

            AssertExtensions.StringEquals(@"class Sample
{
    public Sample()
        : this(""arg"")
    {
    }
}
", result);
        }

        [Fact]
        public void CSharpCodeGenerator_ArrayIndexer()
        {
            var array = new VariableReferenceExpression("array");
            Expression expression = new ArrayIndexerExpression(array, 10);

            var generator = new CSharpCodeGenerator();
            var result = generator.Write(expression);

            AssertExtensions.StringEquals("array[10]", result);
        }

        [Fact]
        public void CSharpCodeGenerator_ArrayIndexer_Multiple()
        {
            var array = new VariableReferenceExpression("array");
            Expression expression = new ArrayIndexerExpression(array, 10, "test");

            var generator = new CSharpCodeGenerator();
            var result = generator.Write(expression);

            AssertExtensions.StringEquals(@"array[10, ""test""]", result);
        }

        [Fact]
        public void CSharpCodeGenerator_Assign()
        {
            var statement = new AssignStatement(new VariableReferenceExpression("a"), 10);

            var generator = new CSharpCodeGenerator();
            var result = generator.Write(statement);

            AssertExtensions.StringEquals(@"a = 10;
", result);
        }

        [Fact]
        public void CSharpCodeGenerator_If()
        {
            var statement = new ConditionStatement
            {
                Condition = new LiteralExpression(value: true),
                TrueStatements = new SnippetStatement("TrueSnippet"),
            };

            var generator = new CSharpCodeGenerator();
            var result = generator.Write(statement);

            AssertExtensions.StringEquals(@"if (true)
{
    TrueSnippet
}
", result);
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

            var generator = new CSharpCodeGenerator();
            var result = generator.Write(statement);

            AssertExtensions.StringEquals(@"if (true)
{
    TrueSnippet
}
else
{
    FalseSnippet
}
", result);
        }

        [Fact]
        public void CSharpCodeGenerator_If_Empty()
        {
            var statement = new ConditionStatement
            {
                Condition = new LiteralExpression(value: true),
            };

            var generator = new CSharpCodeGenerator();
            var result = generator.Write(statement);

            AssertExtensions.StringEquals(@"if (true)
{
}
", result);
        }

        [Fact]
        public void CSharpCodeGenerator_Try_Catch()
        {
            var statement = new TryCatchFinallyStatement
            {
                Try = new SnippetStatement("TrySnippet"),
                Catch = new CatchClauseCollection
                {
                    new CatchClause() { Body = new SnippetStatement("Catch1") },
                },
            };

            var generator = new CSharpCodeGenerator();
            var result = generator.Write(statement);

            AssertExtensions.StringEquals(@"try
{
    TrySnippet
}
catch
{
    Catch1
}
", result);
        }

        [Fact]
        public void CSharpCodeGenerator_Try_Catch_WithException()
        {
            var statement = new TryCatchFinallyStatement
            {
                Try = new SnippetStatement("TrySnippet"),
                Catch = new CatchClauseCollection
                {
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
                },
            };

            var generator = new CSharpCodeGenerator();
            var result = generator.Write(statement);

            AssertExtensions.StringEquals(@"try
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

        [Fact]
        public void CSharpCodeGenerator_Try_Finally()
        {
            var statement = new TryCatchFinallyStatement
            {
                Try = new SnippetStatement("TrySnippet"),
                Finally = new SnippetStatement("FinallyStatement"),
            };

            var generator = new CSharpCodeGenerator();
            var result = generator.Write(statement);

            AssertExtensions.StringEquals(@"try
{
    TrySnippet
}
finally
{
    FinallyStatement
}
", result);
        }

        [Fact]
        public void CSharpCodeGenerator_Literal_String()
        {
            var literal = new LiteralExpression("test");
            var generator = new CSharpCodeGenerator();
            var result = generator.Write(literal);

            AssertExtensions.StringEquals("\"test\"", result);
        }

        [Fact]
        public void CSharpCodeGenerator_Literal_StringWithNewLine()
        {
            var literal = new LiteralExpression("line1\r\nline2");
            var generator = new CSharpCodeGenerator();
            var result = generator.Write(literal);

            AssertExtensions.StringEquals("\"line1\\r\\nline2\"", result, ignoreNewLines: false);
        }

        [Fact]
        public void CSharpCodeGenerator_Default()
        {
            var expr = new DefaultValueExpression(typeof(string));
            var generator = new CSharpCodeGenerator();
            var result = generator.Write(expr);

            AssertExtensions.StringEquals("default(string)", result);
        }

        [Fact]
        public void CSharpCodeGenerator_Cast()
        {
            var expr = new CastExpression(new VariableReferenceExpression("a"), typeof(string));
            var generator = new CSharpCodeGenerator();
            var result = generator.Write(expr);

            AssertExtensions.StringEquals("((string)a)", result);
        }

        [Fact]
        public void CSharpCodeGenerator_Convert()
        {
            var expr = new ConvertExpression(new VariableReferenceExpression("a"), typeof(string));
            var generator = new CSharpCodeGenerator();
            var result = generator.Write(expr);

            AssertExtensions.StringEquals("(a as string)", result);
        }

        [Fact]
        public void CSharpCodeGenerator_Throw()
        {
            var expr = new ThrowStatement(new NewObjectExpression(typeof(Exception)));
            var generator = new CSharpCodeGenerator();
            var result = generator.Write(expr);

            AssertExtensions.StringEquals(@"throw new System.Exception();
", result);
        }

        [Fact]
        public void CSharpCodeGenerator_CustomAttributes_WithoutArgument()
        {
            var method = new MethodDeclaration("Sample")
            {
                Statements = new StatementCollection(),
                CustomAttributes =
                {
                    new CustomAttribute(new TypeReference("TestAttribute")),
                },
            };

            var generator = new CSharpCodeGenerator();
            var result = generator.Write(method);

            AssertExtensions.StringEquals(@"[TestAttribute]
void Sample()
{
}
", result);
        }

        [Fact]
        public void CSharpCodeGenerator_CustomAttributes_WithArgument()
        {
            var method = new MethodDeclaration("Sample")
            {
                Statements = new StatementCollection(),
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

            var generator = new CSharpCodeGenerator();
            var result = generator.Write(method);

            AssertExtensions.StringEquals(@"[TestAttribute(""arg1"")]
void Sample()
{
}
", result);
        }

        [Fact]
        public void CSharpCodeGenerator_CustomAttributes_WithArguments()
        {
            var method = new MethodDeclaration("Sample")
            {
                Statements = new StatementCollection(),
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

            var generator = new CSharpCodeGenerator();
            var result = generator.Write(method);

            AssertExtensions.StringEquals(@"[TestAttribute(""arg1"", ""arg2"")]
void Sample()
{
}
", result);
        }

        [Fact]
        public void CSharpCodeGenerator_CustomAttributes_WithNamedArgument()
        {
            var method = new MethodDeclaration("Sample")
            {
                Statements = new StatementCollection(),
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

            var generator = new CSharpCodeGenerator();
            var result = generator.Write(method);

            AssertExtensions.StringEquals(@"[TestAttribute(Name1 = ""arg1"")]
void Sample()
{
}
", result);
        }

        [Fact]
        public void CSharpCodeGenerator_CustomAttributes_WithNamedArguments()
        {
            var method = new MethodDeclaration("Sample")
            {
                Statements = new StatementCollection(),
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

            var generator = new CSharpCodeGenerator();
            var result = generator.Write(method);

            AssertExtensions.StringEquals(@"[TestAttribute(Name1 = ""arg1"", Name2 = ""arg2"")]
void Sample()
{
}
", result);
        }

        [Fact]
        public void CSharpCodeGenerator_CustomAttributes_WithMixedUnnamedAndNamedArguments()
        {
            var method = new MethodDeclaration("Sample")
            {
                Statements = new StatementCollection(),
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

            var generator = new CSharpCodeGenerator();
            var result = generator.Write(method);

            AssertExtensions.StringEquals(@"[TestAttribute(""arg1"", ""arg3"", Name2 = ""arg2"")]
void Sample()
{
}
", result);
        }

        [Fact]
        public void CSharpCodeGenerator_CustomAttributes_MultipleAttributes()
        {
            var method = new MethodDeclaration("Sample")
            {
                Statements = new StatementCollection(),
                CustomAttributes =
                {
                    new CustomAttribute(new TypeReference("TestAttribute1")),
                    new CustomAttribute(new TypeReference("TestAttribute2")),
                },
            };

            var generator = new CSharpCodeGenerator();
            var result = generator.Write(method);

            AssertExtensions.StringEquals(@"[TestAttribute1]
[TestAttribute2]
void Sample()
{
}
", result);
        }

        [Fact]
        public void CSharpCodeGenerator_Method_GenericParameter()
        {
            var method = new MethodDeclaration("Sample")
            {
                Statements = new StatementCollection(),
                Parameters =
                {
                    new TypeParameter("T"),
                },
            };

            var generator = new CSharpCodeGenerator();
            var result = generator.Write(method);

            AssertExtensions.StringEquals(@"void Sample<T>()
{
}
", result);
        }

        [Fact]
        public void CSharpCodeGenerator_Method_GenericParameterWithConstraint()
        {
            var method = new MethodDeclaration("Sample")
            {
                Statements = new StatementCollection(),
                Parameters =
                {
                    new TypeParameter("T") { Constraints = { new ClassTypeParameterConstraint() } },
                },
            };

            var generator = new CSharpCodeGenerator();
            var result = generator.Write(method);

            AssertExtensions.StringEquals(@"void Sample<T>()
    where T : class
{
}
", result);
        }

        [Fact]
        public void CSharpCodeGenerator_Method_Abstract()
        {
            var method = new MethodDeclaration("Sample")
            {
                Modifiers = Modifiers.Protected | Modifiers.Abstract,
            };

            var generator = new CSharpCodeGenerator();
            var result = generator.Write(method);

            AssertExtensions.StringEquals(@"protected abstract void Sample();
", result);
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

            var generator = new CSharpCodeGenerator();
            var result = generator.Write(method);

            AssertExtensions.StringEquals(@"protected abstract void Sample<T>()
    where T : class
    ;
", result);
        }

        [Fact]
        public void CSharpCodeGenerator_Method_ExplicitImplementation()
        {
            var method = new MethodDeclaration("A")
            {
                PrivateImplementationType = new TypeReference("Foo.IBar"),
                Statements = new StatementCollection(),
            };

            var generator = new CSharpCodeGenerator();
            var result = generator.Write(method);

            AssertExtensions.StringEquals(@"void Foo.IBar.A()
{
}
", result);
        }

        [Fact]
        public void CSharpCodeGenerator_Property_ExplicitImplementation()
        {
            var prop = new PropertyDeclaration("A", typeof(int))
            {
                PrivateImplementationType = new TypeReference("Foo.IBar"),
                Getter = new ReturnStatement(10),
            };

            var generator = new CSharpCodeGenerator();
            var result = generator.Write(prop);

            AssertExtensions.StringEquals(@"int Foo.IBar.A
{
    get
    {
        return 10;
    }
}
", result);
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

            var generator = new CSharpCodeGenerator();
            var result = generator.Write(prop);

            AssertExtensions.StringEquals(@"int A
{
    private get
    {
        return 10;
    }
}
", result);
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

            var generator = new CSharpCodeGenerator();
            var result = generator.Write(prop);

            AssertExtensions.StringEquals(@"int A
{
    internal set
    {
    }
}
", result);
        }

        [Fact]
        public void CSharpCodeGenerator_Property_GenericType()
        {
            var prop = new PropertyDeclaration("A", new TypeReference(typeof(Nullable<>)).MakeGeneric(typeof(int)))
            {
                Setter = new PropertyAccessorDeclaration(),
            };

            var generator = new CSharpCodeGenerator();
            var result = generator.Write(prop);

            AssertExtensions.StringEquals(@"System.Nullable<int> A
{
    set
    {
    }
}
", result);
        }

        [Fact]
        public void CSharpCodeGenerator_Event_ExplicitImplementation()
        {
            var method = new EventFieldDeclaration("A", typeof(EventHandler))
            {
                PrivateImplementationType = new TypeReference("Foo.IBar"),
                AddAccessor = new StatementCollection(),
                RemoveAccessor = new StatementCollection(),
            };

            var generator = new CSharpCodeGenerator();
            var result = generator.Write(method);

            AssertExtensions.StringEquals(@"event System.EventHandler Foo.IBar.A
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

        [Fact]
        public void CSharpCodeGenerator_ExpressionStatement()
        {
            var statement = new ExpressionStatement(new NewObjectExpression(new TypeReference("Disposable")));

            var generator = new CSharpCodeGenerator();
            var result = generator.Write(statement);

            AssertExtensions.StringEquals(@"new Disposable();
", result);
        }

        [Fact]
        public void CSharpCodeGenerator_UsingDirective()
        {
            var directive = new UsingDirective("System");

            var generator = new CSharpCodeGenerator();
            var result = generator.Write(directive);

            AssertExtensions.StringEquals("using System;", result);
        }

        [Fact]
        public void CSharpCodeGenerator_UsingStatement_WithoutBody()
        {
            var statement = new UsingStatement
            {
                Statement = new NewObjectExpression(new TypeReference("Disposable")),
            };

            var generator = new CSharpCodeGenerator();
            var result = generator.Write(statement);

            AssertExtensions.StringEquals(@"using (new Disposable())
{
}
", result);
        }

        [Fact]
        public void CSharpCodeGenerator_UsingStatement_WithBody()
        {
            var statement = new UsingStatement
            {
                Statement = new VariableDeclarationStatement(type: null, "disposable", new NewObjectExpression(new TypeReference("Disposable"))),
                Body = (Statement)new MethodInvokeExpression(new VariableReferenceExpression("disposable")),
            };

            var generator = new CSharpCodeGenerator();
            var result = generator.Write(statement);

            AssertExtensions.StringEquals(@"using (var disposable = new Disposable())
{
    disposable();
}
", result);
        }

        [Fact]
        public void CSharpCodeGenerator_MethodInvoke()
        {
            var expression = new MethodInvokeExpression(
                new MemberReferenceExpression(new TypeReference("Console"), "Write"),
                "test");

            var generator = new CSharpCodeGenerator();
            var result = generator.Write(expression);

            AssertExtensions.StringEquals(@"Console.Write(""test"")", result);
        }

        [Fact]
        public void CSharpCodeGenerator_MethodInvokeWithGenericParameters()
        {
            var expression = new MethodInvokeExpression(
                new MemberReferenceExpression(new TypeReference("Console"), "Write"),
                "test");
            expression.Parameters.Add(typeof(string));

            var generator = new CSharpCodeGenerator();
            var result = generator.Write(expression);

            AssertExtensions.StringEquals(@"Console.Write<string>(""test"")", result);
        }

        [Fact]
        public void CSharpCodeGenerator_MethodInvoke_OutArgument()
        {
            var expression = new MethodInvokeExpression(
                new MemberReferenceExpression(new TypeReference("Console"), "Write"),
                new MethodInvokeArgumentExpression(new VariableReferenceExpression("test")) { Direction = Direction.Out });

            var generator = new CSharpCodeGenerator();
            var result = generator.Write(expression);

            AssertExtensions.StringEquals("Console.Write(out test)", result);
        }

        [Fact]
        public void CSharpCodeGenerator_Iteration()
        {
            var statement = new IterationStatement();
            var variable = new VariableDeclarationStatement(type: null, "i", 0);
            statement.Initialization = variable;
            statement.Condition = new BinaryExpression(BinaryOperator.LessThan, variable, 10);
            statement.IncrementStatement = new UnaryExpression(UnaryOperator.PostIncrement, variable);
            statement.Body = new MethodInvokeExpression(
                new MemberReferenceExpression(new TypeReference("Console"), "Write"),
                variable);

            var generator = new CSharpCodeGenerator();
            var result = generator.Write(statement);

            AssertExtensions.StringEquals(@"for (var i = 0; (i < 10); (i++))
{
    Console.Write(i);
}
", result);
        }

        [Fact]
        public void CSharpCodeGenerator_Iteration_Empty()
        {
            var statement = new IterationStatement();
            var generator = new CSharpCodeGenerator();
            var result = generator.Write(statement);

            AssertExtensions.StringEquals(@"for (; ; )
{
}
", result);
        }

        [Fact]
        public void CSharpCodeGenerator_TypeOf()
        {
            var statement = new TypeOfExpression(typeof(string));
            var generator = new CSharpCodeGenerator();
            var result = generator.Write(statement);

            AssertExtensions.StringEquals("typeof(string)", result);
        }

        [Fact]
        public void CSharpCodeGenerator_NextLoopIteration()
        {
            var statement = new GotoNextLoopIterationStatement();
            var generator = new CSharpCodeGenerator();
            var result = generator.Write(statement);

            AssertExtensions.StringEquals(@"continue;
", result);
        }

        [Fact]
        public void CSharpCodeGenerator_CodeExitLoop()
        {
            var statement = new ExitLoopStatement();
            var generator = new CSharpCodeGenerator();
            var result = generator.Write(statement);

            AssertExtensions.StringEquals(@"break;
", result);
        }

        [Fact]
        public void CSharpCodeGenerator_Return()
        {
            var statement = new ReturnStatement();
            var generator = new CSharpCodeGenerator();
            var result = generator.Write(statement);

            AssertExtensions.StringEquals(@"return;
", result);
        }

        [Fact]
        public void CSharpCodeGenerator_ReturnExpression()
        {
            var statement = new ReturnStatement(10);
            var generator = new CSharpCodeGenerator();
            var result = generator.Write(statement);

            AssertExtensions.StringEquals(@"return 10;
", result);
        }

        [Fact]
        public void CSharpCodeGenerator_YieldReturnExpression()
        {
            var statement = new YieldReturnStatement(10);
            var generator = new CSharpCodeGenerator();
            var result = generator.Write(statement);

            AssertExtensions.StringEquals(@"yield return 10;
", result);
        }

        [Fact]
        public void CSharpCodeGenerator_YieldBreak()
        {
            var statement = new YieldBreakStatement();
            var generator = new CSharpCodeGenerator();
            var result = generator.Write(statement);

            AssertExtensions.StringEquals(@"yield break;
", result);
        }

        [Fact]
        public void CSharpCodeGenerator_Await()
        {
            var statement = new AwaitExpression(new VariableReferenceExpression("awaitable"));
            var generator = new CSharpCodeGenerator();
            var result = generator.Write(statement);

            AssertExtensions.StringEquals("await awaitable", result);
        }

        [Fact]
        public void CSharpCodeGenerator_Comment()
        {
            var statement = new CommentStatement("test");
            var generator = new CSharpCodeGenerator();
            var result = generator.Write(statement);

            AssertExtensions.StringEquals(@"// test
", result);
        }

        [Fact]
        public void CSharpCodeGenerator_CommentMultiLine()
        {
            var statement = new CommentStatement("test1" + Environment.NewLine + Environment.NewLine + "test2");
            var generator = new CSharpCodeGenerator();
            var result = generator.Write(statement);

            AssertExtensions.StringEquals(@"// test1
//
// test2
", result);
        }

        [Fact]
        public void CSharpCodeGenerator_CommentNull()
        {
            var statement = new CommentStatement();
            var generator = new CSharpCodeGenerator();
            var result = generator.Write(statement);

            AssertExtensions.StringEquals(@"//
", result);
        }

        [Fact]
        public void CSharpCodeGenerator_ExpressionCommentBeforeAndAfter()
        {
            var expression = new SnippetExpression("code");
            expression.CommentsBefore.Add("comment1");
            expression.CommentsAfter.Add("comment2");
            var generator = new CSharpCodeGenerator();
            var result = generator.Write(expression);

            AssertExtensions.StringEquals("/* comment1 */ code /* comment2 */", result);
        }

        [Fact]
        public void CSharpCodeGenerator_ExpressionCommentBefore()
        {
            var expression = new SnippetExpression("code");
            expression.CommentsBefore.Add("comment1");
            var generator = new CSharpCodeGenerator();
            var result = generator.Write(expression);

            AssertExtensions.StringEquals("/* comment1 */ code", result);
        }

        [Fact]
        public void CSharpCodeGenerator_ExpressionCommentAfter()
        {
            var expression = new SnippetExpression("code");
            expression.CommentsAfter.Add("comment2");
            var generator = new CSharpCodeGenerator();
            var result = generator.Write(expression);

            AssertExtensions.StringEquals("code /* comment2 */", result);
        }

        [Fact]
        public void CSharpCodeGenerator_ExpressionCommentAfterWithInlineCommentEnd()
        {
            var expression = new SnippetExpression("code");
            expression.CommentsAfter.Add("comment with */ in the middle");
            var generator = new CSharpCodeGenerator();
            var result = generator.Write(expression);

            AssertExtensions.StringEquals(@"code // comment with */ in the middle
", result);
        }

        [Fact]
        public void CSharpCodeGenerator_ExpressionCommentBeforeWithInlineCommentEnd()
        {
            var expression = new SnippetExpression("code");
            expression.CommentsBefore.Add("comment with */ in the middle");
            var generator = new CSharpCodeGenerator();
            var result = generator.Write(expression);

            AssertExtensions.StringEquals(@"// comment with */ in the middle
code", result);
        }

        [Fact]
        public void CSharpCodeGenerator_ExpressionCommentsBefore_Inlines()
        {
            var expression = new SnippetExpression("code");
            expression.CommentsBefore.Add("com1");
            expression.CommentsBefore.Add("com2");
            var generator = new CSharpCodeGenerator();
            var result = generator.Write(expression);

            AssertExtensions.StringEquals("/* com1 */ /* com2 */ code", result);
        }

        [Fact]
        public void CSharpCodeGenerator_ExpressionCommentsBefore_LineAndInline()
        {
            var expression = new SnippetExpression("code");
            expression.CommentsBefore.Add("com1", CommentType.LineComment);
            expression.CommentsBefore.Add("com2");
            var generator = new CSharpCodeGenerator();
            var result = generator.Write(expression);

            AssertExtensions.StringEquals(@"// com1
/* com2 */ code", result);
        }

        [Fact]
        public void CSharpCodeGenerator_ExpressionCommentsAfter_LineAndInline()
        {
            var expression = new SnippetExpression("code");
            expression.CommentsAfter.Add("com1", CommentType.LineComment);
            expression.CommentsAfter.Add("com2");
            var generator = new CSharpCodeGenerator();
            var result = generator.Write(expression);

            AssertExtensions.StringEquals(@"code // com1
/* com2 */", result);
        }

        [Fact]
        public void CSharpCodeGenerator_StatementCommentsAfter_Line()
        {
            var expression = new ReturnStatement();
            expression.CommentsAfter.Add("com1", CommentType.LineComment);
            expression.CommentsAfter.Add("com2");
            var generator = new CSharpCodeGenerator();
            var result = generator.Write(expression);

            AssertExtensions.StringEquals(@"return;
// com1
// com2
", result);
        }

        [Fact]
        public void CSharpCodeGenerator_MethodXmlDocmentation()
        {
            var method = new MethodDeclaration("Sample")
            {
                Statements = new StatementCollection(),
            };

            method.XmlComments.AddSummary("Test");
            var generator = new CSharpCodeGenerator();
            var result = generator.Write(method);

            AssertExtensions.StringEquals(@"/// <summary>Test</summary>
void Sample()
{
}
", result);
        }

        [Fact]
        public void CSharpCodeGenerator_TypeReference()
        {
            var expression = new TypeReferenceExpression(typeof(Console));

            var generator = new CSharpCodeGenerator();
            var result = generator.Write(expression);

            AssertExtensions.StringEquals("System.Console", result);
        }

        [Fact]
        public void CSharpCodeGenerator_TypeReference_Nested()
        {
            var expression = new TypeReferenceExpression(typeof(SampleEnum));

            var generator = new CSharpCodeGenerator();
            var result = generator.Write(expression);

            AssertExtensions.StringEquals("Meziantou.Framework.CodeDom.Tests.CSharpCodeGeneratorTests.SampleEnum", result);
        }

        [Fact]
        public void CSharpCodeGenerator_TypeReference_Generic()
        {
            var expression = new TypeReferenceExpression(typeof(Sample<int>));

            var generator = new CSharpCodeGenerator();
            var result = generator.Write(expression);

            AssertExtensions.StringEquals("Meziantou.Framework.CodeDom.Tests.CSharpCodeGeneratorTests.Sample<int>", result);
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

            var generator = new CSharpCodeGenerator();
            var result = generator.Write(member);

            AssertExtensions.StringEquals(@"public static implicit operator int(long value)
{
    return value;
}
", result);
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

            var generator = new CSharpCodeGenerator();
            var result = generator.Write(member);

            AssertExtensions.StringEquals(@"public static implicit operator Test(long value)
{
    return value;
}
", result);
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

            var generator = new CSharpCodeGenerator();
            var result = generator.Write(member);

            AssertExtensions.StringEquals(@"public static explicit operator int(long value)
{
    return value;
}
", result);
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

            var generator = new CSharpCodeGenerator();
            var result = generator.Write(member);

            AssertExtensions.StringEquals(@"public static int operator +(int a, int b)
{
    return value;
}
", result);
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

            var generator = new CSharpCodeGenerator();
            var result = generator.Write(expression);

            AssertExtensions.StringEquals("Meziantou.Framework.CodeDom.Tests.CSharpCodeGeneratorTests.SampleEnum.A", result);
        }

        [Fact]
        public void CSharpCodeGenerator_CodeExpressionFromEnum_DefinedCombinaison()
        {
            Expression expression = SampleEnum.All;

            var generator = new CSharpCodeGenerator();
            var result = generator.Write(expression);

            AssertExtensions.StringEquals("Meziantou.Framework.CodeDom.Tests.CSharpCodeGeneratorTests.SampleEnum.All", result);
        }

        [Fact]
        public void CSharpCodeGenerator_CodeExpressionFromEnum_Combinaison()
        {
            Expression expression = (SampleEnum)3;

            var generator = new CSharpCodeGenerator();
            var result = generator.Write(expression);

            AssertExtensions.StringEquals("((Meziantou.Framework.CodeDom.Tests.CSharpCodeGeneratorTests.SampleEnum)3)", result);
        }

        [Fact]
        public void CSharpCodeGenerator_CodeExpressionFromEnum_UndefinedValue()
        {
            Expression expression = (SampleEnum)10;

            var generator = new CSharpCodeGenerator();
            var result = generator.Write(expression);

            AssertExtensions.StringEquals("((Meziantou.Framework.CodeDom.Tests.CSharpCodeGeneratorTests.SampleEnum)10)", result);
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
            var generator = new CSharpCodeGenerator();
            var result = generator.Write(expression);

            AssertExtensions.StringEquals($"(1 {symbol} 2)", result);
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
            var generator = new CSharpCodeGenerator();
            var result = generator.Write(expression);

            AssertExtensions.StringEquals($"({symbol}1)", result);
        }

        [Theory]
        [InlineData(UnaryOperator.PostIncrement, "++")]
        [InlineData(UnaryOperator.PostDecrement, "--")]
        public void CSharpCodeGenerator_UnaryExpression_Post(UnaryOperator op, string symbol)
        {
            var expression = new UnaryExpression(op, 1);
            var generator = new CSharpCodeGenerator();
            var result = generator.Write(expression);

            AssertExtensions.StringEquals($"(1{symbol})", result);
        }

        [Fact]
        public void CSharpCodeGenerator_Spaces_MultipleStatements()
        {
            var method = new MethodDeclaration("Test")
            {
                Statements = new StatementCollection()
                {
                    new AssignStatement(new VariableReferenceExpression("a") , 0),
                    new AssignStatement(new VariableReferenceExpression("b") , 0),
                },
            };

            var generator = new CSharpCodeGenerator();
            var result = generator.Write(method);

            AssertExtensions.StringEquals(@"void Test()
{
    a = 0;
    b = 0;
}
", result);
        }

        [Fact]
        public void CSharpCodeGenerator_Spaces_MultipleMethods()
        {
            var c = new ClassDeclaration("Test");
            c.Members.Add(new MethodDeclaration("A"));
            c.Members.Add(new MethodDeclaration("B"));

            var generator = new CSharpCodeGenerator();
            var result = generator.Write(c);

            AssertExtensions.StringEquals(@"class Test
{
    void A();

    void B();
}
", result);
        }

        [Fact]
        public void CSharpCodeGenerator_Spaces_Brackets()
        {
            var method = new MethodDeclaration("Test")
            {
                Statements = new StatementCollection()
                {
                    new ConditionStatement()
                    {
                        Condition = new LiteralExpression(value: true),
                        TrueStatements = new StatementCollection()
                        {
                            new ConditionStatement()
                            {
                                Condition = new LiteralExpression(value: true),
                                TrueStatements = new StatementCollection(),
                            },
                        },
                    },
                    new AssignStatement(new VariableReferenceExpression("a") , 0),
                    new AssignStatement(new VariableReferenceExpression("b") , 0),
                },
            };

            var generator = new CSharpCodeGenerator();
            var result = generator.Write(method);

            AssertExtensions.StringEquals(@"void Test()
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
", result);
        }

        [Fact]
        public void CSharpCodeGenerator_Modifiers_PartialReadOnlyStruct()
        {
            var type = new StructDeclaration("Test")
            {
                Modifiers = Modifiers.Partial | Modifiers.ReadOnly,
            };

            var generator = new CSharpCodeGenerator();
            var result = generator.Write(type);

            AssertExtensions.StringEquals(@"readonly partial struct Test
{
}
", result);
        }

        [Fact]
        public void CSharpCodeGenerator_NewArray()
        {
            var type = new NewArrayExpression(typeof(int), 1, 2);

            var generator = new CSharpCodeGenerator();
            var result = generator.Write(type);

            AssertExtensions.StringEquals(@"new int[1, 2]", result);
        }
    }
}
