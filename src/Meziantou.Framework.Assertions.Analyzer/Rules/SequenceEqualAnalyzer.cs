using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Meziantou.Framework.Analyzers.Assertions;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class SequenceEqualAnalyzer : ConditionRewriteAnalyzerBase
{
    public static readonly DiagnosticDescriptor UseEqualDescriptor = new(
        id: RuleIdentifiers.UseEqualForSequenceEqualDiagnosticId,
        title: "Use Assert.Equal instead of Assert.True(actual.SequenceEqual(expected))",
        messageFormat: "Use Assert.Equal(expected, actual) to report the differing items",
        category: "Assertions",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor UseNotEqualDescriptor = new(
        id: RuleIdentifiers.UseNotEqualForSequenceEqualDiagnosticId,
        title: "Use Assert.NotEqual instead of Assert.False(actual.SequenceEqual(expected))",
        messageFormat: "Use Assert.NotEqual(expected, actual) to report the compared sequences",
        category: "Assertions",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => [UseEqualDescriptor, UseNotEqualDescriptor];

    private protected override TryGetMatch Matcher => ConditionRewriteAnalyzerCommon.TryGetSequenceEqualMatch;

    private protected override DiagnosticDescriptor GetDescriptor(string assertionMethodName)
        => assertionMethodName == "Equal" ? UseEqualDescriptor : UseNotEqualDescriptor;
}
