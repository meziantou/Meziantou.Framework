using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace Meziantou.Framework.Analyzers.Assertions;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class RegexMatchesAnalyzer : DiagnosticAnalyzer
{
    public static readonly DiagnosticDescriptor UseMatchesDescriptor = new(
        id: RuleIdentifiers.UseMatchesDiagnosticId,
        title: "Use Assert.Matches instead of Assert.True(Regex.IsMatch(...))",
        messageFormat: "Use Assert.Matches(pattern, actual) for readability",
        category: "Assertions",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor UseDoesNotMatchDescriptor = new(
        id: RuleIdentifiers.UseDoesNotMatchDiagnosticId,
        title: "Use Assert.DoesNotMatch instead of Assert.False(Regex.IsMatch(...))",
        messageFormat: "Use Assert.DoesNotMatch(pattern, actual) for readability",
        category: "Assertions",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => [UseMatchesDescriptor, UseDoesNotMatchDescriptor];

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterCompilationStartAction(context =>
        {
            var assertType = context.Compilation.GetTypeByMetadataName(AssertionsAnalyzerHelpers.AssertMetadataName);
            if (assertType is null || !TrueFalseConditionMethodSelectionAnalyzerCommon.TryCreateSymbols(context.Compilation, out var symbols))
                return;

            context.RegisterOperationAction(
                context => Analyze(context, assertType, symbols.Value),
                OperationKind.Invocation);
        });
    }

    private static void Analyze(OperationAnalysisContext context, INamedTypeSymbol assertType, TrueFalseConditionMethodSelectionAnalyzerCommon.Symbols symbols)
    {
        var assertInvocation = (IInvocationOperation)context.Operation;
        if (!TrueFalseConditionMethodSelectionAnalyzerCommon.TryGetAssertInnerInvocation(assertInvocation, assertType, out var innerInvocation, out var conditionExpectedToBeFalse))
            return;

        if (!TrueFalseConditionMethodSelectionAnalyzerCommon.TryGetRegexIsMatchMatch(innerInvocation, symbols, conditionExpectedToBeFalse, out var match))
            return;

        var descriptor = conditionExpectedToBeFalse ? UseDoesNotMatchDescriptor : UseMatchesDescriptor;
        context.ReportDiagnostic(Diagnostic.Create(descriptor, match.InnerInvocation.Syntax.GetLocation()));
    }
}
