namespace Meziantou.Framework.Yaml.Tests.Serialization;
public sealed class YamlSerializerStreamTests
{
    [Fact]
    public void Serialize_Stream_ShouldWriteYamlAndLeaveStreamOpen()
    {
        using var stream = new MemoryStream();

        YamlSerializer.Serialize(stream, new Dictionary<string, int>(StringComparer.Ordinal) { ["a"] = 1 });

        Assert.True(stream.CanRead);
        stream.Position = 0;
        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true, leaveOpen: true);
        var yaml = reader.ReadToEnd();

        Assert.Contains("a:", yaml);
        Assert.Contains("1", yaml);
    }

    [Fact]
    public void Deserialize_Stream_ShouldReadYamlAndLeaveStreamOpen()
    {
        var data = Encoding.UTF8.GetBytes("a: 1\n");
        using var stream = new MemoryStream(data);

        var dict = YamlSerializer.Deserialize<Dictionary<string, int>>(stream);

        Assert.NotNull(dict);
        Assert.Equal(1, dict["a"]);
        Assert.True(stream.CanRead);
    }

    [Fact]
    public void SerializeAndDeserialize_Stream_WithContext()
    {
        var context = new TestYamlSerializerContext();
        var person = new GeneratedPerson { FirstName = "Bob", Age = 42 };

        using var stream = new MemoryStream();
        YamlSerializer.Serialize(stream, person, context);

        Assert.True(stream.CanRead);
        stream.Position = 0;

        var roundtripped = YamlSerializer.Deserialize<GeneratedPerson>(stream, context);

        Assert.NotNull(roundtripped);
        Assert.Equal("Bob", roundtripped.FirstName);
        Assert.Equal(42, roundtripped.Age);
    }

    [Fact]
    public void Deserialize_StreamWithInvalidUtf8_ThrowsYamlException()
    {
        byte[] payload = [(byte)'a', (byte)':', (byte)' ', 0xFF, (byte)'\n'];

        // The bytes are read as is: the error has the location of the invalid byte
        var exception = Assert.Throws<SyntaxErrorException>(() => YamlSerializer.Deserialize<Dictionary<string, string>>(new MemoryStream(payload)));
        Assert.Contains("UTF-8", exception.Message, StringComparison.Ordinal);
        Assert.Equal(0, exception.Start.Line);
        Assert.Equal(3, exception.Start.Column);
        Assert.False(YamlSerializer.TryDeserialize<Dictionary<string, string>>(new MemoryStream(payload), out _));
        Assert.False(YamlSerializer.TryDeserialize(new MemoryStream(payload), typeof(Dictionary<string, string>), out _));
    }

    [Theory]
    [InlineData("utf-8")]
    [InlineData("utf-8-bom")]
    [InlineData("utf-16")]
    [InlineData("utf-16BE")]
    [InlineData("utf-32")]
    public void Deserialize_StreamWithByteOrderMark_DetectsTheEncoding(string encodingName)
    {
        var encoding = encodingName switch
        {
            "utf-8" => new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
            "utf-8-bom" => new UTF8Encoding(encoderShouldEmitUTF8Identifier: true),
            "utf-16" => Encoding.Unicode,
            "utf-16BE" => Encoding.BigEndianUnicode,
            _ => Encoding.UTF32,
        };
        var payload = encoding.GetPreamble().Concat(encoding.GetBytes("a: é😀\nb: [1, 2]\n")).ToArray();

        // A stream that returns a few bytes per read: the byte order mark and the characters are split between reads
        using var stream = new ParserTests.TrickleStream(payload);
        var value = YamlSerializer.Deserialize<Dictionary<string, object>>(stream);

        Assert.NotNull(value);
        Assert.Equal("é😀", value["a"]);
    }

    [Fact]
    public void Deserialize_StreamReadByFewBytes_ReadsTheSameValueAsTheString()
    {
        var builder = new StringBuilder();
        for (var i = 0; i < 300; i++)
        {
            builder.Append(CultureInfo.InvariantCulture, $"- name: item-{i} é\n  # comment {i}\n  text: |\n    line {i} 😀\n  flow: [a, {{ k: \"v{i}\" }}]\n");
        }

        var yaml = builder.ToString();
        var expected = YamlSerializer.Deserialize<List<Dictionary<string, object>>>(yaml);
        using var stream = new ParserTests.TrickleStream(Encoding.UTF8.GetBytes(yaml));
        var actual = YamlSerializer.Deserialize<List<Dictionary<string, object>>>(stream);

        Assert.Equal(YamlSerializer.Serialize(expected), YamlSerializer.Serialize(actual));
    }

    [Fact]
    public void Deserialize_ConversionErrorBeforeASyntaxError_IsReportedFirstByEveryInput()
    {
        // The tokens are read by batches: an error of the reader must not be reported before the tokens that come before it
        var builder = new StringBuilder("- first_name: a\n  Age: not a number\n");
        for (var i = 0; i < 200; i++)
        {
            builder.Append("- Age: 1\n");
        }

        builder.Append("- [unterminated\n");
        var yaml = builder.ToString();
        var utf8 = Encoding.UTF8.GetBytes(yaml);

        var expected = Assert.Throws<YamlException>(() => YamlSerializer.Deserialize<List<GeneratedPerson>>(yaml));
        Assert.Equal(1, expected.Start.Line);
        Assert.Equal(expected.Message, Assert.Throws<YamlException>(() => YamlSerializer.Deserialize<List<GeneratedPerson>>(utf8.AsSpan())).Message);
        Assert.Equal(expected.Message, Assert.Throws<YamlException>(() => YamlSerializer.Deserialize<List<GeneratedPerson>>(new StringReader(yaml))).Message);
        using var stream = new ParserTests.TrickleStream(utf8);
        Assert.Equal(expected.Message, Assert.Throws<YamlException>(() => YamlSerializer.Deserialize<List<GeneratedPerson>>(stream)).Message);
    }

    [Fact]
    public void Deserialize_Utf8Bytes()
    {
        #region YamlSerializer_DeserializeUtf8
        // The bytes are parsed as is, without being decoded to a string first
        var ports = YamlSerializer.Deserialize<Dictionary<string, int>>("http: 80\nhttps: 443\n"u8);
        Assert.Equal(443, ports!["https"]);
        #endregion

        Assert.Equal("Bob", YamlSerializer.Deserialize<GeneratedPerson>("first_name: Bob"u8)!.FirstName);

        var context = new TestYamlSerializerContext();
        Assert.Equal(42, YamlSerializer.Deserialize<GeneratedPerson>("Age: 42"u8, context)!.Age);
        Assert.Equal(42, ((GeneratedPerson)YamlSerializer.Deserialize("Age: 42"u8, typeof(GeneratedPerson), context)!).Age);
        Assert.Equal(42, ((GeneratedPerson)YamlSerializer.Deserialize("Age: 42"u8, typeof(GeneratedPerson))!).Age);
        Assert.Equal(42, YamlSerializer.Deserialize("Age: 42"u8, context.GeneratedPerson)!.Age);
        Assert.Equal(42, ((GeneratedPerson)YamlSerializer.Deserialize("Age: 42"u8, (YamlTypeInfo)context.GeneratedPerson)!).Age);
        Assert.Equal([1, 2], YamlSerializer.Deserialize<int[]>("\uFEFF[1, 2]"u8));
        Assert.Equal(0, YamlSerializer.Deserialize<int>(""u8));

        Assert.True(YamlSerializer.TryDeserialize<int[]>("[1]"u8, out var array));
        Assert.Equal([1], array);
        Assert.False(YamlSerializer.TryDeserialize<int[]>("[1"u8, out _));
        Assert.False(YamlSerializer.TryDeserialize<int[]>([(byte)'[', 0xFF, (byte)']'], out _));
        Assert.False(YamlSerializer.TryDeserialize("[1"u8, typeof(int[]), out _));
        Assert.False(YamlSerializer.TryDeserialize<GeneratedPerson>("Age: ["u8, context, out _));
        Assert.False(YamlSerializer.TryDeserialize("Age: ["u8, typeof(GeneratedPerson), context, out _));
    }
}
