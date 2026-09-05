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

    public DiagnosticReporter(SemanticModelAnalysisContext context)
    {
        _reportDiagnostic = context.ReportDiagnostic;
        Options = context.Options;
        CancellationToken = context.CancellationToken;
    }

    public DiagnosticReporter(SyntaxTreeAnalysisContext context)
    {
        _reportDiagnostic = context.ReportDiagnostic;
        Options = context.Options;
        CancellationToken = context.CancellationToken;
    }

    public DiagnosticReporter(CodeBlockAnalysisContext context)
    {
        _reportDiagnostic = context.ReportDiagnostic;
        Options = context.Options;
        CancellationToken = context.CancellationToken;
    }

    public DiagnosticReporter(AdditionalFileAnalysisContext context)
    {
        _reportDiagnostic = context.ReportDiagnostic;
        Options = context.Options;
        CancellationToken = context.CancellationToken;
    }

    /// <summary>
    /// Gets or sets a filter evaluated before a diagnostic is reported by any <see cref="DiagnosticReporter"/>. When the filter returns <see langword="false"/>, the diagnostic is not reported.
    /// </summary>
    /// <remarks>
    /// The value defaults to <see langword="null"/>, so no filtering occurs. It is meant to be set once, when the analyzer is initialized. As the types of this package are embedded, the filter only applies to the assembly that consumes the package.
    /// </remarks>
    public static DiagnosticFilter? CanReportDiagnostic { get; set; }

    public AnalyzerOptions Options { get; }

    public CancellationToken CancellationToken { get; }

    public void ReportDiagnostic(Diagnostic diagnostic)
    {
        var filter = CanReportDiagnostic;
        if (filter is not null && !filter(diagnostic, Options, CancellationToken))
            return;

        _reportDiagnostic(diagnostic);
    }

    public static implicit operator DiagnosticReporter(SymbolAnalysisContext context) => new(context);
    public static implicit operator DiagnosticReporter(OperationAnalysisContext context) => new(context);
    public static implicit operator DiagnosticReporter(OperationBlockAnalysisContext context) => new(context);
    public static implicit operator DiagnosticReporter(SyntaxNodeAnalysisContext context) => new(context);
    public static implicit operator DiagnosticReporter(CompilationAnalysisContext context) => new(context);
    public static implicit operator DiagnosticReporter(SemanticModelAnalysisContext context) => new(context);
    public static implicit operator DiagnosticReporter(SyntaxTreeAnalysisContext context) => new(context);
    public static implicit operator DiagnosticReporter(CodeBlockAnalysisContext context) => new(context);
    public static implicit operator DiagnosticReporter(AdditionalFileAnalysisContext context) => new(context);
}
