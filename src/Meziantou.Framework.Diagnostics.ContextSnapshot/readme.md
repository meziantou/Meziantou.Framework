# Meziantou.Framework.Diagnostics.ContextSnapshot

Get the current execution context for diagnostic purposes.

```c#
var context = new ContextSnapshotBuilder().AddDefault().Build();

// You can serialize the context
JsonSerializer.Serialize(snapshot, new JsonSerializerOptions
{
    WriteIndented = true,
    Converters =
    {
        new JsonStringEnumConverter(),
    },
});
```
