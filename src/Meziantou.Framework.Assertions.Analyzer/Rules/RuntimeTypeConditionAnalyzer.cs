using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Meziantou.Framework.Analyzers.Assertions;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class RuntimeTypeConditionAnalyzer : ConditionRewriteAnalyzerBase
{
    public static readonly DiagnosticDescriptor UseIsTypeDescriptor = new(
        id: RuleIdentifiers.UseIsTypeForRuntimeTypeDiagnosticId,
        title: "Use Assert.IsType instead of Assert.True(x.GetType() == typeof(T))",
        messageFormat: "Use Assert.IsType<T>(actual) to report the actual runtime type",
        category: "Assertions",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor UseIsNotTypeDescriptor = new(
        id: RuleIdentifiers.UseIsNotTypeForRuntimeTypeDiagnosticId,
        title: "Use Assert.IsNotType instead of Assert.False(x.GetType() == typeof(T))",
        messageFormat: "Use Assert.IsNotType<T>(actual) to report the actual runtime type",
        category: "Assertions",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => [UseIsTypeDescriptor, UseIsNotTypeDescriptor];

    private protected override TryGetMatch Matcher => ConditionRewriteAnalyzerCommon.TryGetRuntimeTypeMatch;

    private protected override DiagnosticDescriptor GetDescriptor(string assertionMethodName)
        => assertionMethodName == "IsType" ? UseIsTypeDescriptor : UseIsNotTypeDescriptor;
}
