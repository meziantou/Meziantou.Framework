# Meziantou.Framework.SnapshotTesting.Roslyn

`Meziantou.Framework.SnapshotTesting.Roslyn` extends [`Meziantou.Framework.SnapshotTesting`](https://www.nuget.org/packages/Meziantou.Framework.SnapshotTesting) with support for Roslyn objects, so testing a source generator, a syntax rewriter or an analyzer is a single `Snapshot.Validate` call.

## Setup

Call `AddRoslyn()` on your `SnapshotSettings` to register the serializers and converters. `AddRoslyn` is in the `Meziantou.Framework.SnapshotTesting.Roslyn` namespace, and `ModuleInitializer` in `System.Runtime.CompilerServices`:

<!-- ref:../../tests/Meziantou.Framework.SnapshotTesting.Roslyn.Tests/RoslynSnapshotSerializerTests.cs;region=SnapshotSettings_AddRoslyn;format=md-fenced-code-block;dedent=true -->
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

## Supported types

| Type | Snapshot |
| ---- | -------- |
| `GeneratorDriverRunResult` | One source file per generated source, plus a text file with the diagnostics |
| `SyntaxTree`, `SyntaxNode`, `SyntaxToken`, `SyntaxNodeOrToken`, `SyntaxTrivia`, `SyntaxTokenList`, `SyntaxTriviaList` | A single source file with its full text |
| `SourceText` | A single file with its text |
| `Diagnostic`, and any collection of them | A text file with one diagnostic per line |

## Source generators

Run the generator in a test and validate the result of the run:

<!-- ref:../../tests/Meziantou.Framework.SnapshotTesting.Roslyn.Tests/RoslynSnapshotSerializerTests.cs;region=SnapshotRoslyn_SourceGenerator;format=md-fenced-code-block;dedent=true -->
```csharp
var source = "partial class Customer; partial class Order;";
MetadataReference[] references = [MetadataReference.CreateFromFile(typeof(object).Assembly.Location)];

var compilation = CSharpCompilation.Create("compilation", [CSharpSyntaxTree.ParseText(source)], references);
GeneratorDriver driver = CSharpGeneratorDriver.Create(new MyGenerator());
driver = driver.RunGenerators(compilation);

Snapshot.Validate(driver.GetRunResult());
```
<!-- endref -->

- One `.cs` file per generated source, ordered by hint name. The order in which the driver runs the generators is not a contract, so it is not used to name the files.
- Each file starts with a `// HintName: <name>` comment, so renaming a generated file shows up as a diff.
- A final `.txt` file listing the diagnostics reported by the generators and, if a generator threw, the exception it threw. The file is omitted when there is nothing to report.

Because a run usually produces several files, the snapshots are numbered: `MyTests_GenerateCode_0.verified.cs`, `MyTests_GenerateCode_1.verified.cs`, `MyTests_GenerateCode_2.verified.txt`, … Adding a generated source therefore shifts the files that come after it in hint-name order.

A run that generated no source and reported no diagnostic has nothing to compare, and fails with a `SnapshotException`.

## Syntax trees, nodes, tokens and trivia

Validate the tree, or any node of it, for example the output of a syntax rewriter:

<!-- ref:../../tests/Meziantou.Framework.SnapshotTesting.Roslyn.Tests/RoslynSnapshotSerializerTests.cs;region=SnapshotRoslyn_SyntaxRewriter;format=md-fenced-code-block;dedent=true -->
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

The value is stored as a single `.cs` file (`.vb` for Visual Basic) containing its full text, trivia included. The text is not reformatted, so a change in the generated whitespace shows up as a diff.

A `SourceText`, and an empty token or trivia list, have no language attached: they use the extension of the requested snapshot type, which defaults to `.txt`.

<!-- ref:../../tests/Meziantou.Framework.SnapshotTesting.Roslyn.Tests/RoslynSnapshotSerializerTests.cs;region=SnapshotRoslyn_SourceText;format=md-fenced-code-block;dedent=true -->
```csharp
var sourceText = SourceText.From("class Sample;\n");
Snapshot.Validate(sourceText, SnapshotType.Create("cs"));
```
<!-- endref -->

## Diagnostics

Validate the diagnostics reported by an analyzer:

<!-- ref:../../tests/Meziantou.Framework.SnapshotTesting.Roslyn.Tests/RoslynSnapshotSerializerTests.cs;region=SnapshotRoslyn_Diagnostics;format=md-fenced-code-block;dedent=true -->
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

```text
Sample.cs(3,10): warning MY0001: Do not use this method
warning MY0002: This diagnostic has no location
```

- The message is formatted with the invariant culture, so the snapshot does not depend on the machine's UI culture. `Diagnostic.ToString()` uses the current UI culture, which is why it is not used here.
- Positions are one-based, like the compiler reports them.
- The diagnostics are sorted by path, position, ID, then message, so the snapshot does not depend on the order the analyzers reported them in, which is not deterministic when they run concurrently.
- An empty collection produces an empty file: that is the point when the test asserts no diagnostic is reported.
- The path comes from the syntax tree. A tree parsed from a string has no path; a tree read from disk carries the path it was read with, which is machine-specific unless it is relative. Use a [scrubber](https://www.nuget.org/packages/Meziantou.Framework.SnapshotTesting) if that path ends up in the snapshot.

## Nested values

Roslyn values nested inside another snapshot are written by the human-readable serializer, which would otherwise dump their object graph. `AddRoslyn()` registers converters for them:

| Type | Written as |
| ---- | ---------- |
| `Diagnostic` | `Sample.cs(4,10): warning MY0001: message` |
| `Location` | `Sample.cs(3,9)-(3,20)`, or its `LocationKind` when it is not in source |
| `LinePosition` | `3,9` |
| `LinePositionSpan` | `(3,9)-(3,20)` |
| `TextSpan` | `[42..53)` |

`LinePosition` and `LinePositionSpan` keep the zero-based values Roslyn exposes. Only a diagnostic is reported one-based, because that is how the compiler reports it.

## Line endings

Line endings are normalized to `\n` in every file, so snapshots do not depend on the platform the tests run on, nor on the line endings used by the source they were produced from.
