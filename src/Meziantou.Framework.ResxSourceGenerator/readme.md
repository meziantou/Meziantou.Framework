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

## How to configure the source generator

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
    <!-- Reference the source generator -->
    <PackageReference Include="Meziantou.Framework.ResxSourceGenerator" Version="1.0.0" />

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
