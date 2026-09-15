# Meziantou.Framework.SnapshotTesting

`Meziantou.Framework.SnapshotTesting` validates serialized values against snapshot files stored on disk.

## Basic usage

```csharp
public sealed class SampleTests
{
    [Fact]
    public void ValidateUser()
    {
        var value = new { Name = "John", Age = 42 };
        Snapshot.Validate(value);
    }
}
```

For typed snapshots:

```csharp
Snapshot.Validate(imageBytes, SnapshotType.Png);
Snapshot.Validate(svgText, SnapshotType.Svg);
```

For GIF/ICO frame snapshots (opt-in, emitted as PNG snapshots):

```csharp
var settings = SnapshotSettings.Default with { };
settings.Serializers.AddGifSerializer();
settings.Serializers.AddIcoSerializer();

Snapshot.Validate(gifBytes, SnapshotType.Gif, settings);
Snapshot.Validate(icoBytes, SnapshotType.Ico, settings);
```

## File naming convention

Snapshots are stored in a `__snapshots__` directory next to the test source file:

- expected snapshots: `*.verified.<extension>`
- mismatch output: `*.actual.<extension>`

Example:

- `__snapshots__/SampleTests_ValidateUser.verified.txt`
- `__snapshots__/SampleTests_ValidateUser.actual.txt`

Notes:

- By default, snapshot names include class name and test name to avoid collisions across test classes.
- `.actual` files are always written when a snapshot does not match.
- `.actual` files are deleted once they are no longer relevant: when the snapshot matches again, when the assertion no
  longer produces that snapshot, and after `Overwrite` or `OverwriteWithoutFailure` updated the verified file. A stale
  `.actual` file left by an earlier failed run can therefore never be approved over a correct snapshot.
- If a single assertion serializes multiple files, an index suffix (`_0`, `_1`, ...) is appended.
- If a test calls `Snapshot.Validate` several times, the first call keeps the name and the next ones get an
  ordinal suffix (`~2`, `~3`, ...), see [Several assertions in one test](#several-assertions-in-one-test).
- Only letters, digits, `-`, `_` and `.` are kept in a name. Any other character - a space, a parenthesis,
  a quote, a `/` - is replaced with `_` and a stable hash of the original name is added: removing those
  characters would let two test names - `Case_a/b` and `Case_a?b`, say - claim the same snapshot file, and
  the hash keeps them apart. A hash is also added when the name is too long (more than
  `MaxSnapshotFileNameLength` characters, or more than the 255 UTF-8 bytes most file systems accept) or
  already ends with `.verified` / `.actual`. The hash does not depend on where the repository is checked out.

### Several assertions in one test

Each call to `Snapshot.Validate` in a test gets its own snapshot file:

```csharp
[Fact]
public void Render()
{
    Snapshot.Validate(html);                   // __snapshots__/RenderTests_Render.verified.txt
    Snapshot.Validate(png, SnapshotType.Png);  // __snapshots__/RenderTests_Render~2.verified.png
}
```

The calls are numbered by the line they are made from, in the order they first run, so the calls must run in
a deterministic order. Calls made from the same line - in a loop, or through a helper that does not forward
the caller information - share a name: set `Snapshot.TestContext` to give each of them a distinct name. The
suffix is only added by the built-in naming strategies, as a custom `SnapshotNamingStrategy` may already tell
the calls apart.

### Name collisions

Two tests that get the same snapshot name would overwrite each other's snapshot. When they run in the same
process, the second one fails with a `SnapshotException` that names both tests. This happens, for instance,
with two tests that have the same display name, test cases whose arguments do not override `ToString`,
test contexts that only differ by `Metadata` (it is not part of the name), or names that only differ by case
(`Case_A` and `Case_a` are the same file on Windows and macOS). Give one of the tests a distinct name, for
example by setting `Snapshot.TestContext`. Tests that explicitly set the same `SnapshotTestContext.TestName`
may share a snapshot. Collisions are only detected with the default `SnapshotPathStrategy` and a built-in
naming strategy.

An assertion that produces several files (`Name_0`, `Name_1`) cannot tell whether a `Name_2` file is one it
produced in an earlier run or the snapshot of a test named `Name_2`. Such a file is reported as unexpected
but never deleted automatically.

### Parameterized tests

The name of a parameterized test is the method name followed by the arguments: `SampleTests_Render_1.5_True`.
`null` is written as `null`, the string `"null"` is quoted (so its name gets a hash and differs from the name
of `null`), and arrays and lists are written element by element (`SampleTests_Render_1_2_<hash>` for
`new[] { 1, 2 }`). A custom display name (`DisplayName`, `TestName`) is
used as-is, including its `.` characters.

Earlier versions truncated a display name at its last `.` (`[InlineData(1.5)]` was named `5)_1.5`) and named
arrays after their type. The snapshot files created with those names keep being used as long as they exist
and no other test uses the name; delete them to switch to the new names.

## Storing snapshots in git

Snapshots are often binary: PNG frames from the GIF/ICO serializers, whatever the ImageSharp and
SkiaSharp backends emit, and `.bin` for any value whose extension is unknown. Add an entry to your
`.gitattributes` so git never applies line-ending conversion to them:

```gitattributes
**/__snapshots__/** -text
```

Without it, a repository using the default `core.autocrlf=true` on Windows rewrites the line endings
of every binary snapshot on checkout. The symptom is snapshots that pass for whoever approved them
and fail for everyone else. PNG files fail to decode outright rather than mis-comparing, because the
PNG signature contains a `CR LF` pair specifically to catch this, but the reported error
("Unsupported image format") does not point at the cause.

## Line endings

The default serializer writes `\n` line endings on every platform, so a snapshot produced on Windows is
identical to one produced on Linux or macOS. Use `HumanReadableSerializerOptions.NewLine` to write `\r\n`
instead:

```csharp
settings.ConfigureHumanReadableSerializer(options => options.NewLine = "\r\n");
```

Text snapshots are compared without regard to line endings: `\r\n`, `\r` and `\n` are equivalent, and a leading
UTF-8 byte order mark is ignored. A verified file whose line endings were changed by an editor, a merge tool or git's
`core.autocrlf`, or that an editor saved with a byte order mark, still matches. This applies to every known text format
(`txt`, `svg`, `json`, `yaml`, `xml`, `html`, `md`, `csv`, `cs`, `vb`, `razor`, `sql`, ... and their common variants):
a format without its own comparer uses the comparer registered for `SnapshotType.Default`. Other formats, such as
`png` or `bin`, are compared byte for byte.

## Snapshot naming

You can choose how snapshot names are generated using `SnapshotSettings.SnapshotNamingStrategy`:

- `SnapshotNamingStrategies.TestName`
- `SnapshotNamingStrategies.ClassName_TestName` (default)
- `SnapshotNamingStrategies.FullName`

## Calling `Snapshot.Validate` from a helper method

Wrapping `Snapshot.Validate` in a helper method is supported. The snapshot is still named after the test,
not after the helper: the test method is read from the test framework context (Xunit v3, TUnit, and NUnit)
and, under a framework that exposes no context (Xunit v2, MSTest), by walking the stack until a method
carrying a test attribute (`[Fact]`, `[Theory]`, `[Test]`, `[TestMethod]`) is found.

The snapshot directory, however, comes from `[CallerFilePath]`, which points at the file declaring the
helper. Forward the caller information so the snapshots are created next to the test file:

```csharp
public static class ApiSnapshot
{
    public static void ValidateOpenApiSpec(
        string spec,
        [CallerFilePath] string? filePath = null,
        [CallerLineNumber] int lineNumber = -1)
    {
        var settings = SnapshotSettings.Default with { /* shared configuration */ };
        Snapshot.Validate(spec, "yaml", settings, filePath, lineNumber);
    }
}

public sealed class OpenApiTests
{
    [Fact]
    public void ValidateSpec()
    {
        ApiSnapshot.ValidateOpenApiSpec(GetSpec());
        // => __snapshots__/OpenApiTests_ValidateSpec.verified.yaml
    }
}
```

No custom `SnapshotNamingStrategy` or `SnapshotPathStrategy` is needed for this scenario.

This also works when the helper is `async` and awaits before asserting, as long as the test framework exposes
a context. Under a test framework that exposes no context (Xunit v2, MSTest), the test method must still be on
the call stack, so await inside the helper *after* the call to `Snapshot.Validate` rather than before it.

If the same test calls the helper several times, forward the caller line number as above: each call then gets
its own snapshot (see [Several assertions in one test](#several-assertions-in-one-test)). Otherwise, set
`Snapshot.TestContext` to give each call a distinct name (see [Test context](#test-context)).

## Snapshots stored as source files

Some snapshots are source files (for example the output of a source generator, see [`Meziantou.Framework.SnapshotTesting.Roslyn`](https://www.nuget.org/packages/Meziantou.Framework.SnapshotTesting.Roslyn)),
and many snapshot extensions mean something to the build: `.razor` and `.cshtml` files are compiled, `.resx` files are embedded,
`.xaml` files are compiled as pages, and the Web SDK copies `.json` files to the output and publish directories.
The package ships MSBuild targets that remove every file under `**/__snapshots__/**` from the default `Compile`, `EmbeddedResource`,
`Content`, `Page`, `ApplicationDefinition`, `MauiXaml` and `AvaloniaXaml` items and add them as `None` items, so they still show up
in the IDE but are not part of the build output.

Set `SnapshotTestingExcludeSnapshotFilesFromCompilation` to `false` to opt out:

```xml
<PropertyGroup>
  <SnapshotTestingExcludeSnapshotFilesFromCompilation>false</SnapshotTestingExcludeSnapshotFilesFromCompilation>
</PropertyGroup>
```

## Approving snapshots

To approve generated `*.actual.*` files, you can use the dedicated tool package:

```bash
dotnet tool install --global Meziantou.Framework.SnapshotTesting.Tool
Meziantou.Framework.SnapshotTesting.Tool approve
```

Use `--interactive` to approve or reject snapshots one by one.

## Continuous integration and LLM environments

Snapshots are never updated on a continuous integration server, in a continuous testing runner (NCrunch, ReSharper), or
when the tests are run by an LLM agent (Claude Code, Codex, GitHub Copilot, Cursor, ...), even when the update strategy
is `Overwrite` or is set using `SNAPSHOTTESTING_STRATEGY`. The failure message says which environment was detected.

- A continuous integration server is detected by the variables the major build servers set: `CI` set to `true`, `1` or `yes`
  (`CI=false` is not a build server), `TF_BUILD`, `GITHUB_ACTION`, `GITLAB_CI`, `JENKINS_URL`, `TEAMCITY_VERSION`, etc.
- LLM agents are detected by [`Meziantou.Framework.LLMContext`](https://www.nuget.org/packages/Meziantou.Framework.LLMContext).

To allow updates in such an environment, set the `SNAPSHOTTESTING_AUTODETECT_CONTINUOUS_ENVIRONMENT` environment variable
to `false` (or `0`, `no`, `off`), or set `AutoDetectContinuousEnvironment` to `false`:

```csharp
var settings = SnapshotSettings.Default with
{
    AutoDetectContinuousEnvironment = false,
};
```

## Snapshot types

`SnapshotType` controls extension and optional metadata (`MimeType`, `DisplayName`). This can also affect the serializer.

## Test context

Snapshot naming uses test context when available:

- `Snapshot.TestContext` (`AsyncLocal<SnapshotTestContext?>`) can be set explicitly.
- Xunit v3, TUnit, and NUnit display names are auto-detected to improve generated file names.
- The test class and method names are auto-detected from the same frameworks and are used as-is. The call
  stack is only walked for a name the context does not provide: under Xunit v2, MSTest, or no test framework,
  or when `Snapshot.TestContext` is set to a context that carries no `ClassName` or `MethodName`.
- A test declared in a base class is named after the class it ran in, as reported by the test framework.

## Customization

Use `SnapshotSettings` to customize behavior:

- `Serializers` (`SnapshotSerializerCollection`)
- `Comparers` (`SnapshotComparerCollection`)
- `SnapshotUpdateStrategy` (`Disallow`, `Overwrite`, `OverwriteWithoutFailure`, `MergeTool`, `MergeToolSync`)
- `SnapshotPathStrategy` for full path generation

A custom `SnapshotNamingStrategy` or `SnapshotPathStrategy` receives a `SnapshotPathContext`. Its `ClassName`
and `MethodName` come from the test framework context when it exposes them, and from the call stack otherwise.
The walk is the most expensive part of an assertion and only runs when a strategy reads a name the context did
not provide: a strategy that uses neither name never pays for it. Use `MemberName` when the name of the method
that called the assertion - captured by the compiler, always available - is enough.

You can also set the default strategy using the `SNAPSHOTTESTING_STRATEGY` environment variable.
The value is case-insensitive and must match one of the `SnapshotUpdateStrategy` static property names (for example: `DISALLOW`, `MergeTool`, `overwritewithoutfailure`).

```csharp
var settings = SnapshotSettings.Default with
{
    SnapshotUpdateStrategy = SnapshotUpdateStrategy.Disallow,
};

Snapshot.Validate(value, SnapshotType.Default, settings);
```

The default serializers handle human-readable objects, `byte[]`, and `Stream`.
GIF frame extraction is opt-in via `Serializers.AddGifSerializer()`: when enabled and `SnapshotType.Gif` is used with a valid GIF `byte[]`, each frame is serialized as a separate `.png` snapshot.
ICO image extraction is opt-in via `Serializers.AddIcoSerializer()`: when enabled and `SnapshotType.Ico` is used with a valid ICO `byte[]`, each icon image is serialized as a separate `.png` snapshot.
BMP/PNG/JPEG/TIFF image comparison is opt-in via `Comparers.AddImageComparer()`. When enabled, `SnapshotType.Bmp`, `SnapshotType.Png`, `SnapshotType.Jpeg` (including `.jpg` aliases), and `SnapshotType.Tiff` (including `.tif` aliases) snapshots are compared by decoded pixel content (ARGB), so format metadata differences do not trigger snapshot mismatches. Fully transparent pixels are equal whatever color they hide. A snapshot that cannot be decoded does not match.
To allow small visual differences, configure the image comparer with an SSIM threshold or a maximum 64-bit dHash/pHash distance. When multiple thresholds are configured, all comparisons must pass. Every comparison requires images with identical dimensions.

- `SimilarityThreshold` is compared to the mean SSIM over every 7×7 window of the images (uniform weights, sample covariance, `K1 = 0.01`, `K2 = 0.03`), the value scikit-image's `structural_similarity(expected, actual, data_range=255, channel_axis=-1)` computes with its default parameters. The R, G, and B channels are premultiplied by the alpha channel and averaged, and the score is the lower of that value and the SSIM of the alpha channel, so opacity differences are detected. Because the score is a mean, a localized difference lowers it in proportion to the area it covers: a difference confined to 1% of the image lowers it by about 0.01 at most, so use a threshold close to 1 to catch small regressions.
- `DHashThreshold` and `PHashThreshold` are compared to the Hamming distance between the hashes of the images, plus the difference between their mean luminances on the same 0-64 scale (about 4 luminance levels per unit), capped at 64. The hashes are computed from area-averaged thumbnails, so every pixel contributes. Images that are not fully opaque are compared composited over both a black and a white background, and the larger distance is used.

```csharp
var settings = SnapshotSettings.Default with { };
settings.Comparers.AddImageComparer(new ImageComparisonSettings
{
    SimilarityThreshold = 0.95f,
    DHashThreshold = 5,
    PHashThreshold = 5,
});
```

## Scrubbing

Scrubbing helps make snapshots deterministic by removing unstable values or lines. Scrubbers are applied to text
snapshots only: snapshots stored in a binary format (`png`, `bmp`, `jpeg`, `bin`, ...) are left untouched.

```csharp
var settings = SnapshotSettings.Default with { };
settings.ConfigureHumanReadableSerializer(options => options.ScrubGuid());
settings.ScrubLinesContaining("GeneratedAt:");

Snapshot.Validate(value, SnapshotType.Default, settings);
```

You can also scrub relative temporal values:

```csharp
var now = DateTime.UtcNow;
var settings = SnapshotSettings.Default with { };
settings.ConfigureHumanReadableSerializer(options => options.UseRelativeDateTime(now));
```

## Invisible characters

When spaces, tabs, or line endings matter, a snapshot that looks correct can still hide a difference.
Set `ShowInvisibleCharactersInValues` to write them as Unicode control pictures (`␠` for a space, `␉` for a
tab, `␍` and `␊` for line endings, `␀` for U+0000, and so on):

```csharp
var settings = SnapshotSettings.Default with { };
settings.ConfigureHumanReadableSerializer(options => options.ShowInvisibleCharactersInValues = true);

Snapshot.Validate("line 1\r\nline\t2", settings);
```

The verified snapshot contains:

```text
line␠1␍␊
line␉2
```

Notes:

- The control pictures of a line ending are followed by a real line break, written with `NewLine` (`\n` by default,
  see [Line endings](#line-endings)), so each value keeps its lines.
- Only multi-line values and property names are converted. A single-line value such as `"a b\tc"` is written as-is.
- `ConfigureHumanReadableSerializer` only affects the settings instance it is called on. Use
  `SnapshotSettings.Default with { }` to get a copy, or call it on `SnapshotSettings.Default` to enable the option
  for every snapshot.

## Merge tools

With `SnapshotUpdateStrategy.MergeTool` or `SnapshotUpdateStrategy.MergeToolSync`, a merge tool is started to compare each verified file with its actual file. The tools listed in `SnapshotSettings.MergeTools` are tried in order. By default:

- The tool named by the `DiffEngine_Tool` environment variable
- The merge tool from the git configuration (`merge.tool` and `mergetool.<tool>.cmd`)
- The diff tool from the git configuration (`diff.tool` and `difftool.<tool>.cmd`)
- The current IDE (Visual Studio, VS Code, Rider). This relies on inspecting the ancestor processes, which is only supported on Windows.

Merge tools are not started when the `DiffEngine_Disabled` environment variable is `true` or `1`, nor, while `AutoDetectContinuousEnvironment` is enabled, on a build server, under a continuous test runner (Visual Studio Live Unit Testing, NCrunch background runs), or in an LLM agent. The snapshot difference is then reported as a regular assertion failure. When merge tools are enabled but none of them can be started, the assertion fails with the paths to compare and the reason each tool could not start. A tool that fails to start does not prevent the next ones from being tried.

`DiffEngine_Tool` is the case-insensitive name of a `MergeTool` property, for example `VisualStudioCode` or `rider`.

The `cmd` of a git `mergetool.<tool>` or `difftool.<tool>` is run like git runs it: verbatim, through `sh -c`, with the `LOCAL`, `REMOTE`, `BASE` and `MERGED` environment variables set, so `"$LOCAL"`, `${REMOTE}`, `~` or `&&` work as they do with git, and paths may contain spaces or quotes. On Windows, the `sh.exe` of Git for Windows is used. When it cannot be found, each placeholder is replaced by its value, quoted as a single argument. For a merge tool, `LOCAL` and `BASE` are a temporary copy of the verified file as it was before the merge, `REMOTE` is the actual file, and `MERGED` is the verified file the tool writes the result to. For a diff tool, `LOCAL`, `MERGED` and `BASE` are the verified file and `REMOTE` is the actual file. A git tool command must not return before the merge is done (for VS Code, use `code --wait`): the temporary copy is deleted as soon as the command exits.

`SnapshotUpdateStrategy.MergeToolSync` waits for the merge tool process to exit. Visual Studio Code and Cursor are started with `--wait` so they block until the diff is closed. Other launchers, such as the ones of Rider, Visual Studio or Kaleidoscope, may hand the files over to a running instance and exit immediately, so the test continues while the diff is still open. In that case, an empty verified file created for the merge is only removed when the test process exits, if it is still empty. The verified files that the assertion no longer produces are never deleted by the merge tool strategies; the assertion failure lists them. With `MergeToolSync`, the actual file is deleted once the merge tool saved it as the verified file.
