namespace Meziantou.Framework.Yamlish.Converters;

internal sealed class ByteArrayYamlishConverter : ScalarYamlishConverter<byte[]>
{
    protected override byte[] Parse(string value) => Convert.FromBase64String(value);

    protected override string Format(byte[] value) => Convert.ToBase64String(value);
}
