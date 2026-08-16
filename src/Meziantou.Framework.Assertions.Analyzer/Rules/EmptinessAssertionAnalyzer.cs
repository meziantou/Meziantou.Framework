using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace Meziantou.Framework.Analyzers.Assertions;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class EmptinessAssertionAnalyzer : DiagnosticAnalyzer
{
    public static readonly DiagnosticDescriptor Descriptor = new(
        id: RuleIdentifiers.UseEmptinessAssertionDiagnosticId,
        title: "Use Assert.Empty or Assert.NotEmpty instead of a count assertion",
        messageFormat: "Use Assert.{0} instead of a count assertion against zero",
        category: "Assertions",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => [Descriptor];

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterCompilationStartAction(context =>
        {
            var assertType = context.Compilation.GetTypeByMetadataName(AssertionsAnalyzerHelpers.AssertMetadataName);
            if (assertType is null)
                return;

            context.RegisterOperationAction(context => Analyze(context, assertType), OperationKind.Invocation);
        });
    }

    private static void Analyze(OperationAnalysisContext context, INamedTypeSymbol assertType)
    {
        var invocationOperation = (IInvocationOperation)context.Operation;
        if (!EmptinessAssertionAnalyzerCommon.TryGetAssertionMatch(invocationOperation, assertType, out var match))
            return;

        context.ReportDiagnostic(Diagnostic.Create(Descriptor, invocationOperation.Syntax.GetLocation(), match.AssertionMethodName));
    }
}
