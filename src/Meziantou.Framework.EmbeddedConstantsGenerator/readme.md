# Meziantou.Framework.EmbeddedConstantsGenerator

Generate a static partial class that exposes selected files as text and byte constants.

## Installation

```bash
dotnet package add Meziantou.Framework.EmbeddedConstantsGenerator
```

## Usage

```xml
<PropertyGroup>
  <EmbeddedConstantsNamespace>MyApp.Generated</EmbeddedConstantsNamespace>
  <EmbeddedConstantsClassName>EmbeddedFiles</EmbeddedConstantsClassName>
</PropertyGroup>

<ItemGroup>
  <EmbeddedConstant Include="Assets\hello.txt" Kind="Text" />
  <EmbeddedConstant Include="Assets\logo.bin" Kind="Binary" />
  <EmbeddedConstant Include="Assets\config.json"
                    Kind="Both"
                    Name="ConfigJson" />
</ItemGroup>
```

The package uses an MSBuild task so binary files are read byte-for-byte during the build. The generated type is always `static partial`. Text files generate a `const string` member. Binary files generate a `ReadOnlySpan<byte>` member. Files marked as `Both` generate both members.

```csharp
namespace MyApp.Generated;

internal static partial class EmbeddedFiles
{
    public const string HelloText = "...";
    public static ReadOnlySpan<byte> LogoBytes => new byte[] { ... };
    public const string ConfigJsonText = "...";
    public static ReadOnlySpan<byte> ConfigJsonBytes => new byte[] { ... };
}
```

## MSBuild Properties

| Property | Default | Description |
| -- | -- | -- |
| `EmbeddedConstantsNamespace` | `$(RootNamespace)`, then `$(MSBuildProjectName)` | Namespace of the generated class |
| `EmbeddedConstantsClassName` | `EmbeddedConstants` | Name of the generated class |
| `EmbeddedConstantsClassVisibility` | `internal` | Generated class visibility: `internal` or `public` |
| `EmbeddedConstantsMemberVisibility` | `public` | Generated member visibility: `internal` or `public` |
| `EmbeddedConstantsOutputPath` | `$(IntermediateOutputPath)\Meziantou.Framework.EmbeddedConstantsGenerator\EmbeddedConstants.g.cs` | Generated C# file path |

The prefixed forms `Meziantou_EmbeddedConstantsNamespace`, `Meziantou_EmbeddedConstantsClassName`, `Meziantou_EmbeddedConstantsClassVisibility`, `Meziantou_EmbeddedConstantsMemberVisibility`, and `Meziantou_EmbeddedConstantsOutputPath` are also supported and take precedence over the shorter aliases.

## EmbeddedConstant Metadata

| Metadata | Required | Description |
| -- | :--: | -- |
| `Kind` | ✔️ | `Text`, `Binary`, or `Both` |
| `Name` |  | Base name used for generated members |

The forms `EmbeddedConstantKind`, `Meziantou_EmbeddedConstantKind`, `EmbeddedConstantName`, and `Meziantou_EmbeddedConstantName` are also supported for compatibility.

## Naming

When `Name` is set, the value is converted to a valid PascalCase identifier and used as the member base name. Otherwise, the file name without extension is used. Invalid identifier characters are treated as word separators, and names that start with a digit are prefixed with `_`.

Text files append `Text` to the base name. Binary files append `Bytes`. Files marked as `Both` append both `Text` and `Bytes`.

Examples:

| File | Kind | Members |
| -- | -- | -- |
| `hello-world.txt` | `Text` | `HelloWorldText` |
| `appsettings.Development.json` | `Both` | `AppsettingsDevelopmentText`, `AppsettingsDevelopmentBytes` |
| `logo.bin` | `Binary` | `LogoBytes` |

If two implicit names collide, the generator tries a path-based name. Remaining collisions are reported as MSBuild errors.

## Text Encoding

Text files must be valid UTF-8. An optional UTF-8 BOM is ignored. Binary files are embedded as-is.

Text files larger than 1 MiB are rejected to avoid producing impractically large generated source. Binary files are emitted as byte arrays and should also be kept reasonably small.

## MSBuild Errors

| Id | Description |
| -- | -- |
| `MFECG0001` | Embedded constants namespace is invalid |
| `MFECG0002` | Embedded constants class name is invalid |
| `MFECG0003` | Embedded constant kind is missing |
| `MFECG0004` | Embedded constant kind is invalid |
| `MFECG0005` | Embedded constant member name is duplicated |
| `MFECG0006` | Embedded constant file cannot be read |
| `MFECG0007` | Embedded constant text file is not valid UTF-8 |
| `MFECG0008` | Embedded constant text file is too large |
| `MFECG0009` | Embedded constants visibility is invalid |
