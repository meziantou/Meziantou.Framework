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

## File naming convention

Snapshots are stored in a `__snapshots__` directory next to the test source file:

- expected snapshots: `*.verified.<extension>`
- mismatch output: `*.actual.<extension>`

Example:

- `__snapshots__/SampleTest.verified.txt`
- `__snapshots__/SampleTest.actual.txt`

Notes:

- `.actual` files are always written when a snapshot does not match.
- If a single assertion serializes multiple files, an index suffix (`_0`, `_1`, ...) is appended.
- If names are too long (or already end with `.verified` / `.actual`), a stable hash is added.

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
