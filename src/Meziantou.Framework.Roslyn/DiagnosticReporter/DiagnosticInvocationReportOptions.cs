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
internal enum DiagnosticInvocationReportOptions
{
    None = 0x0,
    ReportOnMember = 0x1,
    ReportOnArguments = 0x2,
}
