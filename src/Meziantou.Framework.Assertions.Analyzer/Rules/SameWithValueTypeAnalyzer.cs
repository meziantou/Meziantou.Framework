using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace Meziantou.Framework.Analyzers.Assertions;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class SameWithValueTypeAnalyzer : DiagnosticAnalyzer
{
    public static readonly DiagnosticDescriptor SameWithValueTypeDescriptor = new(
        id: RuleIdentifiers.SameWithValueTypeDiagnosticId,
        title: "Do not use Assert.Same with value types",
        messageFormat: "Use Assert.Equal instead of Assert.Same for value types",
        category: "Assertions",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor NotSameWithValueTypeDescriptor = new(
        id: RuleIdentifiers.NotSameWithValueTypeDiagnosticId,
        title: "Do not use Assert.NotSame with value types",
        messageFormat: "Use Assert.NotEqual instead of Assert.NotSame for value types",
        category: "Assertions",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => [SameWithValueTypeDescriptor, NotSameWithValueTypeDescriptor];

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
        if (!AssertionMethodSelectionAnalyzerCommon.TryGetSameNotSameValueTypeMatch(invocationOperation, assertType, out var match))
            return;

        var descriptor = match.AssertionMethodName == "Equal" ? SameWithValueTypeDescriptor : NotSameWithValueTypeDescriptor;
        context.ReportDiagnostic(Diagnostic.Create(descriptor, invocationOperation.Syntax.GetLocation()));
    }
}
