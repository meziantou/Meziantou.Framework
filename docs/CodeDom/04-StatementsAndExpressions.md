---
title: Statements and expressions
description: Write the body of members with statements, expressions, type references and code snippets.
---

## Expressions

An `Expression` is a value: a literal, a reference to a variable or a member, an operation, a method call, an object
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
| `LiteralExpression` | `42`, `"text"`, `true`, `null`. Numbers, strings and `bool` values convert to it. |
| `VariableReferenceExpression`, `ArgumentReferenceExpression` | A local variable or an argument. A `VariableDeclarationStatement` or a `MethodArgumentDeclaration` converts to it. |
| `MemberReferenceExpression` | `target.Member`. A member declaration converts to `this.Member`. |
| `ThisExpression`, `BaseExpression`, `ValueArgumentExpression` | `this`, `base`, `value`. |
| `BinaryExpression`, `UnaryExpression` | An operator applied to one or two operands: `BinaryOperator`, `UnaryOperator`. |
| `MethodInvokeExpression` | A method call, with generic arguments and `out` or `ref` arguments. |
| `NewObjectExpression`, `NewArrayExpression` | `new T(...)` and `new T[...]`. |
| `CastExpression`, `ConvertExpression`, `IsInstanceOfTypeExpression` | `(T)value`, `value as T`, `value is T`. |
| `TypeOfExpression`, `NameofExpression`, `DefaultValueExpression` | `typeof(T)`, `nameof(x)`, `default(T)`. |
| `ArrayIndexerExpression` | `array[index]`. |
| `AwaitExpression` | `await value`. |
| `TypeReferenceExpression` | A type, as the target of a static member. A `TypeReference` converts to it. |
| `SnippetExpression` | Any C# code, written as is. |

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

`Expression` has factory methods for the common cases: `Expression.Add`, `And` and `Or` chain several operands,
`EqualsNull` and `NotEqualsNull` compare with `null`, `IsNullOrEmpty` calls `string.IsNullOrEmpty`, and `Null()`,
`True()` and `False()` create the literals. The `Member` and `InvokeMethod` extension methods chain member accesses and
calls on any expression.

## Statements

A `Statement` is an instruction of a method, a constructor or an accessor. The `Statements` of a member is a
`StatementCollection`: `Add` attaches a statement and returns it, so a variable declaration can be referenced afterwards,
and an expression converts to an expression statement.

| Class | Writes |
| --- | --- |
| `VariableDeclarationStatement` | `T name = value;` |
| `AssignStatement` | `target = value;` |
| `ExpressionStatement` | An expression used as a statement, such as a method call. |
| `ReturnStatement`, `YieldReturnStatement`, `YieldBreakStatement` | `return`, `yield return`, `yield break`. |
| `ConditionStatement` | `if` with `TrueStatements` and `FalseStatements`. |
| `IterationStatement`, `WhileStatement` | `for` and `while` loops. |
| `ExitLoopStatement`, `GotoNextLoopIterationStatement` | `break` and `continue`. |
| `TryCatchFinallyStatement` | `try`, `catch` clauses and `finally`. |
| `ThrowStatement` | `throw`. |
| `UsingStatement` | `using`. |
| `AddEventHandlerStatement`, `RemoveEventHandlerStatement` | `+=` and `-=` on an event. |
| `CommentStatement` | A line comment. |
| `SnippetStatement` | Any C# code, written as is. |

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

`CreateThrowIfNullStatement` is an extension method of `MethodArgumentDeclaration` that writes the `null` check of an
argument.

## Type references

A `TypeReference` designates a type. It converts from a `Type`, and can be created from a name, made generic, made an
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

A `TypeDeclaration` and a `TypeParameter` also convert to a `TypeReference`, so the types you generate can be used as
the types of members.

## Snippets

`SnippetExpression` and `SnippetStatement` write their text without any change. Use them for the constructs that have no
code object, such as a pattern or a lambda expression. The text is not validated: you are responsible for its syntax.
