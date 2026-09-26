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
}
