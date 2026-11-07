using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeFixes;

namespace Meziantou.Framework.Analyzers.Assertions;

[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(ReferenceEqualsConditionCodeFixProvider))]
public sealed class ReferenceEqualsConditionCodeFixProvider : ConditionRewriteCodeFixProviderBase
{
    public override ImmutableArray<string> FixableDiagnosticIds =>
    [
        RuleIdentifiers.UseSameForReferenceEqualsDiagnosticId,
        RuleIdentifiers.UseNotSameForReferenceEqualsDiagnosticId,
    ];

    private protected override TryGetMatch Matcher => ConditionRewriteAnalyzerCommon.TryGetReferenceEqualsMatch;
}
