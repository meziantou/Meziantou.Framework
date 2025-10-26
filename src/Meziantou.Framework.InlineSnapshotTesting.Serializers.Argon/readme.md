# Meziantou.Framework.InlineSnapshotTesting.Serializers.Argon

An Argon-based serializer for [Meziantou.Framework.InlineSnapshotTesting](https://www.nuget.org/packages/Meziantou.Framework.InlineSnapshotTesting) that provides compatibility with [Verify](https://github.com/VerifyTests/Verify)'s serialization format.

## Usage

This package provides an `ArgonSnapshotSerializer` that uses the [Argon](https://github.com/SimonCropp/Argon) JSON serializer. This is useful when migrating from Verify or when you want to use the same serialization format as Verify.

```csharp
using Meziantou.Framework.InlineSnapshotTesting;
using Meziantou.Framework.InlineSnapshotTesting.Serialization;

// Use Argon serializer for a single validation
InlineSnapshot
    .WithSerializer(new ArgonSnapshotSerializer())
    .Validate(data, """
        {
          Property1: value1,
          Property2: value2
        }
        """);
```

### Setting as Default Serializer

You can configure the Argon serializer as the default serializer for all tests using a module initializer:

```csharp
using System.Runtime.CompilerServices;
using Meziantou.Framework.InlineSnapshotTesting;
using Meziantou.Framework.InlineSnapshotTesting.Serialization;

static class AssemblyInitializer
{
    [ModuleInitializer]
    public static void Initialize()
    {
        InlineSnapshotSettings.Default = InlineSnapshotSettings.Default with
        {
            SnapshotSerializer = new ArgonSnapshotSerializer(),
        };
    }
}
```

## When to Use

Use `ArgonSnapshotSerializer` when:

- **Migrating from Verify**: You want to maintain the same snapshot format when migrating from Verify to InlineSnapshotTesting
- **Cross-compatibility**: You need snapshots that are compatible with Verify's format
- **JSON preference**: You prefer JSON-style output over the default HumanReadableSerializer format

For most new projects, the default [`HumanReadableSerializer`](https://www.nuget.org/packages/Meziantou.Framework.HumanReadableSerializer) is recommended as it provides better readability and more configuration options.

## Additional Resources

- [InlineSnapshotTesting documentation](https://github.com/meziantou/Meziantou.Framework/blob/main/src/Meziantou.Framework.InlineSnapshotTesting/readme.md)
- [Argon JSON serializer](https://github.com/SimonCropp/Argon)
- [Verify snapshot testing](https://github.com/VerifyTests/Verify)
