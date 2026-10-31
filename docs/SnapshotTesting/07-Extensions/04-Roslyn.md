---
title: Roslyn
description: Test source generators, syntax rewriters and analyzers with a single Snapshot.Validate call.
---

`Meziantou.Framework.SnapshotTesting.Roslyn` serializes Roslyn objects, so testing a source generator, a syntax rewriter or
an analyzer is a single [Snapshot.Validate](cref:Meziantou.Framework.SnapshotTesting.Snapshot.Validate) call. Generated code is stored as `.cs` files that you can read and diff.

<InstallNuGetPackage Id="Meziantou.Framework.SnapshotTesting.Roslyn" />

## Set up

Call [AddRoslyn()](cref:Meziantou.Framework.SnapshotTesting.Roslyn.SnapshotSettingsRoslynExtensions.AddRoslyn) on [SnapshotSettings.Default](cref:Meziantou.Framework.SnapshotTesting.SnapshotSettings.Default) in a module initializer ([AddRoslyn](cref:Meziantou.Framework.SnapshotTesting.Roslyn.SnapshotSettingsRoslynExtensions.AddRoslyn) is in the
[Meziantou.Framework.SnapshotTesting.Roslyn](cref:Meziantou.Framework.SnapshotTesting.Roslyn) namespace):

<!-- ref:../../../tests/Meziantou.Framework.SnapshotTesting.Roslyn.Tests/RoslynSnapshotSerializerTests.cs;region=SnapshotSettings_AddRoslyn;format=md-fenced-code-block;dedent=true -->
```csharp
internal static class SnapshotConfiguration
{
    [ModuleInitializer]
    internal static void Initialize()
    {
        SnapshotSettings.Default.AddRoslyn();
    }
}
```
<!-- endref -->

| Value | Snapshot |
| --- | --- |
| `GeneratorDriverRunResult` | One source file per generated source, plus a text file with the diagnostics |
| `SyntaxTree`, `SyntaxNode`, `SyntaxToken`, `SyntaxNodeOrToken`, `SyntaxTrivia`, `SyntaxTokenList`, `SyntaxTriviaList` | A single source file with its full text |
| `SourceText` | A single file with its text |
| `Diagnostic`, and any collection of them | A text file with one diagnostic per line |

Line endings are normalized to `\n` in every file, so the snapshots depend neither on the platform the tests run on nor
on the line endings of the source they were produced from.

## Test a source generator

Run the generator in a test and validate the result of the run:

<!-- ref:../../../tests/Meziantou.Framework.SnapshotTesting.Roslyn.Tests/RoslynSnapshotSerializerTests.cs;region=SnapshotRoslyn_SourceGenerator;format=md-fenced-code-block;dedent=true -->
```csharp
var source = "partial class Customer; partial class Order;";
MetadataReference[] references = [MetadataReference.CreateFromFile(typeof(object).Assembly.Location)];

var compilation = CSharpCompilation.Create("compilation", [CSharpSyntaxTree.ParseText(source)], references);
GeneratorDriver driver = CSharpGeneratorDriver.Create(new MyGenerator());
driver = driver.RunGenerators(compilation);

Snapshot.Validate(driver.GetRunResult());
```
<!-- endref -->

The first snapshot file contains the first generated source:

<!-- ref:../../../tests/Meziantou.Framework.SnapshotTesting.Roslyn.Tests/__snapshots__/RoslynSnapshotSerializerTests_ReadmeExample_SourceGenerator_0.verified.cs;format=md-fenced-code-block;language=csharp -->
```csharp
// HintName: Customer.g.cs
partial class Customer
{
    public override string ToString() => "Customer";
}
```
<!-- endref -->

- There is one `.cs` file per generated source, ordered by hint name. The order in which the driver runs the generators
  is not a contract, so it is not used to name the files.
- Each file starts with a `// HintName: <name>` comment, so renaming a generated file shows up as a diff.
- A final `.txt` file lists the diagnostics reported by the generators and, if a generator threw, the exception it threw.
  The file is omitted when there is nothing to report.

A run usually produces several files, so the snapshots are numbered: `MyTests_GenerateCode_0.verified.cs`,
`MyTests_GenerateCode_1.verified.cs`, `MyTests_GenerateCode_2.verified.txt`, ... Adding a generated source shifts the
files that come after it in hint-name order. A run that generated no source and reported no diagnostic has nothing to
compare, and fails with a [SnapshotException](cref:Meziantou.Framework.SnapshotTesting.SnapshotException).

The snapshot files are `.cs` files in your test project: the package keeps them out of the compilation (see
[Snapshots and the build](../03-WritingTests/03-SnapshotFiles.md#snapshots-and-the-build)).

## Test a syntax rewriter

Validate a syntax tree, or any node of it, for example the output of a syntax rewriter:

<!-- ref:../../../tests/Meziantou.Framework.SnapshotTesting.Roslyn.Tests/RoslynSnapshotSerializerTests.cs;region=SnapshotRoslyn_SyntaxRewriter;format=md-fenced-code-block;dedent=true -->
```csharp
var tree = CSharpSyntaxTree.ParseText("""
    public class Sample
    {
        public class Nested;
    }
    """);

Snapshot.Validate(new MyRewriter().Visit(tree.GetRoot()));
```
<!-- endref -->

The value is stored as a single `.cs` file (`.vb` for Visual Basic) that contains its full text, trivia included. The
text is not reformatted, so a change in the generated whitespace shows up as a diff.

A `SourceText`, and an empty token or trivia list, have no language: they use the extension of the requested snapshot
type, `.txt` by default.

<!-- ref:../../../tests/Meziantou.Framework.SnapshotTesting.Roslyn.Tests/RoslynSnapshotSerializerTests.cs;region=SnapshotRoslyn_SourceText;format=md-fenced-code-block;dedent=true -->
```csharp
var sourceText = SourceText.From("class Sample;\n");
Snapshot.Validate(sourceText, SnapshotType.Create("cs"));
```
<!-- endref -->

## Test an analyzer

Validate the diagnostics reported by an analyzer:

<!-- ref:../../../tests/Meziantou.Framework.SnapshotTesting.Roslyn.Tests/RoslynSnapshotSerializerTests.cs;region=SnapshotRoslyn_Diagnostics;format=md-fenced-code-block;dedent=true -->
```csharp
var source = """
    class Sample
    {
        void DoNotUse() { }
    }
    """;
var compilation = CSharpCompilation.Create("compilation", [CSharpSyntaxTree.ParseText(source, path: "Sample.cs")]);

var diagnostics = await compilation.WithAnalyzers([new MyAnalyzer()]).GetAnalyzerDiagnosticsAsync();

Snapshot.Validate(diagnostics);
```
<!-- endref -->

Each diagnostic is written on its own line, the way the compiler reports it:

<!-- ref:../../../tests/Meziantou.Framework.SnapshotTesting.Roslyn.Tests/__snapshots__/RoslynSnapshotSerializerTests_ReadmeExample_Diagnostics.verified.txt;format=md-fenced-code-block;language=text -->
```text
Sample.cs(3,10): warning MY0001: Do not use this method
```
<!-- endref -->

- The message is formatted with the invariant culture, so the snapshot does not depend on the UI culture of the machine.
- Positions are one-based, like the compiler reports them. A diagnostic without a location is written without a path.
- The diagnostics are sorted by path, position, ID, then message, so the snapshot does not depend on the order in which
  the analyzers reported them, which is not deterministic when they run concurrently.
- An empty collection produces an empty file: that is what a test that asserts no diagnostic is reported expects.
- The path comes from the syntax tree. A tree parsed from a string has no path; a tree read from disk carries the path
  it was read with, which depends on the machine unless it is relative. Use a
  [scrubber](../03-WritingTests/06-DeterministicSnapshots.md#scrubbers) if that path ends up in the snapshot.

## Roslyn values inside other snapshots

Roslyn values nested inside another object are written by the human-readable serializer, which would otherwise dump their
object graph. [AddRoslyn()](cref:Meziantou.Framework.SnapshotTesting.Roslyn.SnapshotSettingsRoslynExtensions.AddRoslyn) registers converters for them:

| Type | Written as |
| --- | --- |
| `Diagnostic` | `Sample.cs(4,10): warning MY0001: message` |
| `Location` | `Sample.cs(3,9)-(3,20)`, or its `LocationKind` when it is not in source |
| `LinePosition` | `3,9` |
| `LinePositionSpan` | `(3,9)-(3,20)` |
| `TextSpan` | `[42..53)` |

`LinePosition` and `LinePositionSpan` keep the zero-based values Roslyn exposes. Only a diagnostic is written one-based,
because that is how the compiler reports it.
