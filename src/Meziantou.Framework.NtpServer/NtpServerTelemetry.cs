using System.Diagnostics;

namespace Meziantou.Framework.Ntp;

internal static class NtpServerTelemetry
{
    public static readonly ActivitySource ActivitySource = new("Meziantou.Framework.NtpServer");
}
