using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeFixes;

namespace Meziantou.Framework.Analyzers.Assertions;

[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(SetConditionCodeFixProvider))]
public sealed class SetConditionCodeFixProvider : ConditionRewriteCodeFixProviderBase
{
    public override ImmutableArray<string> FixableDiagnosticIds =>
    [
        RuleIdentifiers.UseProperSubsetDiagnosticId,
        RuleIdentifiers.UseNotProperSubsetDiagnosticId,
        RuleIdentifiers.UseProperSupersetDiagnosticId,
        RuleIdentifiers.UseNotProperSupersetDiagnosticId,
    ];

    private protected override TryGetMatch Matcher => ConditionRewriteAnalyzerCommon.TryGetSetMatch;
}
