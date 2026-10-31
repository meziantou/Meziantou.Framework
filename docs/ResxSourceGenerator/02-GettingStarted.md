---
title: Getting started
description: Install the package, enable the generator for the .resx files of the project, and use the generated class.
---

## Install the package

Add the `Meziantou.Framework.ResxSourceGenerator` package to the project that contains the `.resx` files:

<InstallNuGetPackage Id="Meziantou.Framework.ResxSourceGenerator" />

## Enable the generator

The generator processes the `.resx` files that are `AdditionalFiles` of the project. Add them in the project file:

```xml
<ItemGroup>
  <AdditionalFiles Include="**/*.resx" />
</ItemGroup>
```

The `.resx` files stay `EmbeddedResource` items, as the SDK declares them by default: the generated class reads the
resources that MSBuild embeds in the assembly.

When a project already uses the Visual Studio generator (`ResXFileCodeGenerator` and a `.Designer.cs` file), remove it:
both would declare the same class.

## Use the generated class

For a `Sample.resx` file at the root of the project, the generator writes a `Sample` class in the root namespace, and
a `SampleNames` class with the names of the resources:

```xml
<data name="Hello" xml:space="preserve">
  <value>Hello {0}!</value>
</data>
```

<!-- ref:../../tests/Meziantou.Framework.ResxSourceGenerator.GeneratorTests/ResxGeneratorTests.cs;region=ResxSourceGenerator_Format;format=md-fenced-code-block;dedent=true -->
```csharp
Assert.Equal("Hello {0}!", Sample.Hello);
Assert.Equal("Hello meziantou!", Sample.FormatHello("meziantou"));
```
<!-- endref -->

- `Sample.Hello` returns the string for the current UI culture.
- `Sample.FormatHello` exists because the value contains `{0}`: it takes one argument and formats the string.

A file in a folder gets the namespace of the folder: `Resources/Errors.resx` generates the `Errors` class in the
`<RootNamespace>.Resources` namespace. See [Configuration](05-Configuration.md) to change it.

## See the generated code

The generated code is not written to disk by default. To inspect it, ask the compiler to write the generated files:

```xml
<PropertyGroup>
  <EmitCompilerGeneratedFiles>true</EmitCompilerGeneratedFiles>
  <CompilerGeneratedFilesOutputPath>$(BaseIntermediateOutputPath)/GeneratedFiles</CompilerGeneratedFilesOutputPath>
</PropertyGroup>
```

Each file starts with a comment that lists the values the generator used (namespace, class name, resource name...),
which helps when a class does not have the expected name.

## Next steps

- [Generated members](03-GeneratedMembers.md): the properties and methods of the generated classes.
- [Format parameters](04-FormatParameters.md): give meaningful names and types to the arguments of format methods.
