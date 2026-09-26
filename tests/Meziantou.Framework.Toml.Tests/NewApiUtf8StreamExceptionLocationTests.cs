using System;
using System.Linq;
using System.IO;
using System.Text;
using Meziantou.Framework.Toml.Model;

namespace Meziantou.Framework.Toml.Tests;

public sealed class NewApiUtf8StreamExceptionLocationTests
{
    [Fact]
    public void Deserialize_Stream_SyntaxError_ThrowsTomlExceptionWithLocation()
    {
        using var stream = new MemoryStream();
        using (var writer = new StreamWriter(stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false), bufferSize: 1024, leaveOpen: true))
        {
            writer.Write("a = [\n");
        }

        stream.Position = 0;
        var ex = Assert.Throws<TomlException>(() => TomlSerializer.Deserialize<TomlTable>(stream));
        Assert.NotNull(ex);
        Assert.NotNull(ex!.Span);

        Assert.Equal(2, ex.Line);
        Assert.True(ex.Column > 0);
    }

    [Fact]
    public void Deserialize_Stream_WithBom_Succeeds()
    {
        using var stream = new MemoryStream();
        using (var writer = new StreamWriter(stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true), bufferSize: 1024, leaveOpen: true))
        {
            writer.Write("a = 1\n");
        }

        stream.Position = 0;
        var table = TomlSerializer.Deserialize<TomlTable>(stream);

        Assert.NotNull(table);
        Assert.Equal(1L, (long)table!["a"]);
    }

    [Fact]
    public void Deserialize_Stream_InvalidUtf8_ThrowsTomlExceptionWithLocation()
    {
        var bytes = "a = 1\nbé = \"x"u8.ToArray().Concat(new byte[] { 0xC3, 0x28 }).Concat("\"\n"u8.ToArray()).ToArray();

        var ex = Assert.Throws<TomlException>(() => TomlSerializer.Deserialize<TomlTable>(new MemoryStream(bytes)));

        Assert.Contains("byte offset 14", ex.Message, StringComparison.Ordinal);
        Assert.Equal(2, ex.Line);
        Assert.Equal(8, ex.Column);
        Assert.IsType<DecoderFallbackException>(ex.InnerException);
        Assert.False(TomlSerializer.TryDeserialize<TomlTable>(new MemoryStream(bytes), out _));
    }

    [Fact]
    public void Serialize_Stream_UnpairedSurrogate_ThrowsTomlException()
    {
        var table = new TomlTable { ["a"] = "x" + (char)0xD800 };

        var ex = Assert.Throws<TomlException>(() => TomlSerializer.Serialize(new MemoryStream(), table));

        Assert.IsType<EncoderFallbackException>(ex.InnerException);
    }
}
