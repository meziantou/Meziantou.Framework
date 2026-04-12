using System.Diagnostics;

namespace Meziantou.Framework;

internal static class ProcessWrapperTelemetry
{
    public static readonly ActivitySource ActivitySource = new("Meziantou.Framework.ProcessWrapper");
}
