using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Meziantou.Framework.Analyzers.Assertions;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class ReferenceEqualsConditionAnalyzer : ConditionRewriteAnalyzerBase
{
    public static readonly DiagnosticDescriptor UseSameDescriptor = new(
        id: RuleIdentifiers.UseSameForReferenceEqualsDiagnosticId,
        title: "Use Assert.Same instead of Assert.True(ReferenceEquals(a, b))",
        messageFormat: "Use Assert.Same(expected, actual) to report the compared instances",
        category: "Assertions",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor UseNotSameDescriptor = new(
        id: RuleIdentifiers.UseNotSameForReferenceEqualsDiagnosticId,
        title: "Use Assert.NotSame instead of Assert.False(ReferenceEquals(a, b))",
        messageFormat: "Use Assert.NotSame(expected, actual) to report the compared instances",
        category: "Assertions",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => [UseSameDescriptor, UseNotSameDescriptor];

    private protected override TryGetMatch Matcher => ConditionRewriteAnalyzerCommon.TryGetReferenceEqualsMatch;

    private protected override DiagnosticDescriptor GetDescriptor(string assertionMethodName)
        => assertionMethodName == "Same" ? UseSameDescriptor : UseNotSameDescriptor;
}
