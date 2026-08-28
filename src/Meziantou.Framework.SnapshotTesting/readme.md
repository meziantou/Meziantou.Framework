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
- If a single assertion serializes multiple files, an index suffix (`_0`, `_1`, ...) is appended.
- If names are too long (or already end with `.verified` / `.actual`), a stable hash is added.

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

## Snapshot naming

You can choose how snapshot names are generated using `SnapshotSettings.SnapshotNamingStrategy`:

- `SnapshotNamingStrategies.TestName`
- `SnapshotNamingStrategies.ClassName_TestName` (default)
- `SnapshotNamingStrategies.FullName`

## Snapshots stored as source files

Some snapshots are source files (for example the output of a source generator, see [`Meziantou.Framework.SnapshotTesting.Roslyn`](https://www.nuget.org/packages/Meziantou.Framework.SnapshotTesting.Roslyn)).
The package ships MSBuild targets that remove `**/__snapshots__/**/*.cs` and `**/__snapshots__/**/*.vb` from the `Compile` items and add them as `None` items, so they still show up in the IDE but are not compiled with the test project.

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

## Snapshot types

`SnapshotType` controls extension and optional metadata (`MimeType`, `DisplayName`). This can also affect the serializer.

## Test context

Snapshot naming uses test context when available:

- `Snapshot.TestContext` (`AsyncLocal<SnapshotTestContext?>`) can be set explicitly.
- Xunit v3, TUnit, and NUnit display names are auto-detected to improve generated file names.

## Customization

Use `SnapshotSettings` to customize behavior:

- `Serializers` (`SnapshotSerializerCollection`)
- `Comparers` (`SnapshotComparerCollection`)
- `SnapshotUpdateStrategy` (`Disallow`, `Overwrite`, `OverwriteWithoutFailure`, `MergeTool`, `MergeToolSync`)
- `AssertionExceptionCreator` and `ErrorMessageFormatter`
- `SnapshotPathStrategy` for full path generation

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
BMP/PNG/JPEG/TIFF image comparison is opt-in via `Comparers.AddImageComparer()`. When enabled, `SnapshotType.Bmp`, `SnapshotType.Png`, `SnapshotType.Jpeg` (including `.jpg` aliases), and `SnapshotType.Tiff` (including `.tif` aliases) snapshots are compared by decoded pixel content (ARGB), so format metadata differences do not trigger snapshot mismatches.
To allow small visual differences, configure the image comparer with an SSIM threshold or a maximum 64-bit dHash/pHash Hamming distance. When multiple thresholds are configured, all comparisons must pass. dHash and pHash comparisons allow images with different dimensions, while exact and SSIM comparisons require identical dimensions.

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

Scrubbing helps make snapshots deterministic by removing unstable values or lines.

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
