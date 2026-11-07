# Meziantou.Framework.Diagnostics.ContextSnapshot

Get the current execution context for diagnostic purposes (`JsonSerializer` and `JsonStringEnumConverter` are in the
`System.Text.Json` and `System.Text.Json.Serialization` namespaces).

<!-- ref:../../tests/Meziantou.Framework.Diagnostics.ContextSnapshot.Tests/SnapshotTests.cs;region=ContextSnapshot_Serialize;format=md-fenced-code-block;dedent=true -->
```csharp
var snapshot = new ContextSnapshotBuilder().AddDefault().BuildSnapshot();

// You can serialize the context
var json = JsonSerializer.Serialize(snapshot, new JsonSerializerOptions
{
    RespectNullableAnnotations = true,
    RespectRequiredConstructorParameters = true,
    WriteIndented = true,
    Converters =
    {
        new JsonStringEnumConverter(),
    },
});
```
<!-- endref -->
