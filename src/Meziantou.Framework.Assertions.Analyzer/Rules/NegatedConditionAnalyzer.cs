using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Meziantou.Framework.Analyzers.Assertions;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class NegatedConditionAnalyzer : ConditionRewriteAnalyzerBase
{
    public static readonly DiagnosticDescriptor Descriptor = new(
        id: RuleIdentifiers.UseNegatedConditionAssertionDiagnosticId,
        title: "Use Assert.False instead of Assert.True(!condition)",
        messageFormat: "Use Assert.{0} and drop the negation",
        category: "Assertions",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => [Descriptor];

    private protected override TryGetMatch Matcher => ConditionRewriteAnalyzerCommon.TryGetNegatedConditionMatch;

    private protected override DiagnosticDescriptor GetDescriptor(string assertionMethodName) => Descriptor;
}
