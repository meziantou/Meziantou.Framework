using System.Collections.Immutable;
using Meziantou.Framework.Roslyn;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace Meziantou.Framework.Analyzers.Assertions;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class AwaitAssertionAnalyzer : DiagnosticAnalyzer
{
    public static readonly DiagnosticDescriptor AwaitAssertionDescriptor = new(
        id: RuleIdentifiers.AwaitAssertionDiagnosticId,
        title: "Await assertions that return a Task",
        messageFormat: "The task returned by this assertion is never awaited, so the assertion never runs",
        category: "Assertions",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => [AwaitAssertionDescriptor];

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterCompilationStartAction(context =>
        {
            var assertType = context.Compilation.GetTypeByMetadataName(AssertionsAnalyzerHelpers.AssertMetadataName);
            if (assertType is null || !AwaitAssertionAnalyzerCommon.TryCreateSymbols(context.Compilation, out var symbols))
                return;

            context.RegisterOperationAction(context => Analyze(context, assertType, symbols.Value), OperationKind.Invocation);
        });
    }

    private static void Analyze(OperationAnalysisContext context, INamedTypeSymbol assertType, AwaitAssertionAnalyzerCommon.Symbols symbols)
    {
        var invocationOperation = (IInvocationOperation)context.Operation;
        if (!AwaitAssertionAnalyzerCommon.IsDiscardedTaskAssertion(invocationOperation, assertType, symbols))
            return;

        context.ReportDiagnostic(AwaitAssertionDescriptor, invocationOperation);
    }
}
