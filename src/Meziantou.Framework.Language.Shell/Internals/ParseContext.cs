namespace Meziantou.Framework.Language.Shell.Internals;

/// <summary>Describes where a statement list ends.</summary>
/// <param name="StopCharacter">A closing character such as <c>)</c>, or <c>\0</c> when there is none.</param>
/// <param name="StopWords">Reserved words that terminate the list, such as <c>then</c> or <c>done</c>.</param>
/// <param name="StopAtCaseTerminator">Whether <c>;;</c> ends the list, which is true only inside a case clause.</param>
internal readonly record struct ParseContext(char StopCharacter, string[]? StopWords, bool StopAtCaseTerminator)
{
    public static ParseContext TopLevel => new('\0', StopWords: null, StopAtCaseTerminator: false);

    public static ParseContext UntilCharacter(char stopCharacter) => new(stopCharacter, StopWords: null, StopAtCaseTerminator: false);

    public static ParseContext UntilWords(params string[] stopWords) => new('\0', stopWords, StopAtCaseTerminator: false);

    public static ParseContext CaseClauseBody => new('\0', ["esac"], StopAtCaseTerminator: true);
}
