---
title: TypeConverter and configuration
description: Convert IDs from and to strings with TypeDescriptor, and bind them from configuration files and environment variables.
---

## Convert with a TypeConverter

The generator adds a `TypeConverter` to each ID and registers it with `[TypeConverter]`. Libraries that convert
values through `TypeDescriptor`, such as `Microsoft.Extensions.Configuration`, MVC model binding and UI frameworks,
handle IDs like any simple type:

<!-- ref:../../../tests/Meziantou.Framework.StronglyTypedId.GeneratorTests/StronglyTypedIdTests.cs;region=StronglyTypedId_TypeConverter;format=md-fenced-code-block;dedent=true -->
```csharp
var converter = TypeDescriptor.GetConverter(typeof(ProjectId));

Assert.Equal(ProjectId.FromInt32(42), converter.ConvertFromInvariantString("42"));
Assert.Equal(ProjectId.FromInt32(42), converter.ConvertFrom(42));
Assert.Equal("42", converter.ConvertToInvariantString(ProjectId.FromInt32(42)));
```
<!-- endref -->

The converter converts:

- **from** a `string` (with `Parse`), the underlying type or the ID itself;
- **to** a `string` ([ValueAsString](cref:Meziantou.Framework.IStronglyTypedId.ValueAsString)), the underlying type or the ID itself.

The culture given to the converter is ignored: the conversion always uses the invariant culture. A `string` that does
not parse throws a `FormatException`.

## Bind configuration

The configuration binder converts the strings of `appsettings.json`, environment variables and the other configuration
sources with the `TypeConverter`, so an options class can use IDs directly:

<!-- ref:../../../tests/Meziantou.Framework.StronglyTypedId.GeneratorTests/StronglyTypedIdTests.cs;region=StronglyTypedId_BindConfigurationModel;format=md-fenced-code-block;dedent=true -->
```csharp
public sealed class ImportOptions
{
    public ProjectId DefaultProject { get; set; }
    public IList<ProjectId> ArchivedProjects { get; } = [];
}
```
<!-- endref -->

<!-- ref:../../../tests/Meziantou.Framework.StronglyTypedId.GeneratorTests/StronglyTypedIdTests.cs;region=StronglyTypedId_BindConfiguration;format=md-fenced-code-block;dedent=true -->
```csharp
var configuration = new ConfigurationBuilder()
    .AddInMemoryCollection(new Dictionary<string, string?>
    {
        ["Import:DefaultProject"] = "42",
        ["Import:ArchivedProjects:0"] = "7",
        ["Import:ArchivedProjects:1"] = "8",
    })
    .Build();

// The configuration binder converts the strings with the generated TypeConverter
var options = configuration.GetSection("Import").Get<ImportOptions>();
Assert.Equal(ProjectId.FromInt32(42), options!.DefaultProject);
Assert.Equal([ProjectId.FromInt32(7), ProjectId.FromInt32(8)], options.ArchivedProjects);
```
<!-- endref -->

The same options class works with `services.Configure<ImportOptions>(...)` and `IOptions<ImportOptions>`.
