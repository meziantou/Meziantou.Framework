using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace Meziantou.Framework.Analyzers.Assertions;

/// <summary>
/// Base class for the rules that rewrite <c>Assert.True(condition)</c> / <c>Assert.False(condition)</c> into a
/// dedicated assertion. Derived types only provide the detection method and the descriptor to report.
/// </summary>
public abstract class ConditionRewriteAnalyzerBase : DiagnosticAnalyzer
{
    private protected delegate bool TryGetMatch(
        IInvocationOperation assertInvocation,
        INamedTypeSymbol assertType,
        ConditionRewriteAnalyzerCommon.Symbols symbols,
        out ConditionRewriteAnalyzerCommon.ConditionRewriteMatch match);

    private protected abstract TryGetMatch Matcher { get; }

    private protected abstract DiagnosticDescriptor GetDescriptor(string assertionMethodName);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterCompilationStartAction(context =>
        {
            var assertType = context.Compilation.GetTypeByMetadataName(AssertionsAnalyzerHelpers.AssertMetadataName);
            if (assertType is null || !ConditionRewriteAnalyzerCommon.TryCreateSymbols(context.Compilation, out var symbols))
                return;

            context.RegisterOperationAction(context => Analyze(context, assertType, symbols.Value), OperationKind.Invocation);
        });
    }

    private void Analyze(OperationAnalysisContext context, INamedTypeSymbol assertType, ConditionRewriteAnalyzerCommon.Symbols symbols)
    {
        var assertInvocation = (IInvocationOperation)context.Operation;
        if (!Matcher(assertInvocation, assertType, symbols, out var match))
            return;

        context.ReportDiagnostic(Diagnostic.Create(GetDescriptor(match.AssertionMethodName), match.ReportOperation.Syntax.GetLocation(), match.AssertionMethodName));
    }
}
