using System.Collections.Immutable;
using Meziantou.Framework.Roslyn;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace Meziantou.Framework.Analyzers.Assertions;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class CountAssertionAnalyzer : DiagnosticAnalyzer
{
    public static readonly DiagnosticDescriptor UseEmptyDescriptor = new(
        id: RuleIdentifiers.UseEmptyAssertionDiagnosticId,
        title: "Use Assert.Empty or Assert.NotEmpty for zero count checks",
        messageFormat: "Use Assert.{0} for count checks against zero",
        category: "Assertions",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor UseHasCountDescriptor = new(
        id: RuleIdentifiers.UseHasCountAssertionDiagnosticId,
        title: "Use specialized count assertions",
        messageFormat: "Use a specialized count assertion method",
        category: "Assertions",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => [UseEmptyDescriptor, UseHasCountDescriptor];

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterCompilationStartAction(context =>
        {
            var assertType = context.Compilation.GetTypeByMetadataName(AssertionsAnalyzerHelpers.AssertMetadataName);
            if (assertType is null || !CountAssertionAnalyzerCommon.TryCreateSymbols(context.Compilation, out var symbols))
                return;

            context.RegisterOperationAction(context => Analyze(context, assertType, symbols), OperationKind.Invocation);
        });
    }

    private static void Analyze(OperationAnalysisContext context, INamedTypeSymbol assertType, CountAssertionAnalyzerCommon.Symbols symbols)
    {
        var invocationOperation = (IInvocationOperation)context.Operation;
        if (!CountAssertionAnalyzerCommon.TryGetAssertionMatch(invocationOperation, assertType, symbols, out var match))
            return;

        if (match.UseEmptyAssertion)
        {
            context.ReportDiagnostic(UseEmptyDescriptor, match.CountOperation, match.AssertionMethodName);
        }
        else
        {
            context.ReportDiagnostic(UseHasCountDescriptor, match.CountOperation);
        }
    }
}
