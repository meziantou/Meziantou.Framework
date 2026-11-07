using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Meziantou.Framework.Analyzers.Assertions;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class RangeConditionAnalyzer : ConditionRewriteAnalyzerBase
{
    public static readonly DiagnosticDescriptor UseInRangeDescriptor = new(
        id: RuleIdentifiers.UseInRangeDiagnosticId,
        title: "Use Assert.InRange instead of Assert.True(low <= x && x <= high)",
        messageFormat: "Use Assert.InRange(actual, low, high) to report the value and the range",
        category: "Assertions",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor UseNotInRangeDescriptor = new(
        id: RuleIdentifiers.UseNotInRangeDiagnosticId,
        title: "Use Assert.NotInRange instead of Assert.False(low <= x && x <= high)",
        messageFormat: "Use Assert.NotInRange(actual, low, high) to report the value and the range",
        category: "Assertions",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => [UseInRangeDescriptor, UseNotInRangeDescriptor];

    private protected override TryGetMatch Matcher => ConditionRewriteAnalyzerCommon.TryGetRangeMatch;

    private protected override DiagnosticDescriptor GetDescriptor(string assertionMethodName)
        => assertionMethodName == "InRange" ? UseInRangeDescriptor : UseNotInRangeDescriptor;
}
