#nullable enable
using System;

namespace Meziantou.Framework.Roslyn;

[Flags]
internal enum DiagnosticPropertyReportOptions
{
    None = 0x0,
    ReportOnReturnType = 0x1,
}
