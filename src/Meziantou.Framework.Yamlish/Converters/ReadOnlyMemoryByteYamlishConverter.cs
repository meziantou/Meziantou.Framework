namespace Meziantou.Framework.Yamlish.Converters;

internal sealed class ReadOnlyMemoryByteYamlishConverter : ScalarYamlishConverter<ReadOnlyMemory<byte>>
{
    protected override ReadOnlyMemory<byte> Parse(string value) => Convert.FromBase64String(value);

    protected override string Format(ReadOnlyMemory<byte> value) => Convert.ToBase64String(value.Span);
}
