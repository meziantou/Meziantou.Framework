using System.Collections.Immutable;
using Meziantou.Framework.Roslyn;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace Meziantou.Framework.Analyzers.Assertions;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class AwaitableDelegateAnalyzer : DiagnosticAnalyzer
{
    public static readonly DiagnosticDescriptor Descriptor = new(
        id: RuleIdentifiers.AwaitableDelegateDiagnosticId,
        title: "Do not pass an awaitable delegate to an assertion that does not await it",
        messageFormat: "The assertion runs this delegate synchronously and never awaits {0}, so it does not observe what happens after the first await",
        category: "Assertions",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => [Descriptor];

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterCompilationStartAction(context =>
        {
            var assertType = context.Compilation.GetTypeByMetadataName(AssertionsAnalyzerHelpers.AssertMetadataName);
            if (assertType is null || !AwaitableDelegateAnalyzerCommon.TryCreateSymbols(context.Compilation, out var symbols))
                return;

            context.RegisterOperationAction(context => Analyze(context, assertType, symbols.Value), OperationKind.Argument);
        });
    }

    private static void Analyze(OperationAnalysisContext context, INamedTypeSymbol assertType, AwaitableDelegateAnalyzerCommon.Symbols symbols)
    {
        var argumentOperation = (IArgumentOperation)context.Operation;
        if (!AwaitableDelegateAnalyzerCommon.TryGetMatch(argumentOperation, assertType, symbols, out var match))
            return;

        var awaitedValue = match.AwaitableType is null
            ? "the task of this async void lambda"
            : "the returned " + match.AwaitableType.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat);
        context.ReportDiagnostic(Descriptor, match.DelegateCreation, awaitedValue);
    }
}
