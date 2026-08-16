using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Meziantou.Framework.Analyzers.Assertions;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class SetConditionAnalyzer : ConditionRewriteAnalyzerBase
{
    public static readonly DiagnosticDescriptor UseProperSubsetDescriptor = new(
        id: RuleIdentifiers.UseProperSubsetDiagnosticId,
        title: "Use Assert.ProperSubset instead of Assert.True(set.IsProperSubsetOf(other))",
        messageFormat: "Use Assert.ProperSubset(expected, actual) to report the compared sets",
        category: "Assertions",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor UseNotProperSubsetDescriptor = new(
        id: RuleIdentifiers.UseNotProperSubsetDiagnosticId,
        title: "Use Assert.NotProperSubset instead of Assert.False(set.IsProperSubsetOf(other))",
        messageFormat: "Use Assert.NotProperSubset(expected, actual) to report the compared sets",
        category: "Assertions",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor UseProperSupersetDescriptor = new(
        id: RuleIdentifiers.UseProperSupersetDiagnosticId,
        title: "Use Assert.ProperSuperset instead of Assert.True(set.IsProperSupersetOf(other))",
        messageFormat: "Use Assert.ProperSuperset(expected, actual) to report the compared sets",
        category: "Assertions",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor UseNotProperSupersetDescriptor = new(
        id: RuleIdentifiers.UseNotProperSupersetDiagnosticId,
        title: "Use Assert.NotProperSuperset instead of Assert.False(set.IsProperSupersetOf(other))",
        messageFormat: "Use Assert.NotProperSuperset(expected, actual) to report the compared sets",
        category: "Assertions",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        [UseProperSubsetDescriptor, UseNotProperSubsetDescriptor, UseProperSupersetDescriptor, UseNotProperSupersetDescriptor];

    private protected override TryGetMatch Matcher => ConditionRewriteAnalyzerCommon.TryGetSetMatch;

    private protected override DiagnosticDescriptor GetDescriptor(string assertionMethodName) => assertionMethodName switch
    {
        "ProperSubset" => UseProperSubsetDescriptor,
        "NotProperSubset" => UseNotProperSubsetDescriptor,
        "ProperSuperset" => UseProperSupersetDescriptor,
        _ => UseNotProperSupersetDescriptor,
    };
}
