# Meziantou.Framework.ResxSourceGenerator

Generate code to access the content of resx files. It does provides more methods than the generator provided by Visual Studio. For instance, it parses placeholders in text and provides method

````xml
<!-- Sample.resx -->
<?xml version="1.0" encoding="utf-8"?>
<root>
  <data name="Hello" xml:space="preserve">
    <value>Hello {0}!</value>
  </data>
</root>
````

````c#
_ = Sample.Hello; // Hello {0}
_ = Sample.FormatHello("meziantou"); // Hello meziantou
````

The generator also supports binary resources and expose them as `byte[]`.

You can customize generated format method parameter names, types, and XML documentation comments by adding metadata in the `https://meziantou.net/meziantou.framework/resxgenerator` namespace:

````xml
<root xmlns:mfrg="https://meziantou.net/meziantou.framework/resxgenerator">
  <data name="Location" xml:space="preserve">
    <value>I live in country {0}, city {1}</value>
    <mfrg:parameter name="country" comment="Country name." />
    <mfrg:parameter name="city" typename="global::System.String" comment="City name." />
  </data>
</root>
````

The parameter elements are matched to composite format placeholders by their order in the `.resx` file. When `typename` is omitted, the generated parameter type is `object?`.

## How to configure the source generator

Install the NuGet package `Meziantou.Framework.ResxSourceGenerator` ([NuGet](https://www.nuget.org/packages/Meziantou.Framework.ResxSourceGenerator/))

````bash
dotnet package add Meziantou.Framework.ResxSourceGenerator
````

````xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <!-- Debug -->
    <EmitCompilerGeneratedFiles>true</EmitCompilerGeneratedFiles>
    <CompilerGeneratedFilesOutputPath>$(BaseIntermediateOutputPath)\GeneratedFiles</CompilerGeneratedFilesOutputPath>

    <!-- optional -->
    <DefaultResourcesNamespace>Sample</DefaultResourcesNamespace>
  </PropertyGroup>

  <ItemGroup>
    <!-- Enable the source generator for all resx files in the project -->
    <AdditionalFiles Include="**/*.resx" />

    <!-- Use advanced configuration for a specific resx file -->
    <AdditionalFiles Include="file1.resx"
                     Namespace="CustomNamespace"
                     ClassName="CustomClassName"
                     ResourceName="CustomResourceFileName"
                     Visibility="public"
                     GenerateResourcesType="True"
                     GenerateKeyNamesType="True"
                     />
  </ItemGroup>

</Project>
````

## Analyzer rules

<!-- analyzer-rules -->
| Id | Category | Description | Severity | Enabled |
| -- | -- | -- | :--: | :--: |
| `MFRG0001` | ResxGenerator | Couldn't parse Resx file | Warning | ✔️ |
| `MFRG0003` | ResxGenerator | Couldn't compute resource name | Warning | ✔️ |
| `MFRG0004` | ResxGenerator | Inconsistent properties | Warning | ✔️ |
<!-- analyzer-rules -->
