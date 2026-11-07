# Meziantou.Framework.CodeDom

`Meziantou.Framework.CodeDom` is a modern replacement for .NET CodeDom. The built-in CodeDom APIs have not evolved for years and do not support newer C# syntax, while this library lets you generate modern C# code using a strongly-typed object model.

## Usage

The types are in the `Meziantou.Framework.CodeDom` namespace.

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
