namespace Meziantou.Framework.Yamlish.Converters;

internal sealed class HttpMethodYamlishConverter : ScalarYamlishConverter<HttpMethod>
{
    protected override HttpMethod Parse(string value) => new(value);

    protected override string Format(HttpMethod value) => value.Method;
}
