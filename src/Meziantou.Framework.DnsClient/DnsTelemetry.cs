using System.Diagnostics;

namespace Meziantou.Framework.DnsClient;

internal static class DnsTelemetry
{
    public static readonly ActivitySource ActivitySource = new("Meziantou.Framework.DnsClient");
}
