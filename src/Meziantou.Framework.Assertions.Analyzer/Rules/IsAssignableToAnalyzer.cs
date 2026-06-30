using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace Meziantou.Framework.Analyzers.Assertions;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class IsAssignableToAnalyzer : DiagnosticAnalyzer
{
    public static readonly DiagnosticDescriptor UseIsAssignableToDescriptor = new(
        id: RuleIdentifiers.UseIsAssignableToDiagnosticId,
        title: "Use Assert.IsAssignableTo for type pattern checks",
        messageFormat: "Use Assert.IsAssignableTo<T>(value) for readability",
        category: "Assertions",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor UseIsNotAssignableToDescriptor = new(
        id: RuleIdentifiers.UseIsNotAssignableToDiagnosticId,
        title: "Use Assert.IsNotAssignableTo for type pattern checks",
        messageFormat: "Use Assert.IsNotAssignableTo<T>(value) for readability",
        category: "Assertions",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => [UseIsAssignableToDescriptor, UseIsNotAssignableToDescriptor];

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
        if (!AssertionMethodSelectionAnalyzerCommon.TryGetAssignableTypeCheckMatch(invocationOperation, assertType, out var match))
            return;

        var descriptor = match.AssertionMethodName == "IsAssignableTo" ? UseIsAssignableToDescriptor : UseIsNotAssignableToDescriptor;
        context.ReportDiagnostic(Diagnostic.Create(descriptor, match.ActualOperation.Syntax.GetLocation()));
    }
}
