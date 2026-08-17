using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeFixes;

namespace Meziantou.Framework.Analyzers.Assertions;

[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(NegatedConditionCodeFixProvider))]
public sealed class NegatedConditionCodeFixProvider : ConditionRewriteCodeFixProviderBase
{
    public override ImmutableArray<string> FixableDiagnosticIds =>
    [
        RuleIdentifiers.UseNegatedConditionAssertionDiagnosticId,
    ];

    private protected override TryGetMatch Matcher => ConditionRewriteAnalyzerCommon.TryGetNegatedConditionMatch;
}
