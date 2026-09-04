#if !MEZIANTOU_FRAMEWORK_ROSLYN_ENABLE_WARNINGS
#pragma warning disable
#endif
#nullable enable
using System;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Meziantou.Framework.Roslyn;

#if !MEZIANTOU_FRAMEWORK_ROSLYN_DISABLE_EMBEDDEDATTRIBUTE
[Microsoft.CodeAnalysis.Embedded]
#endif
internal readonly struct DiagnosticReporter
{
    private readonly Action<Diagnostic> _reportDiagnostic;

    public DiagnosticReporter(SymbolAnalysisContext context)
    {
        _reportDiagnostic = context.ReportDiagnostic;
        Options = context.Options;
        CancellationToken = context.CancellationToken;
    }

    public DiagnosticReporter(OperationAnalysisContext context)
    {
        _reportDiagnostic = context.ReportDiagnostic;
        Options = context.Options;
        CancellationToken = context.CancellationToken;
    }

    public DiagnosticReporter(OperationBlockAnalysisContext context)
    {
        _reportDiagnostic = context.ReportDiagnostic;
        Options = context.Options;
        CancellationToken = context.CancellationToken;
    }

    public DiagnosticReporter(SyntaxNodeAnalysisContext context)
    {
        _reportDiagnostic = context.ReportDiagnostic;
        Options = context.Options;
        CancellationToken = context.CancellationToken;
    }

    public DiagnosticReporter(CompilationAnalysisContext context)
    {
        _reportDiagnostic = context.ReportDiagnostic;
        Options = context.Options;
        CancellationToken = context.CancellationToken;
    }

    public AnalyzerOptions Options { get; }

    public CancellationToken CancellationToken { get; }

    public void ReportDiagnostic(Diagnostic diagnostic) => _reportDiagnostic(diagnostic);

    public static implicit operator DiagnosticReporter(SymbolAnalysisContext context) => new(context);
    public static implicit operator DiagnosticReporter(OperationAnalysisContext context) => new(context);
    public static implicit operator DiagnosticReporter(OperationBlockAnalysisContext context) => new(context);
    public static implicit operator DiagnosticReporter(SyntaxNodeAnalysisContext context) => new(context);
    public static implicit operator DiagnosticReporter(CompilationAnalysisContext context) => new(context);
}
