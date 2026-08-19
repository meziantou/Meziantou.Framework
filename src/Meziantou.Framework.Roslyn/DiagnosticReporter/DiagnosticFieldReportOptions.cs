#if !MEZIANTOU_FRAMEWORK_ROSLYN_ENABLE_WARNINGS
#pragma warning disable
#endif
#nullable enable
using System;

namespace Meziantou.Framework.Roslyn;

#if !MEZIANTOU_FRAMEWORK_ROSLYN_DISABLE_EMBEDDEDATTRIBUTE
[Microsoft.CodeAnalysis.Embedded]
#endif
[Flags]
internal enum DiagnosticFieldReportOptions
{
    None = 0x0,
    ReportOnReturnType = 0x1,
}
