---
title: Comments and nullable context
description: Add XML documentation, line comments and nullable annotations to the generated code.
---

## XML documentation

Types and members have `XmlComments`. `AddSummary`, `AddParam`, `AddTypeParam` and `AddReturn` add the common
elements, and `Add(XElement)` any other element. Statements have `CommentsBefore` and `CommentsAfter`, which write line
comments:

<!-- ref:../../tests/Meziantou.Framework.CodeDom.Tests/CSharpCodeGeneratorTests.cs;region=CodeDom_Comments;format=md-fenced-code-block;dedent=true -->
```csharp
var method = new MethodDeclaration("Greet")
{
    Modifiers = Modifiers.Public,
    ReturnType = typeof(string),
};
var name = method.AddArgument("name", typeof(string));
method.XmlComments.AddSummary("Creates a greeting message.");
method.XmlComments.AddParam("name", "The name of the person to greet.");

var statement = new ReturnStatement(Expression.Add("Hello ", name));
statement.CommentsBefore.Add("Keep the message short");
method.Statements = statement;
```
<!-- endref -->

The method is written as:

<!-- ref:../../tests/Meziantou.Framework.CodeDom.Tests/CSharpCodeGeneratorTests.cs;region=CodeDom_CommentsOutput;format=md-fenced-code-block;dedent=true -->
```csharp
/// <summary>Creates a greeting message.</summary>
/// <param name="name">The name of the person to greet.</param>
public string Greet(string name)
{
    // Keep the message short
    return ("Hello " + name);
}
```
<!-- endref -->

A `CommentStatement` is a comment on its own line among the statements. Expressions, declarations and other code objects
have `CommentsBefore` and `CommentsAfter` too. The comments of an expression are inline comments (`/* ... */`) by
default; `Add(text, CommentType)` chooses the kind of each comment.

## Nullable reference types

Set `NullableContext` on a compilation unit, a type, a member or a statement to write a `#nullable` directive, and use
`MakeNullable()` to annotate a reference type:

<!-- ref:../../tests/Meziantou.Framework.CodeDom.Tests/CSharpCodeGeneratorTests.cs;region=CodeDom_NullableContext;format=md-fenced-code-block;dedent=true -->
```csharp
var unit = new CompilationUnit
{
    NullableContext = NullableContext.Enable,
};
var type = unit.AddNamespace("Demo").AddType(new ClassDeclaration("Person"));
type.AddMember(new FieldDeclaration("_nickname", new TypeReference(typeof(string)).MakeNullable()));

Assert.Equal("""
    #nullable enable
    namespace Demo
    {
        class Person
        {
            string? _nickname;
        }
    }

    """, unit.ToCsharpString());
```
<!-- endref -->

`NullableContext.Enable` writes `#nullable enable` and `Disable` writes `#nullable disable`. `Default` writes nothing. On
a type, a member or a statement, the opposite directive follows the object, so the setting applies to that object
only.
