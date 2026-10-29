---
title: Symbols and types
description: Test inheritance, interfaces, assignability and attributes, and query members, overrides and visibility.
---

## Type relationships

| Method | `true` when |
| --- | --- |
| `InheritsFrom(baseType)` | The type derives from `baseType`, directly or not. A type never derives from itself. |
| `IsOrInheritsFrom(type)` | The type is `type` or derives from it. |
| `Implements(interfaceType)` | The type implements the interface. |
| `IsOrImplements(interfaceType)` | The type is the interface or implements it. |
| `ImplementsGenericInterface(interfaceType)` | The type implements a construction of the generic interface, such as `IEnumerable<T>` for any `T`. |
| `IsAssignableTo(type)`, `IsAssignableFrom(type)` | A value of one type can be assigned to the other without creating a new value. |

For a type parameter, the constraints are inspected: `T` in `where T : Base` derives from `Base`.

<!-- ref:../../tests/Meziantou.Framework.Roslyn.Tests/RoslynHelperTests.cs;region=Roslyn_IsOrInheritsFrom;format=md-fenced-code-block;dedent=true -->
```csharp
var compilation = CSharpCompilation.Create(
    "Sample",
    [CSharpSyntaxTree.ParseText("public class Base { } public class Derived : Base { }")],
    [MetadataReference.CreateFromFile(typeof(object).Assembly.Location)]);
var baseType = compilation.GetTypeByMetadataName("Base")!;
var type = compilation.GetTypeByMetadataName("Derived")!;

// true when the type is Base or derives from it
Assert.True(type.IsOrInheritsFrom(baseType));
Assert.True(baseType.IsOrInheritsFrom(baseType));
Assert.False(baseType.IsOrInheritsFrom(type));
```
<!-- endref -->

`IsAssignableTo` follows the C# conversions that keep the identity of the value: identity, implicit reference and boxing
conversions. It handles `object`, the variance of generic interfaces and delegates, array covariance, type parameter
constraints, and boxing to `System.ValueType`, `System.Enum` and the interfaces of a value type:

<!-- ref:../../tests/Meziantou.Framework.Roslyn.Tests/RoslynHelperTests.cs;region=Roslyn_IsAssignableTo;format=md-fenced-code-block;dedent=true -->
```csharp
var compilation = CSharpCompilation.Create(
    "Sample",
    [CSharpSyntaxTree.ParseText("""
        using System.Collections.Generic;

        public class Base { }
        public class Derived : Base { }
        public class Sample
        {
            public void M<T>(string[] a, List<string> b, IEnumerable<Derived> c, T d, IEnumerable<int> e, int f)
                where T : IEnumerable<string>
            {
            }
        }
        """)],
    [MetadataReference.CreateFromFile(typeof(object).Assembly.Location)]);
var objectType = compilation.GetSpecialType(SpecialType.System_Object);
var enumerableOfObjectType = compilation.GetTypeByMetadataName("System.Collections.Generic.IEnumerable`1")!.Construct(objectType);
var method = (IMethodSymbol)compilation.GetTypeByMetadataName("Sample")!.GetMembers("M").Single();
var parameterTypes = method.Parameters.Select(parameter => parameter.Type).ToArray();

// true for string[], List<string>, IEnumerable<Derived>, or T where T : IEnumerable<string>
Assert.True(parameterTypes[0].IsAssignableTo(enumerableOfObjectType));
Assert.True(parameterTypes[1].IsAssignableTo(enumerableOfObjectType));
Assert.True(parameterTypes[2].IsAssignableTo(enumerableOfObjectType));
Assert.True(parameterTypes[3].IsAssignableTo(enumerableOfObjectType));

// false for IEnumerable<int>, as boxing the elements creates new values
Assert.False(parameterTypes[4].IsAssignableTo(enumerableOfObjectType));

// false for int to long, as the conversion creates a new value
Assert.False(parameterTypes[5].IsAssignableTo(compilation.GetSpecialType(SpecialType.System_Int64)));
```
<!-- endref -->

The conversions that create a new value are not considered, so `int` is not assignable to `long` or `int?`. Use
`Compilation.ClassifyCommonConversion` to take every conversion into account.

## Attributes

`HasAttribute`, `GetAttributes` and `GetFirstAttribute` find the attributes of a type on a symbol. By default
(`inherits: true`), an attribute whose class derives from the requested type matches too:

<!-- ref:../../tests/Meziantou.Framework.Roslyn.Tests/RoslynHelperTests.cs;region=Roslyn_HasAttribute;format=md-fenced-code-block;dedent=true -->
```csharp
var compilation = CSharpCompilation.Create(
    "Sample",
    [CSharpSyntaxTree.ParseText("""
        public class TagAttribute : System.Attribute { }
        public sealed class ImportantTagAttribute : TagAttribute { }

        [ImportantTag]
        public class Sample { }
        """)],
    [MetadataReference.CreateFromFile(typeof(object).Assembly.Location)]);
var tagAttribute = compilation.GetTypeByMetadataName("TagAttribute");
var type = compilation.GetTypeByMetadataName("Sample")!;

// By default, an attribute that derives from TagAttribute matches too
Assert.True(type.HasAttribute(tagAttribute));
Assert.False(type.HasAttribute(tagAttribute, inherits: false));
Assert.Equal("ImportantTagAttribute", type.GetFirstAttribute(tagAttribute)?.AttributeClass?.Name);
```
<!-- endref -->

Only the attributes of the symbol itself are inspected, never those of its base types or of the members it overrides:
`inherits` is about the attribute class, unlike the `inherit` parameter of `Attribute.GetCustomAttributes`.
`GetReturnTypeAttribute` and `HasReturnTypeAttribute` do the same for the attributes of the return value of a method.

## Find types

`Compilation.GetBestTypeByMetadataName(name)` returns the type with this metadata name that the compilation can use: the
one declared in the compilation, or the only accessible one among the referenced assemblies. The types the compilation
cannot access, such as an `internal` copy of the type in a referenced assembly, are ignored. It returns `null` when no
type, or several accessible types, have this name. Prefer it to `GetTypeByMetadataName` to find the types an analyzer
looks for.

## Other helpers

| Method | Description |
| --- | --- |
| `GetAllMembers()`, `GetAllMembers(name)` | The members of a type and of its base types, and optionally of its interfaces. |
| `GetAllInterfacesIncludingSelf()` | The interfaces of a type, and the type itself when it is an interface. |
| `IsEqualToAny(types)` | The type is one of the given types. |
| `IsObject()`, `IsString()`, `IsChar()`, `IsBoolean()`, `IsDateTime()`, `IsEnum()`, `IsNumberType()`, `IsBlittableType()` | Tests on special types. |
| `GetEnumUnderlyingType()`, `GetUnderlyingNullableTypeOrSelf()` | The underlying type of an enum, or of a `Nullable<T>`. |
| `IsVisibleOutsideOfAssembly()` | The symbol and its containing types are accessible from another assembly. |
| `IsOverrideOrInterfaceImplementation()` | The member overrides a base member or implements an interface member. |
| `IsInterfaceImplementation()`, `GetImplementedInterfaceMember()` | The member implements an interface member, and which one. |
| `IsOrOverrides(baseMethod)`, `Overrides(baseSymbol)` | The method is or overrides another one. |
| `IsPrimaryConstructor()` | The method is the primary constructor of a class or a struct, and optionally of a record. |
| `GetSymbolType()` | The type of a field, property, local, parameter, or the return type of a method. |
| `GetFirstSourceLocation()` | The first location of the symbol in source code. |
| `MatchesNamespace(parts)` | The namespace is `parts[0].parts[1]...`. |
| `Compilation.IsNet9OrGreater()` | The core library of the compilation is .NET 9 or later. |
| `GetCSharpLanguageVersion()` | The C# version of a compilation, a syntax tree, a node or an operation. |
