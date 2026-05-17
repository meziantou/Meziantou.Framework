# Meziantou.Framework.CodeDom

`Meziantou.Framework.CodeDom` is a modern replacement for .NET CodeDom. The built-in CodeDom APIs have not evolved for years and do not support newer C# syntax, while this library lets you generate modern C# code using a strongly-typed object model.

## Usage

```csharp
using Meziantou.Framework.CodeDom;

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
Console.WriteLine(generatedCode);
```

Generated code:

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
