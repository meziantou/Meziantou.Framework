---
title: Source generation
description: Generate the serialization code at build time with a CsvSerializerContext for trimming and Native AOT, and the diagnostics the generator reports.
---

By default, [CsvSerializer](cref:Meziantou.Framework.Csv.Serialization.CsvSerializer) discovers your types with reflection the first time it sees them. The source generator does this
work when you build instead: it writes the metadata of your types into a [CsvSerializerContext](cref:Meziantou.Framework.Csv.Serialization.CsvSerializerContext). The generator ships in
the `Meziantou.Framework.Csv` package: there is nothing else to install.

## When to use it

- **The application is trimmed or published with Native AOT.** The methods that use reflection are annotated with
  `[RequiresUnreferencedCode]` and `[RequiresDynamicCode]`, so the compiler warns when you call them.
- **You want errors when you build.** An invalid converter or a type without a usable constructor is a compiler error,
  not an exception the first time a file is read.
- **Startup matters.** The members, the constructors and the converters are not discovered with reflection at runtime.
- **Reading speed matters.** The generated code sets the members of each record directly, which is about 5 to 13% faster
  than with reflection.

## Declare a context

Declare a `partial` class that derives from [CsvSerializerContext](cref:Meziantou.Framework.Csv.Serialization.CsvSerializerContext), with a [CsvSerializableAttribute](cref:Meziantou.Framework.Csv.Serialization.CsvSerializableAttribute) for each type of
record. [CsvSourceGenerationOptionsAttribute](cref:Meziantou.Framework.Csv.Serialization.CsvSourceGenerationOptionsAttribute) sets the options of the `Default` instance of the context:

<!-- ref:../../tests/Meziantou.Framework.Csv.Tests/CsvSerializerSourceGenerationTests.cs;region=CsvSerializerContext_Declare;format=md-fenced-code-block;dedent=true -->
```csharp
[CsvSerializable(typeof(Product))]
[CsvSerializable(typeof(Person))]
[CsvSourceGenerationOptions(Separator = ';')]
public partial class AppCsvContext : CsvSerializerContext
{
}
```
<!-- endref -->

## Use the context

The generator adds a static `Default` instance, and a [CsvTypeInfo\<T\>](cref:Meziantou.Framework.Csv.Serialization.Metadata.CsvTypeInfo{T}) property for each type, named after the type or
after [TypeInfoPropertyName](cref:Meziantou.Framework.Csv.Serialization.CsvSerializableAttribute.TypeInfoPropertyName). Pass the property to the serializer:

<!-- ref:../../tests/Meziantou.Framework.Csv.Tests/CsvSerializerSourceGenerationTests.cs;region=CsvSerializerContext_Use;format=md-fenced-code-block;dedent=true -->
```csharp
Product[] products = [new(1, "Keyboard", 49.90m), new(2, "Mouse", 19.99m)];

// The metadata comes from the generated code: no reflection, compatible with trimming and Native AOT
var csv = CsvSerializer.Serialize(products, AppCsvContext.Default.Product);
Assert.Equal("Id;Name;Price\r\n1;Keyboard;49.90\r\n2;Mouse;19.99\r\n", csv);

var result = CsvSerializer.Deserialize(csv, AppCsvContext.Default.Product);
Assert.Equal(products, result);
```
<!-- endref -->

To use other options, create an instance of the context with them, such as `new AppCsvContext(options)`, and keep it:
the context caches the metadata of its types. A context only gives the metadata of the types it declares; it does not
fall back to reflection.

The generated code reads and writes the same columns as reflection. It reaches the non-public members included with
[CsvIncludeAttribute](cref:Meziantou.Framework.Csv.Serialization.CsvIncludeAttribute), the `init` accessors and the constructors of types with `required` members through `UnsafeAccessor`,
which works with Native AOT.

When it reads a file, the generated code also converts and sets each member of a record directly, without a virtual
call: the compiler can inline the converter of each member. It does so when the columns of the members are in their
declaration order, as in the files the serializer writes, and when no
[DeserializationErrorHandler](cref:Meziantou.Framework.Csv.Serialization.CsvSerializerOptions.DeserializationErrorHandler) is set. Otherwise, the members are set as with reflection. The records
and the errors are the same in both cases.

## Native AOT and trimming

The library is trimmable and compatible with Native AOT. When an application is published with Native AOT, the
`MeziantouFrameworkCsvIsReflectionEnabledByDefault` MSBuild property is `false`: the reflection-based code is removed,
and the overloads that take a [CsvSerializerOptions](cref:Meziantou.Framework.Csv.Serialization.CsvSerializerOptions) without a context throw an `InvalidOperationException`.
[IsReflectionEnabledByDefault](cref:Meziantou.Framework.Csv.Serialization.CsvSerializer.IsReflectionEnabledByDefault) gives the value of the switch. [CsvReader](cref:Meziantou.Framework.Csv.CsvReader), [CsvWriter](cref:Meziantou.Framework.Csv.CsvWriter) and their UTF-8 versions do not use
reflection.

## Diagnostics

| Id | Severity | Description |
| --- | --- | --- |
| MFCSV001 | Error | The context, or a type that contains it, is not `partial`. |
| MFCSV002 | Error | A class with [CsvSerializableAttribute](cref:Meziantou.Framework.Csv.Serialization.CsvSerializableAttribute) does not derive from [CsvSerializerContext](cref:Meziantou.Framework.Csv.Serialization.CsvSerializerContext). |
| MFCSV003 | Error | The context is generic, or is declared in a generic type. |
| MFCSV004 | Error | The type cannot be serialized: it is abstract, an interface or an open generic type, it is not accessible from the context, or it has no usable constructor. |
| MFCSV005 | Error | The type of a [CsvConverterAttribute](cref:Meziantou.Framework.Csv.Serialization.CsvConverterAttribute) is not a converter with a public parameterless constructor. |
| MFCSV006 | Error | A member cannot be serialized: its type is not accessible from the context, or it needs an `UnsafeAccessor` on a generic type. |
| MFCSV007 | Warning | Two members have the same column name: only the first one is read. |
| MFCSV008 | Error | Two types of the context have the same property name: set [TypeInfoPropertyName](cref:Meziantou.Framework.Csv.Serialization.CsvSerializableAttribute.TypeInfoPropertyName). |
