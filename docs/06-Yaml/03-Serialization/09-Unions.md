---
title: C# unions
description: Serialize a C# union as its selected case, and tell its cases apart when reading.
---

A C# union is written as the value of its selected case, without a wrapper. C# unions need .NET 11.

<!-- ref:../../../tests/Meziantou.Framework.Yaml.Tests/Serialization/YamlCSharpUnionTests.cs;region=YamlUnion_Setting;format=md-fenced-code-block;dedent=true -->
```csharp
internal union Setting(bool, int, string);
```
<!-- endref -->

<!-- ref:../../../tests/Meziantou.Framework.Yaml.Tests/Serialization/YamlCSharpUnionTests.cs;region=YamlUnion_Serialize;format=md-fenced-code-block;dedent=true -->
```csharp
var yaml = YamlSerializer.Serialize(new Setting(42));
Assert.Equal("42\n", yaml);
```
<!-- endref -->

## How a case is selected

When reading, the case is selected by the shape of the YAML, so a union whose cases have different shapes needs no
configuration:

- A case that matches the value exactly wins: `Setting` reads `42` as an `int`, `true` as a `bool`, and `"42"` as a
  `string`.
- Otherwise, the cases that can still read the value are used: a `string` case reads a plain `42` or `true`, a `char`
  or enum case reads a number, and a case whose type has a [custom converter](07-CustomConverters.md) reads any value.
- `YamlSequence`, `YamlMapping` and `YamlValue` cases match sequences, mappings and scalars; `object`, `YamlNode` and
  `YamlElement` cases match any value. A nested union case matches the shapes of its own cases.

A union with a nullable case reads `null` as that case. Otherwise, `null` gives the default value of the union.

## Tell mapping cases apart

When several cases match, for instance two classes that are both written as a mapping, reading throws a
`YamlException`, unless a type classifier selects one of them. `YamlUnionTypeStructuralClassifier` selects the case
from the keys of the mapping:

<!-- ref:../../../tests/Meziantou.Framework.Yaml.Tests/Serialization/YamlCSharpUnionTests.cs;region=YamlUnionTypeStructuralClassifier_Shape;format=md-fenced-code-block;dedent=true -->
```csharp
internal union Shape(Circle, Rectangle);
internal sealed class Circle { public int Radius { get; set; } }
internal sealed class Rectangle { public int Width { get; set; } public int Height { get; set; } }
```
<!-- endref -->

<!-- ref:../../../tests/Meziantou.Framework.Yaml.Tests/Serialization/YamlCSharpUnionTests.cs;region=YamlUnionTypeStructuralClassifier_Usage;format=md-fenced-code-block;dedent=true -->
```csharp
var options = new YamlSerializerOptions
{
    TypeClassifiers = [new YamlUnionTypeStructuralClassifier()],
};

var shape = YamlSerializer.Deserialize<Shape>("Radius: 3", options);
Assert.Equal(3, Assert.IsType<Circle>(shape.Value).Radius);
```
<!-- endref -->

The classifier eliminates the cases that do not declare a key of the mapping, and, once the mapping is read, the cases
that miss one of their required keys. A key that no case declares eliminates only the cases that
[reject unknown keys](06-ReadingBehavior.md#unknown-keys). Reading succeeds when exactly one case is left. A union whose
cases can never be told apart is rejected when the classifier is created.

For other rules, write your own classifier: see [Choose the type from the content](08-Polymorphism.md#choose-the-type-from-the-content).

## Numbers in strings

`[YamlNumberHandling]` on a union applies to its numeric cases. A quoted scalar normally selects a string case; with
`AllowReadingFromString`, a quoted number such as `"42"` also matches the numeric cases, and with
`AllowNamedFloatingPointLiterals`, a quoted `"NaN"`, `"Infinity"` or `"-Infinity"` matches the floating-point cases. A
value that matches several cases is ambiguous:

<!-- ref:../../../tests/Meziantou.Framework.Yaml.Tests/Serialization/YamlCSharpUnionTests.cs;region=YamlUnion_NumberHandling;format=md-fenced-code-block;dedent=true -->
```csharp
[YamlNumberHandling(YamlNumberHandling.AllowReadingFromString)]
internal union IdOrName(int, string);
```
<!-- endref -->

<!-- ref:../../../tests/Meziantou.Framework.Yaml.Tests/Serialization/YamlCSharpUnionTests.cs;region=YamlUnion_NumberHandling_Deserialize;format=md-fenced-code-block;dedent=true -->
```csharp
Assert.Equal("alice", YamlSerializer.Deserialize<IdOrName>("\"alice\"").Value);
Assert.Equal(42, YamlSerializer.Deserialize<IdOrName>("42").Value);
Assert.Throws<YamlException>(() => YamlSerializer.Deserialize<IdOrName>("\"42\"")); // both cases match
```
<!-- endref -->
