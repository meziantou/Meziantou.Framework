#nullable enable
using System;

namespace Meziantou.Framework.Roslyn;

[Flags]
internal enum DiagnosticParameterReportOptions
{
    None = 0x0,
    ReportOnType = 0x1,
}
