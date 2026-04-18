# Meziantou.Framework.SnapshotTesting

`Meziantou.Framework.SnapshotTesting` validates serialized values against snapshot files stored on disk.

By default, snapshots are written in a `__snapshots__` folder next to the test source file. Expected files use the `.verified.<extension>` suffix (for example `SampleTest.verified.txt`), while mismatches are written as `.actual.<extension>` files next to them. A short hash is appended when the generated name is long or uses reserved suffixes.

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

You can configure serializers, comparers, and snapshot path strategy (`SnapshotPathStrategy`) with `SnapshotSettings`.

You can also select a specific snapshot type explicitly:

```csharp
Snapshot.Validate(SnapshotType.Png, imageBytes);
```

When a type is provided, its value is used as the snapshot file extension.
The default serializer treats `byte[]` and `Stream` values as binary data and writes the bytes directly (without converting them to text).

When an existing snapshot differs from the new value, a `.actual.<extension>` file is written next to the expected snapshot with the new content.
