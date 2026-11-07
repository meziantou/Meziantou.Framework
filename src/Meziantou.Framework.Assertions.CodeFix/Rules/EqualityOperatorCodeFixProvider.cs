using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeFixes;

namespace Meziantou.Framework.Analyzers.Assertions;

[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(EqualityOperatorCodeFixProvider))]
public sealed class EqualityOperatorCodeFixProvider : ConditionRewriteCodeFixProviderBase
{
    public override ImmutableArray<string> FixableDiagnosticIds =>
    [
        RuleIdentifiers.UseEqualForEqualityOperatorDiagnosticId,
        RuleIdentifiers.UseNotEqualForEqualityOperatorDiagnosticId,
    ];

    private protected override TryGetMatch Matcher => ConditionRewriteAnalyzerCommon.TryGetEqualityOperatorMatch;
}
