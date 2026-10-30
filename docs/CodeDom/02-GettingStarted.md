---
title: Getting started
description: Install the package, build a compilation unit, and write it as C# code.
---

## Install the package

```bash
dotnet add package Meziantou.Framework.CodeDom
```

The types are in the [Meziantou.Framework.CodeDom](cref:Meziantou.Framework.CodeDom) namespace. The examples of this documentation omit the `using`
directives, and check their results with assertions such as `Assert.Equal`.

## Build a file

A file is a [CompilationUnit](cref:Meziantou.Framework.CodeDom.CompilationUnit). Add `using` directives and namespaces to it, types to the namespaces, members to the
types, and statements to the members. The `Add*` extension methods attach an object to its parent and return it, so you
can keep building on it:

<!-- ref:../../tests/Meziantou.Framework.CodeDom.Tests/CSharpCodeGeneratorTests.cs;region=CodeDom_Usage;format=md-fenced-code-block;dedent=true -->
```csharp
var unit = new CompilationUnit();
unit.AddUsing("System");

var ns = unit.AddNamespace("Demo");
var type = ns.AddType(new ClassDeclaration("Greeter")
{
    Modifiers = Modifiers.Public,
});

var method = type.AddMember(new MethodDeclaration("GetMessage")
{
    Modifiers = Modifiers.Public | Modifiers.Static,
    ReturnType = typeof(string),
});

var nameArgument = method.AddArgument("name", typeof(string));
method.Statements = new ReturnStatement(Expression.Add("Hello ", nameArgument, "!"));

var generatedCode = unit.ToCsharpString();
```
<!-- endref -->

- [AddUsing](cref:Meziantou.Framework.CodeDom.Extensions.AddUsing), [AddNamespace](cref:Meziantou.Framework.CodeDom.Extensions.AddNamespace), [AddType](cref:Meziantou.Framework.CodeDom.Extensions.AddType), [AddMember](cref:Meziantou.Framework.CodeDom.Extensions.AddMember) and [AddArgument](cref:Meziantou.Framework.CodeDom.Extensions.AddArgument) create and attach the declarations.
- `ReturnType = typeof(string)` converts the `Type` to a [TypeReference](cref:Meziantou.Framework.CodeDom.TypeReference).
- [Expression.Add](cref:Meziantou.Framework.CodeDom.Expression.Add) builds `"Hello " + name + "!"`. The string literals convert to [LiteralExpression](cref:Meziantou.Framework.CodeDom.LiteralExpression), and the argument
  declaration to a reference to the argument.
- [Statements](cref:Meziantou.Framework.CodeDom.MethodDeclaration.Statements) accepts a single statement, which converts to a collection.

## Write the code

[ToCsharpString()](cref:Meziantou.Framework.CodeDom.CodeObject.ToCsharpString) writes any code object, from a whole compilation unit to a single expression:

<!-- ref:../../tests/Meziantou.Framework.CodeDom.Tests/CSharpCodeGeneratorTests.cs;region=CodeDom_UsageOutput;format=md-fenced-code-block;dedent=true -->
```csharp
using System;

namespace Demo
{
    public class Greeter
    {
        public static string GetMessage(string name)
        {
            return (("Hello " + name) + "!");
        }
    }
}
```
<!-- endref -->

To write to a file or a stream, use [CSharpCodeGenerator](cref:Meziantou.Framework.CodeDom.CSharpCodeGenerator): it writes to a `TextWriter`, with `\n` line endings and four
spaces of indentation. See [Writing code](06-WritingCode.md).

## Next steps

- [Declarations](03-Declarations.md): every kind of type and member.
- [Statements and expressions](04-StatementsAndExpressions.md): conditions, loops, method calls, object creation...
