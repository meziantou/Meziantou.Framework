---
title: Introduction
description: An object model to build C# code as a tree of declarations, statements and expressions, and write it as source code.
---

`Meziantou.Framework.CodeDom` builds C# source code from objects instead of strings. You create a tree of code objects
(a compilation unit, its namespaces, types, members, statements and expressions) and the library writes the C# code,
with the indentation, the braces, the parentheses and the escaping done for you:

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

`generatedCode` contains:

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

It is a modern replacement for `System.CodeDom`, which has not evolved for years, focused on C#: records, nullable
reference types, `async` and `await`, `nameof`, generic constraints such as `unmanaged`...

Use it in code generators, scaffolding tools, source generators or build tasks that write C# files: the code is easier
to compose and to refactor than string concatenation, and the output is always well formed.

## Principles

- **A tree of objects.** Every element is a `CodeObject` with a `Parent`. Adding an object to a collection or assigning
  it to a property attaches it to the tree.
- **Conversions keep the code short.** A `Type`, a `string`, a number, a `bool` or an enum converts to the matching
  type reference or literal, and a member declaration or an argument to a reference to it.
- **The output is explicit.** Binary expressions are parenthesized, and the types that are not C# keywords are written
  with their full name and `global::`, so the meaning of the generated code does not depend on operator precedence or
  on the `using` directives of the file.
- **No compilation.** The library writes source code; it does not check that the code compiles.

## Next steps

- [Getting started](02-GettingStarted.md): install the package and generate a file.
- [Declarations](03-Declarations.md): namespaces, types, members and attributes.
- [Statements and expressions](04-StatementsAndExpressions.md): the code inside members.
