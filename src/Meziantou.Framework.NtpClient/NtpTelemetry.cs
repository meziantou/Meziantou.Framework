using System.Diagnostics;

namespace Meziantou.Framework.Ntp;

internal static class NtpTelemetry
{
    public static readonly ActivitySource ActivitySource = new("Meziantou.Framework.NtpClient");
}
