using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace Meziantou.Framework.Analyzers.Assertions;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class NullWithValueTypeAnalyzer : DiagnosticAnalyzer
{
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

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => [NullWithValueTypeDescriptor, NotNullWithValueTypeDescriptor];

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterCompilationStartAction(context =>
        {
            var assertType = context.Compilation.GetTypeByMetadataName(AssertionsAnalyzerHelpers.AssertMetadataName);
            if (assertType is null)
                return;

            var unionAttributeType = context.Compilation.GetTypeByMetadataName(AssertionsAnalyzerHelpers.CSharpUnionAttributeMetadataName);
            var unionInterfaceType = context.Compilation.GetTypeByMetadataName(AssertionsAnalyzerHelpers.CSharpUnionInterfaceMetadataName);

            context.RegisterOperationAction(
                context => Analyze(context, assertType, unionAttributeType, unionInterfaceType),
                OperationKind.Invocation);
        });
    }

    private static void Analyze(OperationAnalysisContext context, INamedTypeSymbol assertType, INamedTypeSymbol? unionAttributeType, INamedTypeSymbol? unionInterfaceType)
    {
        var invocationOperation = (IInvocationOperation)context.Operation;
        if (!AssertionMethodSelectionAnalyzerCommon.TryGetNullNotNullValueTypeMatch(invocationOperation, assertType, unionAttributeType, unionInterfaceType, out var match))
            return;

        var descriptor = match.AssertionMethodName == "Equal" ? NullWithValueTypeDescriptor : NotNullWithValueTypeDescriptor;
        context.ReportDiagnostic(Diagnostic.Create(descriptor, match.ActualOperation.Syntax.GetLocation(), match.ValueType.ToDisplayString()));
    }
}
