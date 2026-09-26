using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
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

    [Theory]
    [InlineData("name = \"Ada\"\nage = 37\n", true)]
    [InlineData("name = \"Ada\"\nage = \"not-a-number\"\n", false)]
    public void TryDeserialize_WithTypeInfo_ReportsSuccess(string toml, bool expected)
    {
        var typeInfo = TestTomlSerializerContext.Default.GeneratedPerson;
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(toml));
        using var genericStream = new MemoryStream(Encoding.UTF8.GetBytes(toml));

        Assert.Equal(expected, TomlSerializer.TryDeserialize(toml, typeInfo, out var fromString));
        Assert.Equal(expected, TomlSerializer.TryDeserialize(new StringReader(toml), typeInfo, out var fromReader));
        Assert.Equal(expected, TomlSerializer.TryDeserialize(genericStream, typeInfo, out var fromStream));
        Assert.Equal(expected, TomlSerializer.TryDeserialize(toml, (TomlTypeInfo)typeInfo, out var untypedFromString));
        Assert.Equal(expected, TomlSerializer.TryDeserialize(new StringReader(toml), (TomlTypeInfo)typeInfo, out var untypedFromReader));
        Assert.Equal(expected, TomlSerializer.TryDeserialize(stream, (TomlTypeInfo)typeInfo, out var untypedFromStream));
        foreach (var value in new object?[] { fromString, fromReader, fromStream, untypedFromString, untypedFromReader, untypedFromStream })
        {
            Assert.Equal(expected ? 37 : null, (value as GeneratedPerson)?.Age);
        }
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
    public void Serialize_ValueOfAnotherType_ThrowsArgumentException()
    {
        var person = new GeneratedPerson { Name = "Ada" };

        Assert.Throws<ArgumentException>(() => TomlSerializer.Serialize(person, typeof(string)));
        Assert.Throws<ArgumentException>(() => TomlSerializer.Serialize("text", typeof(GeneratedPerson), TestTomlSerializerContext.Default));
        Assert.Throws<ArgumentException>(() => TomlSerializer.Serialize(42, (TomlTypeInfo)TestTomlSerializerContext.Default.GeneratedPerson));
        Assert.Equal("value = 1\n", TomlSerializer.Serialize(1, typeof(int?), new TomlSerializerOptions { RootValueHandling = TomlRootValueHandling.WrapInRootKey }).ReplaceLineEndings("\n"));
    }

    [Fact]
    public void TryDeserialize_TypeMissingFromTheContext_Throws()
    {
        Assert.Throws<TomlException>(() => TomlSerializer.TryDeserialize<NotInContext>(SampleToml, TestTomlSerializerContext.Default, out _));
    }

    [Theory]
    [InlineData(typeof(ConfigMemberConverter))]
    [InlineData(typeof(ConfigTwoConstructors))]
    [InlineData(typeof(ConfigStringStyleOnInt))]
    [InlineData(typeof(ConfigTwoExtensionData))]
    [InlineData(typeof(ConfigDuplicateDiscriminator))]
    [InlineData(typeof(ConfigSingleOrArrayOnInt))]
    public void TryDeserialize_ModelConfigurationError_Throws(Type type)
    {
        Assert.Throws<TomlException>(() => TomlSerializer.TryDeserialize("A = 1\nItems = [{ A = 1 }]\n", type, out _));
    }

    private sealed class ConfigMemberConverter
    {
        [TomlConverter(typeof(string))]
        public int A { get; set; }
    }

    private sealed class ConfigTwoConstructors
    {
        public ConfigTwoConstructors(int a) => A = a;

        public ConfigTwoConstructors(string a) => A = a.Length;

        public int A { get; }
    }

    private sealed class ConfigStringStyleOnInt
    {
        [TomlStringStyle(TomlStringStyle.Literal)]
        public int A { get; set; }
    }

    private sealed class ConfigTwoExtensionData
    {
        [TomlExtensionData]
        public Dictionary<string, object>? First { get; set; }

        [TomlExtensionData]
        public Dictionary<string, object>? Second { get; set; }
    }

    [TomlPolymorphic(TypeDiscriminatorPropertyName = "kind")]
    [TomlDerivedType(typeof(ConfigDerivedA), "x")]
    [TomlDerivedType(typeof(ConfigDerivedB), "x")]
    private class ConfigDuplicateDiscriminator
    {
        public int A { get; set; }
    }

    private sealed class ConfigDerivedA : ConfigDuplicateDiscriminator
    {
    }

    private sealed class ConfigDerivedB : ConfigDuplicateDiscriminator
    {
    }

    private sealed class ConfigSingleOrArrayOnInt
    {
        [TomlSingleOrArray]
        public int A { get; set; }
    }

    [Fact]
    public void TryDeserialize_NullResult_ReturnsFalse()
    {
        var options = new TomlSerializerOptions { Converters = [new NullResultConverter()] };

        Assert.False(TomlSerializer.TryDeserialize<NotInContext>(SampleToml, out var value, options));
        Assert.Null(value);
    }

    private sealed class NotInContext
    {
    }

    private sealed class NullResultConverter : TomlConverter<NotInContext>
    {
        public override NotInContext? Read(TomlReader reader)
        {
            reader.Skip();
            return null;
        }

        public override void Write(TomlWriter writer, NotInContext value) => throw new NotSupportedException();
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

    [Theory]
    [InlineData("name = \"a\"\nage = \"abc\"\n")]
    [InlineData("name = 1\nage = 1\n")]
    [InlineData("name = \"a\"\nage = [1]\n")]
    [InlineData("name = \"a\"\nage = 1.5\n")]
    public void Deserialize_InvalidValue_ReportsTheSameErrorWithReflectionAndGeneratedCode(string toml)
    {
        var reflectionError = Assert.Throws<TomlException>(() => TomlSerializer.Deserialize<GeneratedPerson>(toml, CamelCaseOptions));
        var generatedError = Assert.Throws<TomlException>(() => TomlSerializer.Deserialize<GeneratedPerson>(toml, TestTomlSerializerContext.Default));

        Assert.Equal(reflectionError.Message, generatedError.Message);
        Assert.DoesNotContain("using converter", generatedError.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task DeserializeAsync_AllOverloads_ReadTheStream()
    {
        var context = TestTomlSerializerContext.Default;
        var cancellationToken = TestContext.Current.CancellationToken;

        AssertPerson(await ReadAsync(stream => TomlSerializer.DeserializeAsync<GeneratedPerson>(stream, CamelCaseOptions, cancellationToken)));
        AssertPerson(await ReadAsync(stream => TomlSerializer.DeserializeAsync<GeneratedPerson>(stream, context, cancellationToken)));
        AssertPerson(await ReadAsync(stream => TomlSerializer.DeserializeAsync(stream, context.GeneratedPerson, cancellationToken)));
        AssertPerson((GeneratedPerson?)await ReadAsync(stream => TomlSerializer.DeserializeAsync(stream, typeof(GeneratedPerson), CamelCaseOptions, cancellationToken)));
        AssertPerson((GeneratedPerson?)await ReadAsync(stream => TomlSerializer.DeserializeAsync(stream, typeof(GeneratedPerson), context, cancellationToken)));
        AssertPerson((GeneratedPerson?)await ReadAsync(stream => TomlSerializer.DeserializeAsync(stream, (TomlTypeInfo)context.GeneratedPerson, cancellationToken)));

        static void AssertPerson(GeneratedPerson? person)
        {
            Assert.NotNull(person);
            Assert.Equal("Ada", person.Name);
            Assert.Equal(37, person.Age);
        }
    }

    [Fact]
    public async Task SerializeAsync_AllOverloads_WriteTheSameAsSerialize()
    {
        var context = TestTomlSerializerContext.Default;
        var cancellationToken = TestContext.Current.CancellationToken;
        var person = new GeneratedPerson { Name = "Ada", Age = 37 };
        var expected = TomlSerializer.Serialize(person, context);

        Assert.Equal(expected, await WriteAsync(stream => TomlSerializer.SerializeAsync(stream, person, CamelCaseOptions, cancellationToken)));
        Assert.Equal(expected, await WriteAsync(stream => TomlSerializer.SerializeAsync(stream, person, context, cancellationToken)));
        Assert.Equal(expected, await WriteAsync(stream => TomlSerializer.SerializeAsync(stream, person, context.GeneratedPerson, cancellationToken)));
        Assert.Equal(expected, await WriteAsync(stream => TomlSerializer.SerializeAsync(stream, person, typeof(GeneratedPerson), CamelCaseOptions, cancellationToken)));
        Assert.Equal(expected, await WriteAsync(stream => TomlSerializer.SerializeAsync(stream, person, typeof(GeneratedPerson), context, cancellationToken)));
        Assert.Equal(expected, await WriteAsync(stream => TomlSerializer.SerializeAsync(stream, person, (TomlTypeInfo)context.GeneratedPerson, cancellationToken)));

        static async Task<string> WriteAsync(Func<Stream, Task> write)
        {
            using var stream = new AsyncOnlyStream(allowSynchronousIO: false);
            await write(stream);
            return Encoding.UTF8.GetString(stream.ToArray());
        }
    }

    [Fact]
    public async Task DeserializeAsync_Canceled_Throws()
    {
        using var cancellationTokenSource = new CancellationTokenSource();
        await cancellationTokenSource.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await ReadAsync(stream => TomlSerializer.DeserializeAsync(stream, TestTomlSerializerContext.Default.GeneratedPerson, cancellationTokenSource.Token)));
    }

    [Fact]
    public async Task MaxInputLength_LongerInput_Throws()
    {
        var options = new TomlSerializerOptions { MaxInputLength = SampleToml.Length - 1 };
        var cancellationToken = TestContext.Current.CancellationToken;

        var ex = Assert.Throws<TomlException>(() => TomlSerializer.Deserialize<Model.TomlTable>(SampleToml, options));
        Assert.Contains(nameof(TomlSerializerOptions.MaxInputLength), ex.Message, StringComparison.Ordinal);
        using (var reader = new StringReader(SampleToml))
        {
            Assert.Throws<TomlException>(() => TomlSerializer.Deserialize<Model.TomlTable>(reader, options));
        }

        using (var stream = new MemoryStream(Encoding.UTF8.GetBytes(SampleToml)))
        {
            Assert.Throws<TomlException>(() => TomlSerializer.Deserialize<Model.TomlTable>(stream, options));
        }

        Assert.False(TomlSerializer.TryDeserialize<Model.TomlTable>(SampleToml, out _, options));
        await Assert.ThrowsAsync<TomlException>(async () => await ReadAsync(stream => TomlSerializer.DeserializeAsync<Model.TomlTable>(stream, options, cancellationToken)));

        var exactOptions = options with { MaxInputLength = SampleToml.Length };
        Assert.NotNull(TomlSerializer.Deserialize<Model.TomlTable>(SampleToml, exactOptions));
        using (var reader = new StringReader(SampleToml))
        {
            Assert.NotNull(TomlSerializer.Deserialize<Model.TomlTable>(reader, exactOptions));
        }

        Assert.NotNull(await ReadAsync(stream => TomlSerializer.DeserializeAsync<Model.TomlTable>(stream, exactOptions, cancellationToken)));
    }

    [Fact]
    public async Task MaxInputLength_EndlessStream_StopsReading()
    {
        var options = new TomlSerializerOptions { MaxInputLength = 1_000_000 };
        using var stream = new EndlessStream();

        Assert.Throws<TomlException>(() => TomlSerializer.Deserialize<Model.TomlTable>(stream, options));
        await Assert.ThrowsAsync<TomlException>(async () => await TomlSerializer.DeserializeAsync<Model.TomlTable>(stream, options, TestContext.Current.CancellationToken));
    }

    // The naming policy of TestTomlSerializerContext, for the overloads that use reflection
    private static readonly TomlSerializerOptions CamelCaseOptions = new() { PropertyNamingPolicy = TomlNamingPolicy.CamelCase };

    private static async Task<T> ReadAsync<T>(Func<Stream, ValueTask<T>> read)
    {
        using var stream = new AsyncOnlyStream(allowSynchronousIO: true);
        stream.Write(Encoding.UTF8.GetBytes(SampleToml));
        stream.Position = 0;
        stream.AllowSynchronousIO = false;
        return await read(stream);
    }

    [Fact]
    public void MaxInputLength_Negative_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new TomlSerializerOptions { MaxInputLength = -1 });
    }

    // Like an ASP.NET Core request or response body, which rejects synchronous I/O
    private sealed class AsyncOnlyStream(bool allowSynchronousIO) : Stream
    {
        private readonly MemoryStream _inner = new();

        public bool AllowSynchronousIO { get; set; } = allowSynchronousIO;

        public override bool CanRead => true;

        public override bool CanSeek => true;

        public override bool CanWrite => true;

        public override long Length => _inner.Length;

        public override long Position { get => _inner.Position; set => _inner.Position = value; }

        public byte[] ToArray() => _inner.ToArray();

        public override void Flush() => EnsureSynchronousIO();

        public override Task FlushAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public override int Read(byte[] buffer, int offset, int count)
        {
            EnsureSynchronousIO();
            return _inner.Read(buffer, offset, count);
        }

        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) => ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(_inner.Read(buffer.Span));
        }

        public override void Write(byte[] buffer, int offset, int count)
        {
            EnsureSynchronousIO();
            _inner.Write(buffer, offset, count);
        }

        public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) => WriteAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

        public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            _inner.Write(buffer.Span);
            return ValueTask.CompletedTask;
        }

        public override long Seek(long offset, SeekOrigin origin) => _inner.Seek(offset, origin);

        public override void SetLength(long value) => _inner.SetLength(value);

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _inner.Dispose();
            }

            base.Dispose(disposing);
        }

        private void EnsureSynchronousIO()
        {
            if (!AllowSynchronousIO)
            {
                throw new InvalidOperationException("Synchronous I/O is disallowed.");
            }
        }
    }

    private sealed class EndlessStream : Stream
    {
        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => throw new NotSupportedException();

        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }

        public override void Flush()
        {
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            buffer.AsSpan(offset, count).Fill((byte)'#');
            return count;
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
