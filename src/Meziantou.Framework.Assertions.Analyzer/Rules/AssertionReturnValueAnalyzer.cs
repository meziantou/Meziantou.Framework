using System.Collections.Immutable;
using Meziantou.Framework.Roslyn;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace Meziantou.Framework.Analyzers.Assertions;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class AssertionReturnValueAnalyzer : DiagnosticAnalyzer
{
    public static readonly DiagnosticDescriptor Descriptor = new(
        id: RuleIdentifiers.UseAssertionReturnValueDiagnosticId,
        title: "Use the value returned by the assertion instead of re-deriving it",
        messageFormat: "Use the value returned by Assert.{0} instead of re-deriving it with '{1}'",
        category: "Assertions",
        defaultSeverity: DiagnosticSeverity.Info,
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
        if (!AssertionReturnValueAnalyzerCommon.TryGetMatch(invocationOperation, assertType, out var match))
            return;

        var locations = new List<Location>(match.Rederivations.Length + 1) { invocationOperation.Syntax.GetLocation() };
        foreach (var rederivation in match.Rederivations)
        {
            locations.Add(rederivation.Syntax.GetLocation());
        }

        context.ReportDiagnostic(Descriptor, locations, invocationOperation.TargetMethod.Name, match.Rederivations[0].Syntax.ToString());
    }
}
