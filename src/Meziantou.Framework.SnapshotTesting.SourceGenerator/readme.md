# Meziantou.Framework.SnapshotTesting.SourceGenerator

`Meziantou.Framework.SnapshotTesting.SourceGenerator` extends [`Meziantou.Framework.SnapshotTesting`](https://www.nuget.org/packages/Meziantou.Framework.SnapshotTesting) with support for Roslyn source generators. It serializes the `GeneratorDriverRunResult` returned by `GeneratorDriver.GetRunResult()`, so a generator test only has to run the driver and validate the result.

## Setup

Call `AddSourceGenerator()` on your `SnapshotSettings` to register the serializer:

```csharp
using Meziantou.Framework.SnapshotTesting;
using Meziantou.Framework.SnapshotTesting.SourceGenerator;

internal static class SnapshotConfiguration
{
    [ModuleInitializer]
    internal static void Initialize()
    {
        SnapshotSettings.Default.AddSourceGenerator();
    }
}
```

## Usage

```csharp
[Fact]
public void GenerateCode()
{
    var compilation = CSharpCompilation.Create("compilation", [CSharpSyntaxTree.ParseText(source)], references);
    GeneratorDriver driver = CSharpGeneratorDriver.Create(new MyGenerator());
    driver = driver.RunGenerators(compilation);

    Snapshot.Validate(driver.GetRunResult());
}
```

## Snapshot content

- One `.cs` file per generated source, ordered by hint name. The order in which the driver runs the generators is not a contract, so it is not used to name the files.
- Each file starts with a `// HintName: <name>` comment, so renaming a generated file shows up as a diff.
- `\r\n` is normalized to `\n`, so snapshots do not depend on the platform the tests run on.
- A final `.txt` file lists the diagnostics reported by the generators (`<id>: <message>`, ordered by id) and, if a generator threw, the exception it threw. The file is omitted when there is nothing to report, unless the run produced no source at all.

Because a run usually produces several files, the snapshots are numbered: `MyTests.GenerateCode_0.verified.cs`, `MyTests.GenerateCode_1.verified.cs`, `MyTests.GenerateCode_2.verified.txt`, … Adding a generated source therefore shifts the files that come after it in hint-name order.
