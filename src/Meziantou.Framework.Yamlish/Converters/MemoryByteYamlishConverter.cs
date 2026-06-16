namespace Meziantou.Framework.Yamlish.Converters;

internal sealed class MemoryByteYamlishConverter : ScalarYamlishConverter<Memory<byte>>
{
    protected override Memory<byte> Parse(string value) => Convert.FromBase64String(value);

    protected override string Format(Memory<byte> value) => Convert.ToBase64String(value.Span);
}
