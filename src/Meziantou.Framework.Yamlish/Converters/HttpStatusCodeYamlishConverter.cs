using System.Net;

namespace Meziantou.Framework.Yamlish.Converters;

internal sealed class HttpStatusCodeYamlishConverter : ScalarYamlishConverter<HttpStatusCode>
{
    protected override HttpStatusCode Parse(string value)
    {
        var separatorIndex = value.IndexOf(' ', StringComparison.Ordinal);
        var numericValue = separatorIndex < 0 ? value : value[..separatorIndex];
        return (HttpStatusCode)int.Parse(numericValue, NumberStyles.Integer, CultureInfo.InvariantCulture);
    }

    protected override string Format(HttpStatusCode value) => $"{((int)value).ToString(CultureInfo.InvariantCulture)} ({value})";
}
