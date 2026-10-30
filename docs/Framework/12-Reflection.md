---
title: Reflection
description: Call private members dynamically, check attributes, read assembly information, parse enums, combine expressions and evaluate property paths.
---

The helpers of this page use reflection: the trimmer and Native AOT cannot see the members they reach. Several of them are
marked with `RequiresUnreferencedCode`, so the trim analyzer warns when you call them from code that is trimmed.

## Call private members

`ReflectionDynamicObject` wraps an object, or a type, in a `dynamic` object that reaches its private members. Use it in
tests, or to call a member of a library that is not public:

<!-- ref:../../tests/Meziantou.Framework.Tests/ReflectionDynamicObjectTests.cs;region=ReflectionDynamicObject_PrivateMembers;format=md-fenced-code-block;dedent=true -->
```csharp
dynamic counter = new ReflectionDynamicObject(new Counter());

// Read and write private fields and properties
counter._count = 41;
Assert.Equal(41, counter.Count);

// Call private methods
counter.Increment();
Assert.Equal(42, counter._count);

// Wrap the type to reach static members and to call a public constructor
dynamic counterType = new ReflectionDynamicObject(typeof(Counter));
Assert.Equal("counter", (string)counterType.Name);
dynamic createdCounter = counterType.CreateInstance(10);
Assert.Equal(10, createdCounter.Count);
```
<!-- endref -->

The example uses this class:

<!-- ref:../../tests/Meziantou.Framework.Tests/ReflectionDynamicObjectTests.cs;region=ReflectionDynamicObject_Counter;format=md-fenced-code-block;dedent=true -->
```csharp
private sealed class Counter
{
    private int _count;

    public Counter()
    {
    }

    public Counter(int count) => _count = count;

    private static string Name => "counter";

    private int Count => _count;

    private void Increment() => _count++;
}
```
<!-- endref -->

- Wrap an instance to reach its fields, properties, indexers and methods, public or not, including the ones declared by
  its base types.
- Wrap a `Type` to reach its static members, and to create an instance with `CreateInstance`. `CreateInstance` tries the
  public constructors that have as many parameters as the arguments, and throws an `ArgumentException` when none of them
  accepts them.
- The values are `dynamic`: cast them when an overload must be chosen, as in `(string)counterType.Name`.
- A member that does not exist throws a `RuntimeBinderException` when it is used.

## Check attributes

<!-- ref:../../tests/Meziantou.Framework.Tests/CustomAttributeExtensionsTests.cs;region=CustomAttributeExtensions_HasCustomAttribute;format=md-fenced-code-block;dedent=true -->
```csharp
Assert.True(typeof(FileAttributes).HasCustomAttribute<FlagsAttribute>());
Assert.False(typeof(DayOfWeek).HasCustomAttribute<FlagsAttribute>());

// The same with a Type, for instance one read from a configuration file
Assert.True(typeof(FileAttributes).HasCustomAttribute(typeof(FlagsAttribute)));

// Members, parameters, modules and assemblies have the same methods
var method = typeof(string).GetMethod(nameof(string.Format), [typeof(string), typeof(object[])]);
Assert.NotNull(method);
Assert.True(method.GetParameters()[1].HasCustomAttribute<ParamArrayAttribute>());
```
<!-- endref -->

`HasCustomAttribute` exists for `Type`, `MemberInfo`, `ParameterInfo`, `Module` and `Assembly`, with the attribute type as
a generic argument or as a `Type`. It does not search the base types unless you pass `inherit: true`.

## Read assembly information

<!-- ref:../../tests/Meziantou.Framework.Tests/AssemblyUtilitiesTests.cs;region=AssemblyUtilities_Usage;format=md-fenced-code-block;dedent=true -->
```csharp
var assembly = typeof(AssemblyUtilities).Assembly;

// The value of AssemblyInformationalVersionAttribute, or null when the assembly has none
var version = assembly.GetInformationalVersion();
Assert.NotNull(version);

// Throws an ArgumentException that lists the available resources instead of returning null
Assert.Throws<ArgumentException>(() => assembly.GetRequiredManifestResourceStream("missing.txt"));
```
<!-- endref -->

- `GetInformationalVersion` returns the value of `AssemblyInformationalVersionAttribute`, the version you usually show in
  an "About" screen, or `null` when the assembly has none.
- `GetRequiredManifestResourceStream(name)` and `GetRequiredManifestResourceStream(type, name)` return an embedded
  resource, like `Assembly.GetManifestResourceStream`, but throw an `ArgumentException` that lists the available resource
  names instead of returning `null`. The overload with a type looks for the resource in the namespace of the type.
- `GetLinkerTimestampUtc` reads the build date stored in the header of the file. Deterministic builds, the default of
  the .NET SDK, store a hash there instead of a date: the method then returns `null`.

## Inspect types and methods

<!-- ref:../../tests/Meziantou.Framework.Tests/ReflectionUtilitiesTests.cs;region=ReflectionUtilities_TypeChecks;format=md-fenced-code-block;dedent=true -->
```csharp
Assert.True(typeof(int?).IsNullableOfT());
Assert.False(typeof(int).IsNullableOfT());

Assert.True(typeof(FileAttributes).IsFlagsEnum());
Assert.False(ReflectionUtilities.IsFlagsEnum<DayOfWeek>());

// string declares an implicit conversion to ReadOnlySpan<char>
var conversion = ReflectionUtilities.GetImplicitConversion("text", typeof(ReadOnlySpan<char>));
Assert.NotNull(conversion);
Assert.Equal("op_Implicit", conversion.Name);

// The conversions built into the language, such as int to long, are not operators
Assert.Null(ReflectionUtilities.GetImplicitConversion(42, typeof(long)));
```
<!-- endref -->

- `IsNullableOfT` tells whether a type is a `Nullable<T>`, such as `int?`.
- `IsFlagsEnum` tells whether a type is an enum marked with `[Flags]`. It returns `false` for any other type.
- `GetImplicitConversion(value, targetType)` returns the `op_Implicit` operator that the type of the value declares to
  convert it to the target type, or `null`. It does not see operators declared by the target type, nor the conversions
  built into the language.

`GetMethodLocation` finds the source file and the first line of a method, from the portable PDB of its assembly, embedded
or next to the file:

<!-- ref:../../tests/Meziantou.Framework.Tests/ReflectionUtilitiesTests.cs;region=ReflectionUtilities_GetMethodLocation;format=md-fenced-code-block;dedent=true -->
```csharp
Func<int> getAnswer = () => 42;

// Reads the portable PDB, embedded or next to the assembly
var location = getAnswer.Method.GetMethodLocation();
Assert.NotNull(location);
Assert.EndsWith("ReflectionUtilitiesTests.cs", location.Value.FilePath);
Assert.True(location.Value.SequencePoint.StartLine > 0);
```
<!-- endref -->

It returns `null` when the assembly has no file, as in a single-file or Native AOT application, when the PDB cannot be
found, or when the method has no source information. `GetMethodLocationAsync` reads the assembly file asynchronously.

## Parse enums

`EnumExtensions` adds `Parse` and `TryParse` to every enum type, with the case sensitivity as a parameter. They accept a
string or a `ReadOnlySpan<char>`:

<!-- ref:../../tests/Meziantou.Framework.Tests/ReflectionUtilitiesTests.cs;region=EnumExtensions_Parse;format=md-fenced-code-block;dedent=true -->
```csharp
// Parse and TryParse are available on every enum type
var day = DayOfWeek.Parse("friday", ignoreCase: true);
Assert.Equal(DayOfWeek.Friday, day);

Assert.True(DayOfWeek.TryParse("Sunday", ignoreCase: false, out var sunday));
Assert.Equal(DayOfWeek.Sunday, sunday);
```
<!-- endref -->

They call `Enum.Parse<TEnum>` and `Enum.TryParse<TEnum>`, so they accept the names and the numeric values, and the
combinations of names of a `[Flags]` enum.

## Combine expressions

`AndAlso`, `OrElse` and `Negate` combine `Expression<Func<T, bool>>` predicates into a new predicate, for instance to
build a query filter from optional criteria:

<!-- ref:../../tests/Meziantou.Framework.Tests/ExpressionExtensions.cs;region=ExpressionExtensions_Combine;format=md-fenced-code-block;dedent=true -->
```csharp
Expression<Func<int, bool>> isPositive = n => n > 0;
Expression<Func<int, bool>> isSmall = n => n < 10;

// The parameters are rebound: the result is one lambda expression with one parameter
var isPositiveAndSmall = isPositive.AndAlso(isSmall);
var isNotPositiveOrNotSmall = isPositiveAndSmall.Negate();
var isNegativeOrSmall = isPositive.Negate().OrElse(isSmall);

int[] numbers = [-5, 3, 42];
Assert.Equal([3], numbers.AsQueryable().Where(isPositiveAndSmall));
Assert.Equal([-5, 42], numbers.AsQueryable().Where(isNotPositiveOrNotSmall));
Assert.Equal([-5, 3], numbers.AsQueryable().Where(isNegativeOrSmall));
```
<!-- endref -->

The parameters of both expressions are replaced by a single one, so the result is a lambda expression that a LINQ
provider can translate, not an invocation of the original expressions.

## Evaluate a property path

`DataBinder.Eval` reads a value from a path of properties given as a string, such as a path read from a template:

<!-- ref:../../tests/Meziantou.Framework.Tests/DataBinderTests.cs;region=DataBinder_Eval;format=md-fenced-code-block;dedent=true -->
```csharp
var order = new
{
    Number = 42,
    Customer = new { Name = "Ada" },
    Lines = new[] { "Keyboard", "Mouse" },
    Settings = new Dictionary<string, string>(StringComparer.Ordinal) { ["theme"] = "dark" },
};

// Properties are separated by dots, and names are case-insensitive
Assert.Equal("Ada", DataBinder.Eval(order, "Customer.Name"));
Assert.Equal("Ada", DataBinder.Eval(order, "customer.name"));

// Indexes use [] or (): a number for arrays and lists, a quoted string for other indexers
Assert.Equal("Mouse", DataBinder.Eval(order, "Lines[1]"));
Assert.Equal("dark", DataBinder.Eval(order, "Settings['theme']"));

// The overload with a format applies a composite format string to the value
Assert.Equal("Order #42", DataBinder.Eval(order, "Number", "Order #{0}"));
```
<!-- endref -->

- The parts of the path are separated by dots. Property names are case-insensitive.
- An index is written between `[]` or `()`: a number for arrays and lists, a number or a quoted string for any type with
  an indexer, such as a dictionary.
- `Eval` returns `null` when the container or an intermediate value is `null`. A property that does not exist throws an
  `ArgumentException`.
- The overload with a format returns a string: an empty string for `null`, otherwise the value formatted with
  `string.Format` and the current culture.
- `GetPropertyValue` and `GetIndexedPropertyValue` evaluate one part of a path.

## Walk an object graph

`ObjectGraphVisitor` walks an object and everything it references: the values of its public properties and the items of
its collections, recursively. Derive from it and override `VisitValue`, called once for every object, or `VisitProperty`,
called for every property with its value:

<!-- ref:../../tests/Meziantou.Framework.Tests/ObjectGraphVisitorTests.cs;region=ObjectGraphVisitor_StringCollector;format=md-fenced-code-block;dedent=true -->
```csharp
private sealed class StringCollector : ObjectGraphVisitor
{
    public List<string> Strings { get; } = [];

    // Called once for every object of the graph: the root, property values and collection items
    protected override void VisitValue(object value)
    {
        if (value is string text)
        {
            Strings.Add(text);
        }
    }
}
```
<!-- endref -->

<!-- ref:../../tests/Meziantou.Framework.Tests/ObjectGraphVisitorTests.cs;region=ObjectGraphVisitor_Visit;format=md-fenced-code-block;dedent=true -->
```csharp
var order = new
{
    Customer = new { Name = "Ada" },
    Lines = new[] { "Keyboard", "Mouse" },
};

var collector = new StringCollector();
collector.Visit(order);

Assert.Equal(["Ada", "Keyboard", "Mouse"], collector.Strings.Order(StringComparer.Ordinal));
```
<!-- endref -->

- Each object is visited once, compared by reference, so cycles do not loop forever.
- Indexers and write-only properties are skipped. Strings are visited as values, not as collections of characters.
- The properties of every object are visited, including the ones of arrays and collections, such as `Length` or
  `Count`.
