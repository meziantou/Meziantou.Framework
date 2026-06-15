namespace Meziantou.Framework.Yamlish;

internal sealed class StringWriterYamlishConverter : ScalarYamlishConverter<StringWriter>
{
    protected override StringWriter Parse(string value)
    {
        var result = new StringWriter(CultureInfo.InvariantCulture);
        result.Write(value);
        return result;
    }

    protected override string Format(StringWriter value) => value.ToString();
}
