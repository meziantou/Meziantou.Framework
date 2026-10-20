# Fuzzing

The fuzz projects live apart from `tests/` because their campaigns run at a different cadence.

- `Meziantou.Framework.Imaging.FuzzTests`: deterministic mutation fuzz tests over the Imaging golden corpus, with their own
  harness.
- `Meziantou.Framework.*.Fuzz`: coverage-guided fuzzing with [SharpFuzz](https://github.com/Metalnem/sharpfuzz) and
  [libFuzzer](https://llvm.org/docs/LibFuzzer.html) (through [libfuzzer-dotnet](https://github.com/Metalnem/libfuzzer-dotnet)).

Both run weekly, on x64 and on arm64. `.github/workflows/fuzz-sharpfuzz.yml` fuzzes the SharpFuzz projects: its
`FUZZ_PROJECTS` table gives the component label of each project and the night of its campaign, and a manual run fuzzes
one component or all of them. `.github/workflows/fuzz-imaging.yml` runs the Imaging fuzz tests. Two campaigns run each
night at 3:00 (Toronto time): a new project gets a row on a night with the fewest, and an option of the `component`
input (`Meziantou.Framework.Fuzzing.Tests` checks both). `eng/run-fuzz.ps1` (SharpFuzz) and `eng/run-imaging-fuzz.ps1`
(Imaging) write a `result.json` and a `failure.md` file for each target under `artifacts/fuzz/results`, and
`eng/report-fuzz-failures.ps1` opens an issue for each failed target, labeled with the component of the project.

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
- Target names are unique across all the projects: they name the corpus directories and the issues.
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

There is no arm64 release of libfuzzer-dotnet. On arm64, build it from source, with `abort()` instead of
`__builtin_trap()`: the trap that reports a failure to libFuzzer raises a SIGTRAP there, which libFuzzer does not handle,
so it would not save the failing input. The weekly workflows fuzz on both architectures, so that the hardware
intrinsics paths of both (SSE/AVX and AdvSimd) run.

```bash
sudo apt-get install -y --no-install-recommends clang libclang-rt-dev
curl -L -o libfuzzer-dotnet.cc https://raw.githubusercontent.com/Metalnem/libfuzzer-dotnet/bd39d4e88d715ab460a929943645be2a186cde52/libfuzzer-dotnet.cc
sed -i 's/__builtin_trap();/abort();/' libfuzzer-dotnet.cc
clang -O2 -fsanitize=fuzzer libfuzzer-dotnet.cc -o libfuzzer-dotnet
```

Each target gets `artifacts/fuzz/results/<target>`: the libFuzzer log, the failing input (`crash-*`, `timeout-*`),
and `failure.md`, the body of the issue the weekly workflow opens.

### Corpus

The corpus of each target is kept in `artifacts/fuzz-corpus/<target>`: libFuzzer adds the inputs that reach new
coverage, and `eng/run-fuzz.ps1` minimizes it after a campaign that passes. Each weekly campaign starts from the corpus
the previous one reached:

- Every run of `fuzz-sharpfuzz.yml` uploads the corpus of every project as its `fuzz-corpus` artifact, laid out as
  `<project>/<arch>/<target>`. Its `save-corpus` job takes the `fuzz-corpus` artifact of the previous run and replaces
  the corpus of the projects the run fuzzed.
- Before fuzzing, `eng/restore-fuzz-corpus.ps1` downloads the most recent `fuzz-corpus` artifact, from the runs of the
  current branch, else from the runs of `main`, and extracts the corpus of the project: the corpus of the same
  architecture, else the corpus of the other one (the inputs do not depend on it). Without any, the campaign starts from
  the seeds.

The corpus is an artifact, not a cache. The other caches of the repository fill its 10 GB cache storage in about a
day, and GitHub evicts the least recently used caches first, so a corpus cache was evicted long before the next weekly
campaign. Artifacts are not evicted, but they expire after 7 days (the retention of the repository), the interval
between two campaigns of a project. Carrying the corpus of every project over to every run, one run each night, keeps
it alive: it is lost only when the workflow does not complete a run for 7 days.

To fuzz locally from the corpus of the weekly campaigns (needs an authenticated GitHub CLI):

```bash
pwsh eng/restore-fuzz-corpus.ps1 -Path artifacts/fuzz-corpus -Project Meziantou.Framework.Yaml.Fuzz -Architecture x64 -Repository meziantou/Meziantou.Framework -Branch main
```

### Regressions

Once a failure is fixed, commit its input under `fuzz/<Library>.Fuzz/Regressions/<target>/`.
`Meziantou.Framework.Fuzzing.Tests` runs every target on its seeds and on its regressions in every CI build.

## Imaging fuzz tests

`Meziantou.Framework.Imaging.FuzzTests` is a test project: every CI build runs it with a small, deterministic budget, and
the `MEZIANTOU_FRAMEWORK_IMAGING_FUZZ_*` environment variables (`FuzzSettings`) set a longer campaign. `eng/run-imaging-fuzz.ps1`
builds and runs it, then writes a result for each failed test and one for the campaign (any OS):

```bash
export MEZIANTOU_FRAMEWORK_IMAGING_FUZZ_SEED=1234
export MEZIANTOU_FRAMEWORK_IMAGING_FUZZ_ITERATIONS=2000000000
export MEZIANTOU_FRAMEWORK_IMAGING_FUZZ_DURATION_SECONDS=60
pwsh eng/run-imaging-fuzz.ps1 -Filter '--filter-class Meziantou.Framework.Imaging.FuzzTests.MutationFuzzTests'
```

The minimized failing inputs are written to `artifacts/fuzz/inputs`. Once a failure is fixed, commit the minimized input
under `fuzz/Meziantou.Framework.Imaging.FuzzTests/FuzzRegressions` (see `regressions.json`).
