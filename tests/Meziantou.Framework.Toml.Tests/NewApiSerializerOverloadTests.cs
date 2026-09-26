using System.IO;
using System.Text;
using Meziantou.Framework.Toml.Serialization;

namespace Meziantou.Framework.Toml.Tests;

public sealed class NewApiSerializerOverloadTests
{
    private const string SampleToml = """
        name = "Ada"
        age = 37
        """;

    [Fact]
    public void Deserialize_String_WithContext_Works()
    {
        var context = TestTomlSerializerContext.Default;
        var person = TomlSerializer.Deserialize<GeneratedPerson>(SampleToml, context);

        Assert.NotNull(person);
        Assert.Equal("Ada", person!.Name);
        Assert.Equal(37, person.Age);
    }

    [Fact]
    public void Deserialize_String_Type_WithContext_Works()
    {
        var context = TestTomlSerializerContext.Default;
        var result = TomlSerializer.Deserialize(SampleToml, typeof(GeneratedPerson), context);

        Assert.NotNull(result);
        Assert.IsAssignableTo<GeneratedPerson>(result);
        var person = (GeneratedPerson)result!;
        Assert.Equal("Ada", person.Name);
        Assert.Equal(37, person.Age);
    }

    [Fact]
    public void Deserialize_Stream_WithContext_Works()
    {
        var context = TestTomlSerializerContext.Default;
        using var stream = new MemoryStream();
        using (var writer = new StreamWriter(stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false), bufferSize: 1024, leaveOpen: true))
        {
            writer.Write(SampleToml);
        }

        stream.Position = 0;
        var person = TomlSerializer.Deserialize<GeneratedPerson>(stream, context);

        Assert.NotNull(person);
        Assert.Equal("Ada", person!.Name);
        Assert.Equal(37, person.Age);
    }

    [Fact]
    public void Deserialize_TextReader_WithTypeInfo_Works()
    {
        var context = TestTomlSerializerContext.Default;
        using var reader = new StringReader(SampleToml);
        var person = TomlSerializer.Deserialize(reader, context.GeneratedPerson);

        Assert.NotNull(person);
        Assert.Equal("Ada", person!.Name);
        Assert.Equal(37, person.Age);
    }

    [Fact]
    public void Deserialize_TextReader_Type_WithContext_Works()
    {
        var context = TestTomlSerializerContext.Default;
        using var reader = new StringReader(SampleToml);
        var result = TomlSerializer.Deserialize(reader, typeof(GeneratedPerson), context);

        Assert.NotNull(result);
        Assert.IsAssignableTo<GeneratedPerson>(result);
    }

    [Fact]
    public void Deserialize_Stream_WithTypeInfo_Works()
    {
        var context = TestTomlSerializerContext.Default;
        using var stream = new MemoryStream();
        using (var writer = new StreamWriter(stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false), bufferSize: 1024, leaveOpen: true))
        {
            writer.Write(SampleToml);
        }

        stream.Position = 0;
        var person = TomlSerializer.Deserialize(stream, context.GeneratedPerson);

        Assert.NotNull(person);
        Assert.Equal("Ada", person!.Name);
        Assert.Equal(37, person.Age);
    }

    [Fact]
    public void Serialize_TextWriter_WithContext_Works()
    {
        var context = TestTomlSerializerContext.Default;
        using var writer = new StringWriter();
        TomlSerializer.Serialize(writer, new GeneratedPerson { Name = "Ada", Age = 37 }, context);
        var toml = writer.ToString();

        var roundtrip = TomlSerializer.Deserialize(toml, context.GeneratedPerson);
        Assert.NotNull(roundtrip);
        Assert.Equal("Ada", roundtrip!.Name);
        Assert.Equal(37, roundtrip.Age);
    }

    [Fact]
    public void Serialize_Stream_WithContext_Works()
    {
        var context = TestTomlSerializerContext.Default;
        using var stream = new MemoryStream();
        TomlSerializer.Serialize(stream, new GeneratedPerson { Name = "Ada", Age = 37 }, context);
        stream.Position = 0;

        var roundtrip = TomlSerializer.Deserialize(stream, context.GeneratedPerson);
        Assert.NotNull(roundtrip);
        Assert.Equal("Ada", roundtrip!.Name);
        Assert.Equal(37, roundtrip.Age);
    }

    [Fact]
    public void TryDeserialize_String_WithContext_ReturnsFalseOnFailure()
    {
        var context = TestTomlSerializerContext.Default;
        var invalid = "name = \"Ada\"\nage = \"not-a-number\"\n";

        var ok = TomlSerializer.TryDeserialize<GeneratedPerson>(invalid, context, out var value);

        Assert.False(ok);
        Assert.Null(value);
    }

    [Fact]
    public void TryDeserialize_Stream_WithContext_ReturnsFalseOnFailure()
    {
        var context = TestTomlSerializerContext.Default;
        using var stream = new MemoryStream();
        using (var writer = new StreamWriter(stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false), bufferSize: 1024, leaveOpen: true))
        {
            writer.Write("name = \"Ada\"\nage = \"not-a-number\"\n");
        }

        stream.Position = 0;
        var ok = TomlSerializer.TryDeserialize<GeneratedPerson>(stream, context, out var value);

        Assert.False(ok);
        Assert.Null(value);
    }

    [Fact]
    public void TryDeserialize_TextReader_Type_WithContext_ReturnsFalseOnFailure()
    {
        var context = TestTomlSerializerContext.Default;
        using var reader = new StringReader("name = \"Ada\"\nage = \"not-a-number\"\n");

        var ok = TomlSerializer.TryDeserialize(reader, typeof(GeneratedPerson), context, out var value);

        Assert.False(ok);
        Assert.Null(value);
    }

    [Fact]
    public void TryDeserialize_Stream_Type_WithContext_ReturnsFalseOnFailure()
    {
        var context = TestTomlSerializerContext.Default;
        using var stream = new MemoryStream();
        using (var writer = new StreamWriter(stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false), bufferSize: 1024, leaveOpen: true))
        {
            writer.Write("name = \"Ada\"\nage = \"not-a-number\"\n");
        }

        stream.Position = 0;

        var ok = TomlSerializer.TryDeserialize(stream, typeof(GeneratedPerson), context, out var value);

        Assert.False(ok);
        Assert.Null(value);
    }
}
