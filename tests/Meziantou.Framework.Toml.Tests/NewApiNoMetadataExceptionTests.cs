using System;
using Meziantou.Framework.Toml.Serialization;

namespace Meziantou.Framework.Toml.Tests;

public sealed class NewApiNoMetadataExceptionTests
{
    private sealed class EmptyContext : TomlSerializerContext
    {
        public override TomlTypeInfo? GetTypeInfo(Type type, TomlSerializerOptions options) => null;
    }

    [Fact]
    public void Serialize_WithMissingGeneratedMetadata_ThrowsTomlException()
    {
        var context = new EmptyContext();
        Assert.Throws<TomlException>(() => TomlSerializer.Serialize(123, context));
    }
}

