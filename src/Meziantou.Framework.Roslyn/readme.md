# Meziantou.Framework.Roslyn

`Meziantou.Framework.Roslyn` provides source helpers for Roslyn analyzers and source generators. The package does not ship a library assembly; helper code is compiled into the consuming project.

## Usage

Install the package in an analyzer or source generator project that already references Roslyn packages:

````xml
<ItemGroup>
  <PackageReference Include="Meziantou.Framework.Roslyn" Version="x.y.z" PrivateAssets="all" />
  <PackageReference Include="Microsoft.CodeAnalysis.CSharp" Version="5.6.0" PrivateAssets="all" />
</ItemGroup>
````

Then import the helper namespace:

````csharp
using Meziantou.Framework.Roslyn;
````

## Helpers

- `Compilation.GetBestTypeByMetadataName`
- `Compilation.IsNet9OrGreater`
- `ContextExtensions.ReportDiagnostic`
- `DiagnosticReporter`
- `IOperation.UnwrapImplicitConversions`
- `LanguageVersionExtensions`
- `LocalDataFlowAnalysis`
- `LocationExtensions`
- `MethodSymbolExtensions`
- `NamespaceSymbolExtensions`
- `SuppressorHelpers`
- `SymbolAttributeExtensions`
- `ISymbol.GetFirstSourceLocation`
- `ISymbol.IsVisibleOutsideOfAssembly`
- `ITypeSymbol.GetUnderlyingNullableTypeOrSelf`

The package also defines Roslyn and C# feature constants before compilation based on the referenced `Microsoft.CodeAnalysis.*` package version, such as `ROSLYN_4_8_OR_GREATER` and `CSHARP12_OR_GREATER`.

## Compiler warnings

The helper files suppress all compiler warnings so they don't pollute the consuming project's build. Define the `MEZIANTOU_FRAMEWORK_ROSLYN_ENABLE_WARNINGS` constant to report them:

````xml
<PropertyGroup>
  <DefineConstants>$(DefineConstants);MEZIANTOU_FRAMEWORK_ROSLYN_ENABLE_WARNINGS</DefineConstants>
</PropertyGroup>
````
