# Meziantou.Framework.SnapshotTesting

`Meziantou.Framework.SnapshotTesting` validates serialized values against snapshot files stored on disk.

By default, snapshots are written in a `__snapshots__` folder next to the test source file. File names are deterministic and bounded using `<start>_<hash>_<index>.<extension>`.

```csharp
public class SampleTests
{
    [Fact]
    public void Test1()
    {
        var data = new { Name = "John" };
        Snapshot.Validate(data);
    }
}
```

You can configure serializers, comparers, path and naming strategies with `SnapshotSettings`.

You can also select a specific snapshot type explicitly:

```csharp
Snapshot.Validate(SnapshotType.Png, imageBytes);
```

When a type is provided, its value is used as the snapshot file extension.
The default serializer treats `byte[]` and `Stream` values as binary data and writes the bytes directly (without converting them to text).

When an existing snapshot differs from the new value, a `.received.<extension>` file is written next to the expected snapshot with the new content.
