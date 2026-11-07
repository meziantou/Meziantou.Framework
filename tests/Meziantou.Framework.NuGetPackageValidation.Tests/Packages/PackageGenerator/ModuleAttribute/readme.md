# Debug_ModuleAttribute

Generates `Debug_ModuleAttribute.1.0.0.nupkg`.

`Class1.cs` applies an attribute it declares itself to its own module. The row for that attribute in the
`CustomAttribute` table therefore has a `MethodDefinitionHandle` constructor, and it sorts before the
assembly-level `DebuggableAttribute`. Rules that walk `CustomAttributes` must cope with that instead of
assuming every constructor is a `MemberReferenceHandle`.

The project file is not committed, because every `csproj` under `tests/` is added to the solution by
`eng/update-all.cs` and this one is only used to produce the fixture. To regenerate the package, create
`samplelib.csproj` in this folder with the following content:

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <TargetFramework>net6.0</TargetFramework>
    <Version>1.0.0</Version>
  </PropertyGroup>

</Project>
```

then run the following command from the `Packages` folder, and delete `samplelib.csproj`, `bin` and `obj`
afterwards:

```
dotnet pack PackageGenerator/ModuleAttribute/samplelib.csproj --output . --configuration Debug /p:PackageId=Debug_ModuleAttribute
```
