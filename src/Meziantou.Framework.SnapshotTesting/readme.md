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
