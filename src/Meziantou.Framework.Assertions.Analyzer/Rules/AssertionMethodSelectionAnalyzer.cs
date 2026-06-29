using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace Meziantou.Framework.Analyzers.Assertions;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class AssertionMethodSelectionAnalyzer : DiagnosticAnalyzer
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

    public static readonly DiagnosticDescriptor NullWithValueTypeDescriptor = new(
        id: RuleIdentifiers.NullWithValueTypeDiagnosticId,
        title: "Do not use Assert.Null with value types",
        messageFormat: "Use Assert.Equal(default({0}), value) instead of Assert.Null for value types",
        category: "Assertions",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor NotNullWithValueTypeDescriptor = new(
        id: RuleIdentifiers.NotNullWithValueTypeDiagnosticId,
        title: "Do not use Assert.NotNull with value types",
        messageFormat: "Use Assert.NotEqual(default({0}), value) instead of Assert.NotNull for value types",
        category: "Assertions",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

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

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => [UseNullDescriptor, UseNotNullDescriptor, NullWithValueTypeDescriptor, NotNullWithValueTypeDescriptor, SameWithValueTypeDescriptor, NotSameWithValueTypeDescriptor];

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
        if (AssertionMethodSelectionAnalyzerCommon.TryGetNullCheckMatch(invocationOperation, assertType, out var nullCheckMatch))
        {
            var descriptor = nullCheckMatch.AssertionMethodName == "Null" ? UseNullDescriptor : UseNotNullDescriptor;
            context.ReportDiagnostic(Diagnostic.Create(descriptor, nullCheckMatch.ActualOperation.Syntax.GetLocation()));
            return;
        }

        if (AssertionMethodSelectionAnalyzerCommon.TryGetNullNotNullValueTypeMatch(invocationOperation, assertType, out var nullNotNullValueTypeMatch))
        {
            var descriptor = nullNotNullValueTypeMatch.AssertionMethodName == "Equal" ? NullWithValueTypeDescriptor : NotNullWithValueTypeDescriptor;
            context.ReportDiagnostic(Diagnostic.Create(descriptor, nullNotNullValueTypeMatch.ActualOperation.Syntax.GetLocation(), nullNotNullValueTypeMatch.ValueType.ToDisplayString()));
            return;
        }

        if (AssertionMethodSelectionAnalyzerCommon.TryGetSameNotSameValueTypeMatch(invocationOperation, assertType, out var sameNotSameValueTypeMatch))
        {
            var descriptor = sameNotSameValueTypeMatch.AssertionMethodName == "Equal" ? SameWithValueTypeDescriptor : NotSameWithValueTypeDescriptor;
            context.ReportDiagnostic(Diagnostic.Create(descriptor, invocationOperation.Syntax.GetLocation()));
        }
    }
}
