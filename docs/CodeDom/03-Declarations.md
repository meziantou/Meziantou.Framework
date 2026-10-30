---
title: Declarations
description: Declare namespaces, classes, structs, records, interfaces, enums, delegates, their members and their attributes.
---

## Compilation units and namespaces

A `CompilationUnit` is a file. It contains `using` directives, namespaces and types. A `NamespaceDeclaration` can also
have its own `using` directives and nested namespaces:

<!-- ref:../../tests/Meziantou.Framework.CodeDom.Tests/CSharpCodeGeneratorTests.cs;region=CompilationUnit_Example;format=md-fenced-code-block;dedent=true -->
```csharp
var unit = new CompilationUnit();
unit.Usings.Add(new UsingDirective("System"));
var ns = unit.AddNamespace("MyNamespace");
ns.AddType(new ClassDeclaration("MyClass"));
var code = unit.ToCsharpString();

Assert.StartsWith("using System;", code);
Assert.Contains("namespace MyNamespace", code);
Assert.Contains("class MyClass", code);
```
<!-- endref -->

<!-- ref:../../tests/Meziantou.Framework.CodeDom.Tests/CSharpCodeGeneratorTests.cs;region=NamespaceDeclaration_Example;format=md-fenced-code-block;dedent=true -->
```csharp
var ns = new NamespaceDeclaration("MyNamespace");
ns.Usings.Add(new UsingDirective("System"));
ns.AddType(new ClassDeclaration("MyClass"));
var code = ns.ToCsharpString();

Assert.StartsWith("namespace MyNamespace", code);
Assert.Contains("using System;", code);
Assert.Contains("class MyClass", code);
```
<!-- endref -->

## Types

| Class | Declares |
| --- | --- |
| `ClassDeclaration` | A class, with an optional `BaseType`. |
| `StructDeclaration` | A struct. |
| `RecordDeclaration`, `RecordStructDeclaration` | A record class or a record struct. |
| `InterfaceDeclaration` | An interface. |
| `EnumerationDeclaration` | An enum, with its `BaseType` and its `EnumerationMember` items. |
| `DelegateDeclaration` | A delegate, with its return type and arguments. |

Every type has `Modifiers` and `CustomAttributes`. Classes, structs, records and interfaces have `Members`, generic
`Parameters` and the interfaces they `Implements`:

<!-- ref:../../tests/Meziantou.Framework.CodeDom.Tests/CSharpCodeGeneratorTests.cs;region=ClassDeclaration_Example;format=md-fenced-code-block;dedent=true -->
```csharp
var cls = new ClassDeclaration("MyClass");
cls.Modifiers = Modifiers.Public;
cls.BaseType = typeof(object);
cls.Members.Add(new MethodDeclaration("MyMethod") { Statements = [] });

Assert.Equal("""
    public class MyClass : object
    {
        void MyMethod()
        {
        }
    }

    """, cls.ToCsharpString());
```
<!-- endref -->

<!-- ref:../../tests/Meziantou.Framework.CodeDom.Tests/CSharpCodeGeneratorTests.cs;region=StructDeclaration_Example;format=md-fenced-code-block;dedent=true -->
```csharp
var str = new StructDeclaration("Point");
str.Members.Add(new FieldDeclaration("X", typeof(int)));
str.Members.Add(new FieldDeclaration("Y", typeof(int)));

Assert.Equal("""
    struct Point
    {
        int X;

        int Y;
    }

    """, str.ToCsharpString());
```
<!-- endref -->

<!-- ref:../../tests/Meziantou.Framework.CodeDom.Tests/CSharpCodeGeneratorTests.cs;region=RecordDeclaration_Example;format=md-fenced-code-block;dedent=true -->
```csharp
var record = new RecordDeclaration("Person");

// An accessor without statements is auto-implemented
record.Members.Add(new PropertyDeclaration("Name", typeof(string)) { Getter = new PropertyAccessorDeclaration { Statements = null } });
record.Members.Add(new PropertyDeclaration("Age", typeof(int)) { Getter = new PropertyAccessorDeclaration { Statements = null } });

Assert.Equal("""
    record Person
    {
        string Name
        {
            get;
        }

        int Age
        {
            get;
        }
    }

    """, record.ToCsharpString());
```
<!-- endref -->

`Modifiers` is a flags enum: combine the values with `|`, such as `Modifiers.Public | Modifiers.Static |
Modifiers.Partial`.

### Enums and delegates

<!-- ref:../../tests/Meziantou.Framework.CodeDom.Tests/CSharpCodeGeneratorTests.cs;region=EnumerationDeclaration_Example;format=md-fenced-code-block;dedent=true -->
```csharp
var enumDecl = new EnumerationDeclaration("Status");
enumDecl.BaseType = typeof(int);
enumDecl.Members.Add(new EnumerationMember("Active", 1));
enumDecl.Members.Add(new EnumerationMember("Inactive", 2));

Assert.Equal("""
    enum Status : int
    {
        Active = 1,
        Inactive = 2
    }

    """, enumDecl.ToCsharpString());
```
<!-- endref -->

<!-- ref:../../tests/Meziantou.Framework.CodeDom.Tests/CSharpCodeGeneratorTests.cs;region=DelegateDeclaration_Example;format=md-fenced-code-block;dedent=true -->
```csharp
var del = new DelegateDeclaration("MyDelegate");
del.ReturnType = typeof(void);
del.Arguments.Add(new MethodArgumentDeclaration(typeof(string), "message"));

Assert.Equal("""
    delegate void MyDelegate(string message);

    """, del.ToCsharpString());
```
<!-- endref -->

### Generic types

A `TypeParameter` added to `Parameters` declares a type parameter. It converts to a type reference, so it can be used
as the type of a member:

<!-- ref:../../tests/Meziantou.Framework.CodeDom.Tests/CSharpCodeGeneratorTests.cs;region=InterfaceDeclaration_Example;format=md-fenced-code-block;dedent=true -->
```csharp
var iface = new InterfaceDeclaration("IRepository");
var typeParameter = new TypeParameter("T");
iface.Parameters.Add(typeParameter);
iface.Members.Add(new MethodDeclaration("Get") { ReturnType = typeParameter });
var code = iface.ToCsharpString();

Assert.StartsWith("interface IRepository<T>", code);
Assert.Contains("Get();", code);
```
<!-- endref -->

The `Constraints` of a type parameter accept the `class`, `struct`, `unmanaged`, `new()` and base type constraints
(`ClassTypeParameterConstraint`, `ValueTypeTypeParameterConstraint`, `UnmanagedTypeParameterConstraint`,
`ConstructorParameterConstraint` and `BaseTypeParameterConstraint`).

## Members

| Class | Declares |
| --- | --- |
| `FieldDeclaration` | A field, with an optional initializer. |
| `PropertyDeclaration` | A property, with a `Getter` and a `Setter`. |
| `MethodDeclaration` | A method, with its return type, arguments, type parameters and statements. |
| `ConstructorDeclaration` | A constructor, with an optional `base(...)` or `this(...)` initializer. |
| `EventFieldDeclaration` | An event. |
| `OperatorDeclaration` | An operator, including implicit and explicit conversions. |
| `EnumerationMember` | A member of an enum. |

<!-- ref:../../tests/Meziantou.Framework.CodeDom.Tests/CSharpCodeGeneratorTests.cs;region=FieldDeclaration_Example;format=md-fenced-code-block;dedent=true -->
```csharp
var field = new FieldDeclaration("_count", typeof(int), Modifiers.Private, new LiteralExpression(0));

Assert.Equal("""
    private int _count = 0;

    """, field.ToCsharpString());
```
<!-- endref -->

<!-- ref:../../tests/Meziantou.Framework.CodeDom.Tests/CSharpCodeGeneratorTests.cs;region=MethodDeclaration_Example;format=md-fenced-code-block;dedent=true -->
```csharp
var method = new MethodDeclaration("Calculate");
method.Modifiers = Modifiers.Public;
method.ReturnType = typeof(int);
method.Arguments.Add(new MethodArgumentDeclaration(typeof(int), "value"));
method.Statements = new ReturnStatement(new LiteralExpression(42));

Assert.Equal("""
    public int Calculate(int value)
    {
        return 42;
    }

    """, method.ToCsharpString());
```
<!-- endref -->

<!-- ref:../../tests/Meziantou.Framework.CodeDom.Tests/CSharpCodeGeneratorTests.cs;region=ConstructorDeclaration_Example;format=md-fenced-code-block;dedent=true -->
```csharp
var cls = new ClassDeclaration("Person");
var ctor = cls.AddMember(new ConstructorDeclaration());
ctor.Modifiers = Modifiers.Public;
ctor.Arguments.Add(new MethodArgumentDeclaration(typeof(string), "name"));
ctor.Statements = new AssignStatement(new MemberReferenceExpression(new ThisExpression(), "_name"), new ArgumentReferenceExpression("name"));

Assert.Equal("""
    class Person
    {
        public Person(string name)
        {
            this._name = name;
        }
    }

    """, cls.ToCsharpString());
```
<!-- endref -->

A property accessor is a `PropertyAccessorDeclaration`. A statement converts to an accessor, and an accessor without
statements is written as an auto-implemented accessor (`get;`):

<!-- ref:../../tests/Meziantou.Framework.CodeDom.Tests/CSharpCodeGeneratorTests.cs;region=PropertyDeclaration_Example;format=md-fenced-code-block;dedent=true -->
```csharp
var prop = new PropertyDeclaration("Name", typeof(string));
prop.Modifiers = Modifiers.Public;
prop.Getter = new ReturnStatement(new MemberReferenceExpression(new ThisExpression(), "_name"));
prop.Setter = new AssignStatement(new MemberReferenceExpression(new ThisExpression(), "_name"), new ValueArgumentExpression());

Assert.Equal("""
    public string Name
    {
        get
        {
            return this._name;
        }
        set
        {
            this._name = value;
        }
    }

    """, prop.ToCsharpString());
```
<!-- endref -->

## Attributes

A `CustomAttribute` has a type and `Arguments`. A `CustomAttributeArgument` with a `PropertyName` is a named argument.
Add attributes to the `CustomAttributes` collection of a type, a member, an argument or an accessor:

<!-- ref:../../tests/Meziantou.Framework.CodeDom.Tests/CSharpCodeGeneratorTests.cs;region=CustomAttribute_Example;format=md-fenced-code-block;dedent=true -->
```csharp
var attr = new CustomAttribute(typeof(ObsoleteAttribute));
attr.Arguments.Add(new CustomAttributeArgument(new LiteralExpression("Use NewMethod instead")));

Assert.Equal("""[global::System.ObsoleteAttribute("Use NewMethod instead")]""", attr.ToCsharpString());
```
<!-- endref -->
