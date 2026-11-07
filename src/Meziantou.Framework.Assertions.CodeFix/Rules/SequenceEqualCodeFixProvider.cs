using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeFixes;

namespace Meziantou.Framework.Analyzers.Assertions;

[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(SequenceEqualCodeFixProvider))]
public sealed class SequenceEqualCodeFixProvider : ConditionRewriteCodeFixProviderBase
{
    public override ImmutableArray<string> FixableDiagnosticIds =>
    [
        RuleIdentifiers.UseEqualForSequenceEqualDiagnosticId,
        RuleIdentifiers.UseNotEqualForSequenceEqualDiagnosticId,
    ];

    private protected override TryGetMatch Matcher => ConditionRewriteAnalyzerCommon.TryGetSequenceEqualMatch;
}
