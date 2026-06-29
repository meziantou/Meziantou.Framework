using System.Diagnostics;

namespace Meziantou.DnsProxy;

internal static class DnsProxyTelemetry
{
    public static readonly ActivitySource ActivitySource = new("Meziantou.DnsProxy");
}
