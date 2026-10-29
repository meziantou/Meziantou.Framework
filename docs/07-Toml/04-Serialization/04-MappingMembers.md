---
title: Mapping members
description: Rename, ignore, include and require members, choose the constructor, fill existing values, run callbacks, and keep unknown keys.
---

Attributes on your types change how their members map to keys. They are in the `Meziantou.Framework.Toml.Serialization`
namespace.

## Rename, ignore and require members

<!-- ref:../../../tests/Meziantou.Framework.Toml.Tests/BasicTests.cs;region=TomlAttributes_Usage;format=md-fenced-code-block;dedent=true -->
```csharp
public sealed class DatabaseConfig
{
    [TomlRequired]
    public string Host { get; set; } = "";

    [TomlPropertyName("port_number")]
    public int Port { get; set; }

    [TomlIgnore]
    public string ConnectionString => $"{Host}:{Port}";
}

public sealed class Endpoint
{
    [TomlConstructor]
    public Endpoint(string host, int port)
    {
        Host = host;
        Port = port;
    }

    public string Host { get; }
    public int Port { get; }
}
```
<!-- endref -->

<!-- ref:../../../tests/Meziantou.Framework.Toml.Tests/BasicTests.cs;region=TomlAttributes_Read;format=md-fenced-code-block;dedent=true -->
```csharp
var database = TomlSerializer.Deserialize<DatabaseConfig>("Host = \"localhost\"\nport_number = 5432");
var endpoint = TomlSerializer.Deserialize<Endpoint>("Host = \"localhost\"\nPort = 80");

Assert.Equal("localhost:5432", database!.ConnectionString);
Assert.Equal("Host = \"localhost\"\nport_number = 5432\n", TomlSerializer.Serialize(database));
Assert.Throws<TomlException>(() => TomlSerializer.Deserialize<DatabaseConfig>("port_number = 5432"));
Assert.Equal(80, endpoint!.Port);
```
<!-- endref -->

| Attribute | Effect |
| --- | --- |
| `[TomlPropertyName]` | Sets the key of the member. It wins over the naming policy. |
| `[TomlIgnore]` | Ignores the member, always or only when its value is `null` or default (`Condition = TomlIgnoreCondition.WhenWritingNull` or `WhenWritingDefault`). |
| `[TomlInclude]` | Includes a non-public member, or a field. |
| `[TomlPropertyOrder]` | Sets the position of the member in its table. |
| `[TomlRequired]` | The key must be present: a missing key throws a `TomlException`. |

The C# `required` modifier makes a member required too, unless the constructor used has `[SetsRequiredMembers]`.

## Choose the constructor

A type without a parameterless constructor, such as a positional record, is created with its constructor: each
parameter receives the value of the key with the same name. When a type has several constructors, mark the one to use
with `[TomlConstructor]`, as `Endpoint` does above.

A constructor parameter without a default value must be present in the document, unless
`RespectRequiredConstructorParameters` is `false`: it then receives the default value of its type.

## Fill existing values

By default, reading assigns new values to the members that have a setter, and leaves read-only members untouched
(`TomlObjectCreationHandling.Replace`). `Populate` reuses the objects and collections the members already hold, and
appends to collections instead of clearing them. Set it on a type, on a member with `[TomlObjectCreationHandling]`, or
for every type with `PreferredObjectCreationHandling`:

<!-- ref:../../../tests/Meziantou.Framework.Toml.Tests/BasicTests.cs;region=TomlObjectCreationHandling_Populate;format=md-fenced-code-block;dedent=true -->
```csharp
[TomlObjectCreationHandling(TomlObjectCreationHandling.Populate)]
public sealed class ReleaserConfiguration
{
    public List<string> Channels { get; } = ["stable"];
}
```
<!-- endref -->

<!-- ref:../../../tests/Meziantou.Framework.Toml.Tests/BasicTests.cs;region=TomlObjectCreationHandling_PopulateRead;format=md-fenced-code-block;dedent=true -->
```csharp
var configuration = TomlSerializer.Deserialize<ReleaserConfiguration>("Channels = [\"beta\"]");

Assert.Equal(["stable", "beta"], configuration!.Channels);
```
<!-- endref -->

- `Populate` on a member that cannot be populated, such as a struct without a setter, throws. On a type or in the
  options, it is a preference: such members are left unchanged.
- `Populate` does not apply to types created with a constructor that has parameters.
- A member with a converter is always replaced.

## Accept a single value or an array

A collection member requires an array. With `[TomlSingleOrArray]`, it also accepts a single value:

<!-- ref:../../../tests/Meziantou.Framework.Toml.Tests/BasicTests.cs;region=TomlSingleOrArray_Usage;format=md-fenced-code-block;dedent=true -->
```csharp
public sealed class PackagingConfiguration
{
    [TomlSingleOrArray]
    [TomlPropertyName("rid")]
    public List<string> RuntimeIdentifiers { get; } = [];
}
```
<!-- endref -->

<!-- ref:../../../tests/Meziantou.Framework.Toml.Tests/BasicTests.cs;region=TomlSingleOrArray_Read;format=md-fenced-code-block;dedent=true -->
```csharp
var single = TomlSerializer.Deserialize<PackagingConfiguration>("rid = \"win-x64\"");
var array = TomlSerializer.Deserialize<PackagingConfiguration>("rid = [\"win-x64\", \"linux-x64\"]");

Assert.Equal(["win-x64"], single!.RuntimeIdentifiers);
Assert.Equal(["win-x64", "linux-x64"], array!.RuntimeIdentifiers);
```
<!-- endref -->

## Run code before or after serialization

Implement the callback interfaces to validate or prepare an object:

| Interface | Called |
| --- | --- |
| `ITomlOnSerializing` | Before the object is written. |
| `ITomlOnSerialized` | After the object is written. |
| `ITomlOnDeserializing` | When the instance is created, before its members are set. Constructor arguments are already set. |
| `ITomlOnDeserialized` | After the members are read. |

<!-- ref:../../../tests/Meziantou.Framework.Toml.Tests/BasicTests.cs;region=TomlLifecycleCallbacks_Usage;format=md-fenced-code-block;dedent=true -->
```csharp
public sealed class ValidatedConfig : ITomlOnDeserialized
{
    public int Port { get; set; } = 8080;

    public void OnTomlDeserialized()
    {
        if (Port is < 1 or > 65535)
            throw new InvalidOperationException($"Invalid port: {Port}");
    }
}
```
<!-- endref -->

<!-- ref:../../../tests/Meziantou.Framework.Toml.Tests/BasicTests.cs;region=TomlLifecycleCallbacks_Read;format=md-fenced-code-block;dedent=true -->
```csharp
Assert.Equal(8080, TomlSerializer.Deserialize<ValidatedConfig>("")!.Port);
Assert.Throws<InvalidOperationException>(() => TomlSerializer.Deserialize<ValidatedConfig>("Port = 0"));
```
<!-- endref -->

An exception thrown by a callback or a setter is not wrapped: it reaches your code as it is, and `TryDeserialize` does
not catch it. An exception thrown by a constructor is reported as a `TomlException`.

## Keep unknown keys

`[TomlExtensionData]` collects the keys that match no member. The member is a dictionary with `string` keys, such as
`IDictionary<string, object?>` or `TomlTable`:

<!-- ref:../../../tests/Meziantou.Framework.Toml.Tests/BasicTests.cs;region=TomlExtensionData_Usage;format=md-fenced-code-block;dedent=true -->
```csharp
public sealed class ExtensibleConfig
{
    public string Name { get; set; } = "";

    [TomlExtensionData]
    public IDictionary<string, object?>? Extra { get; set; }
}
```
<!-- endref -->

<!-- ref:../../../tests/Meziantou.Framework.Toml.Tests/BasicTests.cs;region=TomlExtensionData_Read;format=md-fenced-code-block;dedent=true -->
```csharp
var config = TomlSerializer.Deserialize<ExtensibleConfig>("Name = \"app\"\nversion = 2");

Assert.Equal("app", config!.Name);
Assert.Equal(2L, config.Extra!["version"]);
```
<!-- endref -->

The values have the types of the [document model](../07-DocumentModel.md): `string`, `long`, `double`, `bool`,
`TomlDateTime`, `TomlTable`, `TomlArray` and
`TomlTableArray`.

## Reject unknown keys

Unknown keys are skipped by default. To reject them, set `UnmappedMemberHandling = TomlUnmappedMemberHandling.Disallow`
in the options, or `[TomlUnmappedMemberHandling(TomlUnmappedMemberHandling.Disallow)]` on a type. A typo in a
configuration file then fails instead of being ignored. Extension data still collects the unknown keys.
