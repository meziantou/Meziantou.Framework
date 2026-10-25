using Meziantou.Framework.Fuzzing;

namespace Meziantou.Framework.Language.Fuzz;

/// <summary>
/// The parsers of Meziantou.Framework.Language.* never throw on malformed input: whatever the text says, the tree
/// reproduces it exactly and reports what is wrong through diagnostics. Each target parses the input with options read
/// from its first bytes, checks the invariants every tree shares (<see cref="SyntaxTreeInvariants"/>), then the
/// properties of the language: the values of the tokens, the lookups of the document, an incremental reparse, or the
/// agreement with an independent implementation (System.Text.Json, System.Text.RegularExpressions, System.Xml).
/// </summary>
public static class LanguageFuzzTargets
{
    public static IReadOnlyList<FuzzTarget> All { get; } = CreateTargets();

    private static FuzzTarget[] CreateTargets() =>
    [.. new[]
    {
        CssFuzzTargets.Targets,
        IniFuzzTargets.Targets,
        JsonFuzzTargets.Targets,
        RegexFuzzTargets.Targets,
        ShellFuzzTargets.Targets,
        SyntaxFactoryFuzzTargets.Targets,
        TomlFuzzTargets.Targets,
        XmlFuzzTargets.Targets,
    }.SelectMany(targets => targets).Select(KnownIssueSeeds.ExcludeKnownIssues)];
}
