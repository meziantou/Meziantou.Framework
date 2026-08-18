#nullable enable
using System;

namespace Meziantou.Framework.Roslyn;

[Flags]
internal enum DiagnosticMethodReportOptions
{
    None = 0x0,
    ReportOnMethodName = 0x1,
    ReportOnReturnType = 0x2,
}
