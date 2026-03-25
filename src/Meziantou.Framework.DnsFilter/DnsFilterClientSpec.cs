using System.Net;

namespace Meziantou.Framework.DnsFilter;

internal sealed class DnsFilterClientSpec
{
    public bool IsExclusion { get; init; }
    public string? Name { get; init; }
    public IPAddress? Address { get; init; }
    public IPNetwork? Network { get; init; }
}
