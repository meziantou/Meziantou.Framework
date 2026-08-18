#nullable enable
using System;

namespace Meziantou.Framework.Roslyn;

[Flags]
internal enum DiagnosticFieldReportOptions
{
    None = 0x0,
    ReportOnReturnType = 0x1,
}
