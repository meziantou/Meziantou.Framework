using System.Net;

namespace Meziantou.Framework.Yamlish;

internal sealed class IPNetworkYamlishConverter : ScalarYamlishConverter<IPNetwork>
{
    protected override IPNetwork Parse(string value) => IPNetwork.Parse(value);

    protected override string Format(IPNetwork value) => value.ToString();
}
