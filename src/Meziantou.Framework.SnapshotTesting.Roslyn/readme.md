# Meziantou.Framework.SnapshotTesting.Roslyn

`Meziantou.Framework.SnapshotTesting.Roslyn` extends [`Meziantou.Framework.SnapshotTesting`](https://www.nuget.org/packages/Meziantou.Framework.SnapshotTesting) with support for Roslyn objects: the `GeneratorDriverRunResult` returned by `GeneratorDriver.GetRunResult()`, and `SyntaxTree` / `SyntaxNode`.

## Setup

Call `AddRoslyn()` on your `SnapshotSettings` to register the serializers:

```csharp
using Meziantou.Framework.SnapshotTesting;
using Meziantou.Framework.SnapshotTesting.Roslyn;

internal static class SnapshotConfiguration
{
    [ModuleInitializer]
    internal static void Initialize()
    {
        SnapshotSettings.Default.AddRoslyn();
    }
}
```

## Source generators

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

The snapshot contains:

- One `.cs` file per generated source, ordered by hint name. The order in which the driver runs the generators is not a contract, so it is not used to name the files.
- Each file starts with a `// HintName: <name>` comment, so renaming a generated file shows up as a diff.
- A final `.txt` file listing the diagnostics reported by the generators (`<id>: <message>`, ordered by id) and, if a generator threw, the exception it threw. The file is omitted when there is nothing to report.

Because a run usually produces several files, the snapshots are numbered: `MyTests.GenerateCode_0.verified.cs`, `MyTests.GenerateCode_1.verified.cs`, `MyTests.GenerateCode_2.verified.txt`, … Adding a generated source therefore shifts the files that come after it in hint-name order.

A run that generated no source and reported no diagnostic has nothing to compare, and fails with a `SnapshotException`.

## Syntax trees and nodes

```csharp
[Fact]
public void RewriteSyntaxTree()
{
    var tree = CSharpSyntaxTree.ParseText(source);

    Snapshot.Validate(new MyRewriter().Visit(tree.GetRoot()));
}
```

The node or tree is stored as a single `.cs` file (`.vb` for Visual Basic) containing its full text, trivia included. The text is not reformatted, so a change in the generated whitespace shows up as a diff.

## Line endings

`\r\n` is normalized to `\n` in every file, so snapshots do not depend on the platform the tests run on.
