using System.Net.Sockets;

namespace Meziantou.Framework.Yamlish.Converters;

internal sealed class UnixDomainSocketEndPointYamlishConverter : ScalarYamlishConverter<UnixDomainSocketEndPoint>
{
    protected override UnixDomainSocketEndPoint Parse(string value) => new(value);

    protected override string Format(UnixDomainSocketEndPoint value) => value.ToString();
}
