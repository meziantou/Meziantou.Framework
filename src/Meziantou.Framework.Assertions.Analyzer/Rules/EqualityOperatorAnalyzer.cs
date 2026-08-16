using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Meziantou.Framework.Analyzers.Assertions;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class EqualityOperatorAnalyzer : ConditionRewriteAnalyzerBase
{
    public static readonly DiagnosticDescriptor UseEqualDescriptor = new(
        id: RuleIdentifiers.UseEqualForEqualityOperatorDiagnosticId,
        title: "Use Assert.Equal instead of Assert.True(a == b)",
        messageFormat: "Use Assert.Equal(expected, actual) to report the compared values",
        category: "Assertions",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor UseNotEqualDescriptor = new(
        id: RuleIdentifiers.UseNotEqualForEqualityOperatorDiagnosticId,
        title: "Use Assert.NotEqual instead of Assert.True(a != b)",
        messageFormat: "Use Assert.NotEqual(expected, actual) to report the compared values",
        category: "Assertions",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => [UseEqualDescriptor, UseNotEqualDescriptor];

    private protected override TryGetMatch Matcher => ConditionRewriteAnalyzerCommon.TryGetEqualityOperatorMatch;

    private protected override DiagnosticDescriptor GetDescriptor(string assertionMethodName)
        => assertionMethodName == "Equal" ? UseEqualDescriptor : UseNotEqualDescriptor;
}
