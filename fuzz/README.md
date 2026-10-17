# Fuzzing

The fuzz projects live apart from `tests/` because their campaigns run at a different cadence.

- `Meziantou.Framework.Imaging.FuzzTests`: deterministic mutation fuzz tests over the Imaging golden corpus, with their own
  harness. The weekly campaign is `.github/workflows/fuzz-imaging.yml`.
- `Meziantou.Framework.*.Fuzz`: coverage-guided fuzzing with [SharpFuzz](https://github.com/Metalnem/sharpfuzz) and
  [libFuzzer](https://llvm.org/docs/LibFuzzer.html) (through [libfuzzer-dotnet](https://github.com/Metalnem/libfuzzer-dotnet)).
  The weekly campaign is `.github/workflows/fuzz.yml`.

## SharpFuzz projects

Each library has an executable project, `fuzz/<Library>.Fuzz`, that references the library and
`Meziantou.Framework.Fuzzing` (the shared host). It declares its targets in a `<Library>FuzzTargets.All` list:

```csharp
public static IReadOnlyList<FuzzTarget> All { get; } =
[
    new("yaml-parser", Parser) { Seeds = GetSeeds, Dictionary = Tokens },
];

private static void Parser(ReadOnlySpan<byte> data)
{
    var yaml = FuzzText.Decode(data);
    // Call the library, catch the exceptions it documents for invalid input, check properties with FuzzAssert
}
```

- A target receives the bytes libFuzzer generates. Any exception that escapes it is a failure: catch the exceptions the
  library documents for invalid input, and check the properties of the results with `FuzzAssert` (round trips, agreement
  between two implementations, bounds of the reported positions, ...).
- `FuzzText.Decode` turns the bytes into a string that can hold unpaired surrogates; `FuzzInput` reads option bytes
  (a dialect, a set of options) before the text.
- The seeds are the inputs the corpus starts from, usually the conformance suites of the unit tests, embedded in the fuzz
  project. Every seed must pass.
- The dictionary lists the tokens of the format (keywords, punctuation), which libFuzzer inserts into the inputs.
- Target names are unique across all the projects: they name the corpus caches and the issues.
- `Program.cs` passes a lambda to `FuzzProgram.Run`: when fuzzing, the list is only built once SharpFuzz has set up the
  memory the instrumented code writes its coverage to, so the static fields of the targets may call the library.

```bash
# List the targets, run a target on input files (any OS), write the seeds or the dictionary
dotnet run --project fuzz/Meziantou.Framework.Yaml.Fuzz --framework net10.0 -- --list
dotnet run --project fuzz/Meziantou.Framework.Yaml.Fuzz --framework net10.0 -- yaml-parser crash-1234 corpus/
dotnet run --project fuzz/Meziantou.Framework.Yaml.Fuzz --framework net10.0 -- yaml-parser --seeds seeds/
```

### Running a campaign

libfuzzer-dotnet runs on Linux. `eng/run-fuzz.ps1` publishes the project, instruments the `Meziantou.Framework.*`
assemblies with `sharpfuzz`, and fuzzes each target for a fixed time:

```bash
dotnet tool install --global SharpFuzz.CommandLine --version 2.3.0
curl -L -o libfuzzer-dotnet https://github.com/Metalnem/libfuzzer-dotnet/releases/download/v2025.05.02.0904/libfuzzer-dotnet-ubuntu
chmod +x libfuzzer-dotnet
pwsh eng/run-fuzz.ps1 -Project fuzz/Meziantou.Framework.Yaml.Fuzz -DurationSeconds 60 -LibFuzzer ./libfuzzer-dotnet
```

Use an x64 machine (or an `linux/amd64` container): on arm64, the trap that reports a failure to libFuzzer raises a
signal libFuzzer does not handle, so it does not save the failing input.

Each target gets `artifacts/fuzz/results/<target>`: the libFuzzer log, the failing input (`crash-*`, `timeout-*`),
and `failure.md`, the body of the issue the weekly workflow opens. The corpus is kept in `artifacts/fuzz-corpus`.

### Regressions

Once a failure is fixed, commit its input under `fuzz/<Library>.Fuzz/Regressions/<target>/`.
`Meziantou.Framework.Fuzzing.Tests` runs every target on its seeds and on its regressions in every CI build.
