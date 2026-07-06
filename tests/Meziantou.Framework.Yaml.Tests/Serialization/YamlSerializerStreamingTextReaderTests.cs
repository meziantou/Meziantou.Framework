namespace Meziantou.Framework.Yaml.Tests.Serialization;
public sealed class YamlSerializerStreamingTextReaderTests
{
    private sealed class ThrowingReadToEndTextReader : TextReader
    {
        private readonly string _text;
        private int _pos;

        public ThrowingReadToEndTextReader(string text)
        {
            _text = text ?? throw new ArgumentNullException(nameof(text));
        }

        public override string ReadToEnd()
        {
            throw new InvalidOperationException("ReadToEnd must not be used by YamlSerializer(TextReader) overloads.");
        }

        public override int Peek()
        {
            return _pos >= _text.Length ? -1 : _text[_pos];
        }

        public override int Read()
        {
            return _pos >= _text.Length ? -1 : _text[_pos++];
        }

        public override int Read(char[] buffer, int index, int count)
        {
            ArgumentNullException.ThrowIfNull(buffer);
            if ((uint)index > (uint)buffer.Length) throw new ArgumentOutOfRangeException(nameof(index));
            if ((uint)count > (uint)(buffer.Length - index)) throw new ArgumentOutOfRangeException(nameof(count));

            var remaining = _text.Length - _pos;
            if (remaining <= 0)
            {
                return 0;
            }

            var toCopy = Math.Min(count, remaining);
            _text.CopyTo(_pos, buffer, index, toCopy);
            _pos += toCopy;
            return toCopy;
        }
    }

    [Fact]
    public void Deserialize_TextReader_ShouldNotCallReadToEnd()
    {
        using var reader = new ThrowingReadToEndTextReader("a: 1\n");

        var dict = YamlSerializer.Deserialize<Dictionary<string, int>>(reader);

        Assert.NotNull(dict);
        Assert.Equal(1, dict["a"]);
    }
}

