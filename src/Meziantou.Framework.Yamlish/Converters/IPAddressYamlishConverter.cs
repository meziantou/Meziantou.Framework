using System.Net;

namespace Meziantou.Framework.Yamlish.Converters;

internal sealed class IPAddressYamlishConverter : ScalarYamlishConverter<IPAddress>
{
    protected override IPAddress Parse(string value) => IPAddress.Parse(value);

    protected override string Format(IPAddress value) => value.ToString();
}
