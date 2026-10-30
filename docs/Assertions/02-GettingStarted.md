---
title: Getting started
description: Install the package, make Assert refer to this library, and write your first assertions.
---

## Install the package

Add the `Meziantou.Framework.Assertions` package to your test project:

```bash
dotnet add package Meziantou.Framework.Assertions
```

The package contains the [Assert](cref:Meziantou.Framework.Assertions.Assert) class and the [analyzers](11-AnalyzerRules.md): they are enabled as soon as the
package is referenced.

## Use it as the Assert class

Test projects usually import the namespace of their test framework, whose [Assert](cref:Meziantou.Framework.Assertions.Assert) class would hide this one. Add a
global alias so that [Assert](cref:Meziantou.Framework.Assertions.Assert) means [Meziantou.Framework.Assertions.Assert](cref:Meziantou.Framework.Assertions.Assert) in the whole project. In the project file:

```xml
<ItemGroup>
  <Using Include="Meziantou.Framework.Assertions.Assert" Alias="Assert" />
</ItemGroup>
```

Or in a C# file of the project, such as `GlobalUsings.cs`:

<!-- ref:../../samples/Meziantou.Framework.Assertions.Samples/GlobalUsings.cs;format=md-fenced-code-block -->
```csharp
global using Assert = Meziantou.Framework.Assertions.Assert;
```
<!-- endref -->

The examples of this documentation use this alias. Without it, write [Meziantou.Framework.Assertions.Assert](cref:Meziantou.Framework.Assertions.Assert) or add
`using Meziantou.Framework.Assertions;` to the files that use it, and make sure no other [Assert](cref:Meziantou.Framework.Assertions.Assert) class is in scope.

## Write your first assertions

<!-- ref:../../tests/Meziantou.Framework.Assertions.Tests/AssertEqualTests.cs;region=Assert_FirstAssertions;format=md-fenced-code-block;dedent=true -->
```csharp
var names = "Alice,Bob,Charlie".Split(',');

Assert.HasCount(3, names);
Assert.Equal("Alice", names[0]);    // the expected value comes first...
Assert.Contains("Bob", names);      // ...including for Contains, StartsWith and EndsWith
Assert.StartsWith("Ch", names[2]);
Assert.DoesNotContain("Dave", names);
```
<!-- endref -->

The expected value always comes first. This is the order of [Assert.Equal](cref:Meziantou.Framework.Assertions.Assert.Equal), and it is kept for the assertions that
search for a value: [Contains](cref:Meziantou.Framework.Assertions.Assert.Contains), [DoesNotContain](cref:Meziantou.Framework.Assertions.Assert.DoesNotContain), [StartsWith](cref:Meziantou.Framework.Assertions.Assert.StartsWith), [EndsWith](cref:Meziantou.Framework.Assertions.Assert.EndsWith), and the count assertions such as
[HasCount](cref:Meziantou.Framework.Assertions.Assert.HasCount). When the order is wrong, the failure message labels the values the wrong way round; the analyzer reports
a constant passed as the actual value (`MFAS0001`).

Every assertion takes an optional `message`, written at the top of the failure message. The expressions of the
arguments are captured by the compiler and written in the failure message too, so you rarely need one. See
[Failure messages](10-FailureMessages.md).

## Use the values the assertions return

Assertions that check a value return it, typed after the check, so the test can go on without a cast or a `!`:

<!-- ref:../../tests/Meziantou.Framework.Assertions.Tests/AssertEqualTests.cs;region=Assert_ReturnedValues;format=md-fenced-code-block;dedent=true -->
```csharp
var settings = new Dictionary<string, object?>(StringComparer.Ordinal)
{
    ["name"] = "Alice",
    ["roles"] = new List<string> { "admin" },
};

// Assertions that check a value return it, typed after the check
var value = Assert.Contains("name", settings);           // the value of the key
var name = Assert.IsType<string>(value);                 // the value as a string
Assert.Equal("ALICE", name.ToUpperInvariant());

var roles = Assert.IsType<List<string>>(settings["roles"]);
var role = Assert.Single(roles);                         // the only item
Assert.Equal("admin", role);
```
<!-- endref -->

| Assertion | Returns |
| --- | --- |
| `NotNull(value)` | The value, as non-nullable |
| `IsType<T>(value)`, `IsAssignableTo<T>(value)` | The value, as a `T` |
| `Single(collection)`, `Single(collection, predicate)` | The only item, or the only item that matches |
| `Contains(key, dictionary)` | The value of the key |
| `Throws<T>(action)`, `ThrowsAny<T>(action)` | The exception, as a `T` |
| `Raise<T>(...)`, `RaiseAny<T>(...)` | The event, with its sender and arguments |

The analyzer suggests the returned value when the next line derives it again, for instance `collection[0]` after
`Assert.Single(collection)` (`MFAS0056`).

## Next steps

- [Equality](03-Equality.md): compare values, strings, numbers and collections.
- [Collections](06-Collections.md): items, counts and per-item assertions.
- [Exceptions](08-Exceptions.md): synchronous and asynchronous code.
