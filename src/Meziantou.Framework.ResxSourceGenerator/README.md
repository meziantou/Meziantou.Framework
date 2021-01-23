# Source Generator usage

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
    <PackageReference Include="Meziantou.Framework.ResxSourceGenerator" Version="1.0.0" />
    <AdditionalFiles Include="**/*.resx" />
    <AdditionalFiles Include="file1.resx" Namespace="CustomNamespace" ClassName="CustomClassName" ResourceName="CustomResourceFileName" />
  </ItemGroup>

</Project>
````

