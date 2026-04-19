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

## File naming convention

Snapshots are stored in a `__snapshots__` directory next to the test source file:

- expected snapshots: `*.verified.<extension>`
- mismatch output: `*.actual.<extension>`

Example:

- `__snapshots__/SampleTest.verified.txt`
- `__snapshots__/SampleTest.actual.txt`

When names are long or use reserved suffixes (`.verified` / `.actual`), a hash is added to keep file names deterministic and bounded.

## Snapshot types

`SnapshotType` controls the extension and can carry optional metadata (`MimeType`, `DisplayName`):

```csharp
Snapshot.Validate(SnapshotType.Png, imageBytes);
```

Built-in types include:

- `SnapshotType.Default` (`txt`, `text/plain`)
- `SnapshotType.Png` (`png`, `image/png`)
- `SnapshotType.Svg` (`svg`, `image/svg+xml`)

## Customization

Use `SnapshotSettings` to customize behavior:

- serializer and comparer per `SnapshotType`
- update strategy
- assertion message and exception type
- `SnapshotPathStrategy` to control full snapshot path generation

The default serializer writes human-readable text for most objects. For `byte[]` and `Stream`, it writes raw bytes directly.
