---
title: Writing code
description: Write a code object as C# text, walk and change the tree with visitors, and attach data to code objects.
---

## Write C# code

[ToCsharpString()](cref:Meziantou.Framework.CodeDom.CodeObject.ToCsharpString) writes any code object with the default settings. [CSharpCodeGenerator](cref:Meziantou.Framework.CodeDom.CSharpCodeGenerator) writes it to a `string`, a
`TextWriter` or an [IndentedTextWriter](cref:Meziantou.Framework.CodeDom.IndentedTextWriter), so you can write a file without building the whole text in memory:

<!-- ref:../../tests/Meziantou.Framework.CodeDom.Tests/CSharpCodeGeneratorTests.cs;region=CSharpCodeGenerator_Example;format=md-fenced-code-block;dedent=true -->
```csharp
var unit = new CompilationUnit();
unit.AddNamespace("MyNamespace").AddType(new ClassDeclaration("MyClass"));

var generator = new CSharpCodeGenerator();
string code = generator.Write(unit);

Assert.Equal("""
    namespace MyNamespace
    {
        class MyClass
        {
        }
    }

    """, code);
```
<!-- endref -->

The generator writes `\n` line endings and indents with four spaces. To use other settings, create an
[IndentedTextWriter](cref:Meziantou.Framework.CodeDom.IndentedTextWriter) with the indentation and line ending you want, and pass it to [Write](cref:Meziantou.Framework.CodeDom.CSharpCodeGenerator.Write).

## Walk the tree

Every code object has a [Parent](cref:Meziantou.Framework.CodeDom.CodeObject.Parent), set when the object is added to a collection or assigned to a property. An object
has only one parent: assigning an object that already belongs to another parent throws an `ArgumentException`, so create
a new object for each place.

<!-- ref:../../tests/Meziantou.Framework.CodeDom.Tests/CSharpCodeGeneratorTests.cs;region=CodeObject_Example;format=md-fenced-code-block;dedent=true -->
```csharp
var unit = new CompilationUnit();
var ns = unit.AddNamespace("MyNamespace");
var cls = ns.AddType(new ClassDeclaration("MyClass"));

// Code objects form a tree
Assert.Same(ns, cls.Parent);
Assert.Same(unit, ns.Parent);
```
<!-- endref -->

[SelfOrAnscestorOfType\<T\>()](cref:Meziantou.Framework.CodeDom.Extensions.SelfOrAnscestorOfType) and [AnscestorOfType\<T\>()](cref:Meziantou.Framework.CodeDom.Extensions.AnscestorOfType) find the closest parent of a type, such as the type that declares
a method.

## Visitors

[Visitor](cref:Meziantou.Framework.CodeDom.Visitor) visits every object of a tree. Derive from it and override the `Visit*` method of the objects you are
interested in, and call the base method to keep visiting their children:

<!-- ref:../../tests/Meziantou.Framework.CodeDom.Tests/CSharpCodeGeneratorTests.cs;region=CodeDom_CustomVisitor;format=md-fenced-code-block;dedent=true -->
```csharp
private sealed class MethodNameCollector : Visitor
{
    public List<string?> Names { get; } = [];

    public override void VisitMethodDeclaration(MethodDeclaration methodDeclaration)
    {
        Names.Add(methodDeclaration.Name);
        base.VisitMethodDeclaration(methodDeclaration);
    }
}
```
<!-- endref -->

<!-- ref:../../tests/Meziantou.Framework.CodeDom.Tests/CSharpCodeGeneratorTests.cs;region=CodeDom_Visitor;format=md-fenced-code-block;dedent=true -->
```csharp
var type = new ClassDeclaration("Sample");
type.AddMember(new MethodDeclaration("Write"));
type.AddMember(new PropertyDeclaration("Name", typeof(string)));
type.AddMember(new MethodDeclaration("Read"));

var collector = new MethodNameCollector();
collector.Visit(type);
Assert.Equal(["Write", "Read"], collector.Names);

// Sort the members: fields, properties, methods... then by name
DefaultFormatterVisitor.Instance.Visit(type);
Assert.Equal(["Name", "Read", "Write"], type.Members.Select(member => member.Name));
```
<!-- endref -->

[DefaultFormatterVisitor.Instance](cref:Meziantou.Framework.CodeDom.DefaultFormatterVisitor.Instance) is a visitor that sorts the members of classes, structs and interfaces by kind, then by
name. Run it before writing the code to get a stable order, whatever the order in which the members were added.

## Attach data

[Data](cref:Meziantou.Framework.CodeDom.CodeObject.Data) is a dictionary on every code object, that the generator ignores. Use it to keep information from your model on
the objects you create, for instance to find them again in a visitor.
