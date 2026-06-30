using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace Meziantou.Framework.Analyzers.Assertions;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class UseNullAssertionAnalyzer : DiagnosticAnalyzer
{
    public static readonly DiagnosticDescriptor UseNullDescriptor = new(
        id: RuleIdentifiers.UseNullAssertionDiagnosticId,
        title: "Use Assert.Null for null comparisons",
        messageFormat: "Use Assert.Null instead of Assert.True(value == null)",
        category: "Assertions",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor UseNotNullDescriptor = new(
        id: RuleIdentifiers.UseNotNullAssertionDiagnosticId,
        title: "Use Assert.NotNull for null comparisons",
        messageFormat: "Use Assert.NotNull instead of Assert.True(value != null)",
        category: "Assertions",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => [UseNullDescriptor, UseNotNullDescriptor];

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterCompilationStartAction(context =>
        {
            var assertType = context.Compilation.GetTypeByMetadataName(AssertionsAnalyzerHelpers.AssertMetadataName);
            if (assertType is null)
                return;

            context.RegisterOperationAction(
                context => Analyze(context, assertType),
                OperationKind.Invocation);
        });
    }

    private static void Analyze(OperationAnalysisContext context, INamedTypeSymbol assertType)
    {
        var invocationOperation = (IInvocationOperation)context.Operation;
        if (!AssertionMethodSelectionAnalyzerCommon.TryGetNullCheckMatch(invocationOperation, assertType, out var nullCheckMatch))
            return;

        var descriptor = nullCheckMatch.AssertionMethodName == "Null" ? UseNullDescriptor : UseNotNullDescriptor;
        context.ReportDiagnostic(Diagnostic.Create(descriptor, nullCheckMatch.ActualOperation.Syntax.GetLocation()));
    }
}
