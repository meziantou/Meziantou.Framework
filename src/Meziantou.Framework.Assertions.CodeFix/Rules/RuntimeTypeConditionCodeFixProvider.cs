using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeFixes;

namespace Meziantou.Framework.Analyzers.Assertions;

[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(RuntimeTypeConditionCodeFixProvider))]
public sealed class RuntimeTypeConditionCodeFixProvider : ConditionRewriteCodeFixProviderBase
{
    public override ImmutableArray<string> FixableDiagnosticIds =>
    [
        RuleIdentifiers.UseIsTypeForRuntimeTypeDiagnosticId,
        RuleIdentifiers.UseIsNotTypeForRuntimeTypeDiagnosticId,
    ];

    private protected override TryGetMatch Matcher => ConditionRewriteAnalyzerCommon.TryGetRuntimeTypeMatch;
}
