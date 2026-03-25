using System.Diagnostics;

namespace Meziantou.Framework.NtpClient;

internal static class NtpTelemetry
{
    public static readonly ActivitySource ActivitySource = new("Meziantou.Framework.NtpClient");
}
