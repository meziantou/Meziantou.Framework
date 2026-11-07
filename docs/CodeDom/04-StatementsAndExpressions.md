---
title: Statements and expressions
description: Write the body of members with statements, expressions, type references and code snippets.
---

## Expressions

An [Expression](cref:Meziantou.Framework.CodeDom.Expression) is a value: a literal, a reference to a variable or a member, an operation, a method call, an object
creation... Expressions nest, and the generator writes the parentheses:

<!-- ref:../../tests/Meziantou.Framework.CodeDom.Tests/CSharpCodeGeneratorTests.cs;region=Expression_Example;format=md-fenced-code-block;dedent=true -->
```csharp
Expression expr1 = new LiteralExpression(42);
Expression expr2 = new BinaryExpression(BinaryOperator.Add, expr1, 10);
Expression expr3 = new MethodInvokeExpression(new MemberReferenceExpression(typeof(Console), "WriteLine"), expr2);

Assert.Equal("global::System.Console.WriteLine((42 + 10))", expr3.ToCsharpString());
```
<!-- endref -->

| Class | Writes |
| --- | --- |
| [LiteralExpression](cref:Meziantou.Framework.CodeDom.LiteralExpression) | `42`, `"text"`, `true`, `null`. Numbers, strings and `bool` values convert to it. |
| [VariableReferenceExpression](cref:Meziantou.Framework.CodeDom.VariableReferenceExpression), [ArgumentReferenceExpression](cref:Meziantou.Framework.CodeDom.ArgumentReferenceExpression) | A local variable or an argument. A [VariableDeclarationStatement](cref:Meziantou.Framework.CodeDom.VariableDeclarationStatement) or a [MethodArgumentDeclaration](cref:Meziantou.Framework.CodeDom.MethodArgumentDeclaration) converts to it. |
| [MemberReferenceExpression](cref:Meziantou.Framework.CodeDom.MemberReferenceExpression) | `target.Member`. A member declaration converts to `this.Member`. |
| [ThisExpression](cref:Meziantou.Framework.CodeDom.ThisExpression), [BaseExpression](cref:Meziantou.Framework.CodeDom.BaseExpression), [ValueArgumentExpression](cref:Meziantou.Framework.CodeDom.ValueArgumentExpression) | `this`, `base`, `value`. |
| [BinaryExpression](cref:Meziantou.Framework.CodeDom.BinaryExpression), [UnaryExpression](cref:Meziantou.Framework.CodeDom.UnaryExpression) | An operator applied to one or two operands: [BinaryOperator](cref:Meziantou.Framework.CodeDom.BinaryOperator), [UnaryOperator](cref:Meziantou.Framework.CodeDom.UnaryOperator). |
| [MethodInvokeExpression](cref:Meziantou.Framework.CodeDom.MethodInvokeExpression) | A method call, with generic arguments and `out` or `ref` arguments. |
| [NewObjectExpression](cref:Meziantou.Framework.CodeDom.NewObjectExpression), [NewArrayExpression](cref:Meziantou.Framework.CodeDom.NewArrayExpression) | `new T(...)` and `new T[...]`. |
| [CastExpression](cref:Meziantou.Framework.CodeDom.CastExpression), [ConvertExpression](cref:Meziantou.Framework.CodeDom.ConvertExpression), [IsInstanceOfTypeExpression](cref:Meziantou.Framework.CodeDom.IsInstanceOfTypeExpression) | `(T)value`, `value as T`, `value is T`. |
| [TypeOfExpression](cref:Meziantou.Framework.CodeDom.TypeOfExpression), [NameofExpression](cref:Meziantou.Framework.CodeDom.NameofExpression), [DefaultValueExpression](cref:Meziantou.Framework.CodeDom.DefaultValueExpression) | `typeof(T)`, `nameof(x)`, `default(T)`. |
| [ArrayIndexerExpression](cref:Meziantou.Framework.CodeDom.ArrayIndexerExpression) | `array[index]`. |
| [AwaitExpression](cref:Meziantou.Framework.CodeDom.AwaitExpression) | `await value`. |
| [TypeReferenceExpression](cref:Meziantou.Framework.CodeDom.TypeReferenceExpression) | A type, as the target of a static member. A [TypeReference](cref:Meziantou.Framework.CodeDom.TypeReference) converts to it. |
| [SnippetExpression](cref:Meziantou.Framework.CodeDom.SnippetExpression) | Any C# code, written as is. |

<!-- ref:../../tests/Meziantou.Framework.CodeDom.Tests/CSharpCodeGeneratorTests.cs;region=LiteralExpression_Example;format=md-fenced-code-block;dedent=true -->
```csharp
var intLiteral = new LiteralExpression(42);
var stringLiteral = new LiteralExpression("Hello");
var boolLiteral = new LiteralExpression(true);
var nullLiteral = new LiteralExpression(null);

Assert.Equal("42", intLiteral.ToCsharpString());
Assert.Equal("\"Hello\"", stringLiteral.ToCsharpString());
Assert.Equal("true", boolLiteral.ToCsharpString());
Assert.Equal("null", nullLiteral.ToCsharpString());
```
<!-- endref -->

<!-- ref:../../tests/Meziantou.Framework.CodeDom.Tests/CSharpCodeGeneratorTests.cs;region=BinaryExpression_Example;format=md-fenced-code-block;dedent=true -->
```csharp
var addition = new BinaryExpression(BinaryOperator.Add, 10, 20);
var comparison = new BinaryExpression(BinaryOperator.GreaterThan, new VariableReferenceExpression("x"), 0);
var logicalAnd = new BinaryExpression(
    BinaryOperator.And,
    new BinaryExpression(BinaryOperator.GreaterThan, new VariableReferenceExpression("x"), 0),
    new BinaryExpression(BinaryOperator.LessThan, new VariableReferenceExpression("x"), 100));

Assert.Equal("(10 + 20)", addition.ToCsharpString());
Assert.Equal("(x > 0)", comparison.ToCsharpString());
Assert.Equal("((x > 0) && (x < 100))", logicalAnd.ToCsharpString());
```
<!-- endref -->

<!-- ref:../../tests/Meziantou.Framework.CodeDom.Tests/CSharpCodeGeneratorTests.cs;region=MemberReferenceExpression_Example;format=md-fenced-code-block;dedent=true -->
```csharp
var memberRef = new MemberReferenceExpression(new ThisExpression(), "Name");
var staticMemberRef = new MemberReferenceExpression(typeof(Console), "WriteLine");

Assert.Equal("this.Name", memberRef.ToCsharpString());
Assert.Equal("global::System.Console.WriteLine", staticMemberRef.ToCsharpString());
```
<!-- endref -->

<!-- ref:../../tests/Meziantou.Framework.CodeDom.Tests/CSharpCodeGeneratorTests.cs;region=MethodInvokeExpression_Example;format=md-fenced-code-block;dedent=true -->
```csharp
var method = new MemberReferenceExpression(typeof(Console), "WriteLine");
var invoke = new MethodInvokeExpression(method, new LiteralExpression("Hello World"));

Assert.Equal("global::System.Console.WriteLine(\"Hello World\")", invoke.ToCsharpString());
```
<!-- endref -->

<!-- ref:../../tests/Meziantou.Framework.CodeDom.Tests/CSharpCodeGeneratorTests.cs;region=NewObjectExpression_Example;format=md-fenced-code-block;dedent=true -->
```csharp
var newObj = new NewObjectExpression(typeof(StringBuilder));
var newObjWithArgs = new NewObjectExpression(typeof(StringBuilder), new LiteralExpression("initial value"));

Assert.Equal("new global::System.Text.StringBuilder()", newObj.ToCsharpString());
Assert.Equal("new global::System.Text.StringBuilder(\"initial value\")", newObjWithArgs.ToCsharpString());
```
<!-- endref -->

[Expression](cref:Meziantou.Framework.CodeDom.Expression) has factory methods for the common cases: [Expression.Add](cref:Meziantou.Framework.CodeDom.Expression.Add), [And](cref:Meziantou.Framework.CodeDom.Expression.And) and [Or](cref:Meziantou.Framework.CodeDom.Expression.Or) chain several operands,
[EqualsNull](cref:Meziantou.Framework.CodeDom.Expression.EqualsNull) and [NotEqualsNull](cref:Meziantou.Framework.CodeDom.Expression.NotEqualsNull) compare with `null`, [IsNullOrEmpty](cref:Meziantou.Framework.CodeDom.Expression.IsNullOrEmpty) calls `string.IsNullOrEmpty`, and [Null()](cref:Meziantou.Framework.CodeDom.Expression.Null),
[True()](cref:Meziantou.Framework.CodeDom.Expression.True) and [False()](cref:Meziantou.Framework.CodeDom.Expression.False) create the literals. The [Member](cref:Meziantou.Framework.CodeDom.Expression.Member) and [InvokeMethod](cref:Meziantou.Framework.CodeDom.Extensions.InvokeMethod) extension methods chain member accesses and
calls on any expression.

## Statements

A [Statement](cref:Meziantou.Framework.CodeDom.Statement) is an instruction of a method, a constructor or an accessor. The [Statements](cref:Meziantou.Framework.CodeDom.MethodDeclaration.Statements) of a member is a
[StatementCollection](cref:Meziantou.Framework.CodeDom.StatementCollection): [Add](cref:Meziantou.Framework.CodeDom.StatementCollection.Add) attaches a statement and returns it, so a variable declaration can be referenced afterwards,
and an expression converts to an expression statement.

| Class | Writes |
| --- | --- |
| [VariableDeclarationStatement](cref:Meziantou.Framework.CodeDom.VariableDeclarationStatement) | `T name = value;` |
| [AssignStatement](cref:Meziantou.Framework.CodeDom.AssignStatement) | `target = value;` |
| [ExpressionStatement](cref:Meziantou.Framework.CodeDom.ExpressionStatement) | An expression used as a statement, such as a method call. |
| [ReturnStatement](cref:Meziantou.Framework.CodeDom.ReturnStatement), [YieldReturnStatement](cref:Meziantou.Framework.CodeDom.YieldReturnStatement), [YieldBreakStatement](cref:Meziantou.Framework.CodeDom.YieldBreakStatement) | `return`, `yield return`, `yield break`. |
| [ConditionStatement](cref:Meziantou.Framework.CodeDom.ConditionStatement) | `if` with [TrueStatements](cref:Meziantou.Framework.CodeDom.ConditionStatement.TrueStatements) and [FalseStatements](cref:Meziantou.Framework.CodeDom.ConditionStatement.FalseStatements). |
| [IterationStatement](cref:Meziantou.Framework.CodeDom.IterationStatement), [WhileStatement](cref:Meziantou.Framework.CodeDom.WhileStatement) | `for` and `while` loops. |
| [ExitLoopStatement](cref:Meziantou.Framework.CodeDom.ExitLoopStatement), [GotoNextLoopIterationStatement](cref:Meziantou.Framework.CodeDom.GotoNextLoopIterationStatement) | `break` and `continue`. |
| [TryCatchFinallyStatement](cref:Meziantou.Framework.CodeDom.TryCatchFinallyStatement) | `try`, `catch` clauses and `finally`. |
| [ThrowStatement](cref:Meziantou.Framework.CodeDom.ThrowStatement) | `throw`. |
| [UsingStatement](cref:Meziantou.Framework.CodeDom.UsingStatement) | `using`. |
| [AddEventHandlerStatement](cref:Meziantou.Framework.CodeDom.AddEventHandlerStatement), [RemoveEventHandlerStatement](cref:Meziantou.Framework.CodeDom.RemoveEventHandlerStatement) | `+=` and `-=` on an event. |
| [CommentStatement](cref:Meziantou.Framework.CodeDom.CommentStatement) | A line comment. |
| [SnippetStatement](cref:Meziantou.Framework.CodeDom.SnippetStatement) | Any C# code, written as is. |

<!-- ref:../../tests/Meziantou.Framework.CodeDom.Tests/CSharpCodeGeneratorTests.cs;region=CodeDom_Statements;format=md-fenced-code-block;dedent=true -->
```csharp
var method = new MethodDeclaration("Sum")
{
    Modifiers = Modifiers.Public | Modifiers.Static,
    ReturnType = typeof(int),
};
var values = method.AddArgument("values", typeof(int[]));
method.Statements = [];

// Throws an ArgumentNullException when values is null
method.Statements.Add(values.CreateThrowIfNullStatement());

var total = method.Statements.Add(new VariableDeclarationStatement("total", typeof(int), 0));
var index = new VariableDeclarationStatement("i", typeof(int), 0);
method.Statements.Add(new IterationStatement
{
    Initialization = index,
    Condition = new BinaryExpression(BinaryOperator.LessThan, index, values.Member("Length")),
    IncrementStatement = new UnaryExpression(UnaryOperator.PostIncrement, index),
    Body = new AssignStatement(total, new BinaryExpression(BinaryOperator.Add, total, new ArrayIndexerExpression(values, index))),
});
method.Statements.Add(new ReturnStatement(total));
```
<!-- endref -->

The method is written as:

<!-- ref:../../tests/Meziantou.Framework.CodeDom.Tests/CSharpCodeGeneratorTests.cs;region=CodeDom_StatementsOutput;format=md-fenced-code-block;dedent=true -->
```csharp
public static int Sum(int[] values)
{
    if ((values == null))
    {
        throw new global::System.ArgumentNullException(nameof(values));
    }

    int total = 0;
    for (int i = 0; (i < values.Length); (i++))
    {
        total = (total + values[i]);
    }

    return total;
}
```
<!-- endref -->

[CreateThrowIfNullStatement](cref:Meziantou.Framework.CodeDom.Extensions.CreateThrowIfNullStatement) is an extension method of [MethodArgumentDeclaration](cref:Meziantou.Framework.CodeDom.MethodArgumentDeclaration) that writes the `null` check of an
argument.

## Type references

A [TypeReference](cref:Meziantou.Framework.CodeDom.TypeReference) designates a type. It converts from a `Type`, and can be created from a name, made generic, made an
array or made nullable:

<!-- ref:../../tests/Meziantou.Framework.CodeDom.Tests/TypeReferenceTests.cs;region=TypeReference_Example;format=md-fenced-code-block;dedent=true -->
```csharp
var stringType = new TypeReference(typeof(string));
var listOfInt = new TypeReference(typeof(List<>)).MakeGeneric(new TypeReference(typeof(int)));
var intArray = new TypeReference(typeof(int)).MakeArray(1);

Assert.Equal("System.String", stringType.ClrFullTypeName);
Assert.Equal("System.Collections.Generic.List<System.Int32>", listOfInt.ClrFullTypeName);
Assert.Equal("System.Int32[]", intArray.ClrFullTypeName);
```
<!-- endref -->

A [TypeDeclaration](cref:Meziantou.Framework.CodeDom.TypeDeclaration) and a [TypeParameter](cref:Meziantou.Framework.CodeDom.TypeParameter) also convert to a [TypeReference](cref:Meziantou.Framework.CodeDom.TypeReference), so the types you generate can be used as
the types of members.

## Snippets

[SnippetExpression](cref:Meziantou.Framework.CodeDom.SnippetExpression) and [SnippetStatement](cref:Meziantou.Framework.CodeDom.SnippetStatement) write their text without any change. Use them for the constructs that have no
code object, such as a pattern or a lambda expression. The text is not validated: you are responsible for its syntax.
