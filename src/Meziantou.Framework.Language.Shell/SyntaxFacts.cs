namespace Meziantou.Framework.Language.Shell;

/// <summary>Answers questions about a <see cref="SyntaxKind"/> that do not need a node to ask them of.</summary>
public static class SyntaxFacts
{
    /// <summary>Returns the text a kind is always spelled with, or an empty string when its text varies.</summary>
    public static string GetText(SyntaxKind kind) => kind switch
    {
        SyntaxKind.SingleQuoteToken => "'",
        SyntaxKind.DoubleQuoteToken => "\"",
        SyntaxKind.DollarSingleQuoteToken => "$'",
        SyntaxKind.DollarDoubleQuoteToken => "$\"",
        SyntaxKind.BacktickToken => "`",
        SyntaxKind.DollarToken => "$",
        SyntaxKind.DollarOpenParenToken => "$(",
        SyntaxKind.DollarOpenBraceToken => "${",
        SyntaxKind.OpenParenToken => "(",
        SyntaxKind.CloseParenToken => ")",
        SyntaxKind.OpenBraceToken => "{",
        SyntaxKind.CloseBraceToken => "}",
        SyntaxKind.OpenBracketToken => "[",
        SyntaxKind.CloseBracketToken => "]",
        SyntaxKind.PipeToken => "|",
        SyntaxKind.PipeAmpersandToken => "|&",
        SyntaxKind.AmpersandAmpersandToken => "&&",
        SyntaxKind.PipePipeToken => "||",
        SyntaxKind.AmpersandToken => "&",
        SyntaxKind.SemicolonToken => ";",
        SyntaxKind.SemicolonSemicolonToken => ";;",
        SyntaxKind.SemicolonAmpersandToken => ";&",
        SyntaxKind.SemicolonSemicolonAmpersandToken => ";;&",
        SyntaxKind.OpenBracketBracketToken => "[[",
        SyntaxKind.CloseBracketBracketToken => "]]",
        SyntaxKind.OpenParenParenToken => "((",
        SyntaxKind.CloseParenParenToken => "))",
        SyntaxKind.LessThanOpenParenToken => "<(",
        SyntaxKind.EqualsOpenParenToken => "=(",
        SyntaxKind.GreaterThanOpenParenToken => ">(",
        SyntaxKind.ColonColonToken => "::",
        SyntaxKind.DotToken => ".",
        SyntaxKind.CommaToken => ",",
        SyntaxKind.AtParenToken => "@(",
        SyntaxKind.AtBraceToken => "@{",
        SyntaxKind.DollarOpenParenPowerShellToken => "$(",
        SyntaxKind.QuestionQuestionToken => "??",
        SyntaxKind.ColonToken => ":",
        SyntaxKind.SplatToken => "@",
        SyntaxKind.LessThanToken => "<",
        SyntaxKind.GreaterThanToken => ">",
        SyntaxKind.GreaterThanGreaterThanToken => ">>",
        SyntaxKind.GreaterThanPipeToken => ">|",
        SyntaxKind.LessThanAmpersandToken => "<&",
        SyntaxKind.GreaterThanAmpersandToken => ">&",
        SyntaxKind.LessThanGreaterThanToken => "<>",
        SyntaxKind.LessThanLessThanToken => "<<",
        SyntaxKind.LessThanLessThanDashToken => "<<-",
        SyntaxKind.LessThanLessThanLessThanToken => "<<<",
        SyntaxKind.AmpersandGreaterThanToken => "&>",
        SyntaxKind.AmpersandGreaterThanGreaterThanToken => "&>>",
        SyntaxKind.EqualsToken => "=",
        SyntaxKind.PlusEqualsToken => "+=",
        SyntaxKind.ExclamationToken => "!",
        SyntaxKind.AsteriskToken => "*",
        SyntaxKind.AsteriskAsteriskToken => "**",
        SyntaxKind.QuestionToken => "?",
        _ => "",
    };

    /// <summary>Determines whether <paramref name="kind"/> is whitespace, a line break, or a comment.</summary>
    public static bool IsTrivia(SyntaxKind kind) => kind is SyntaxKind.WhitespaceTrivia or SyntaxKind.EndOfLineTrivia
        or SyntaxKind.SingleLineCommentTrivia or SyntaxKind.CmdRemCommentTrivia or SyntaxKind.CmdDoubleColonCommentTrivia
        or SyntaxKind.MultiLineCommentTrivia or SyntaxKind.LineContinuationTrivia;

    /// <summary>Determines whether <paramref name="kind"/> is one of the comment trivia kinds.</summary>
    public static bool IsComment(SyntaxKind kind) => kind is SyntaxKind.SingleLineCommentTrivia
        or SyntaxKind.CmdRemCommentTrivia or SyntaxKind.CmdDoubleColonCommentTrivia or SyntaxKind.MultiLineCommentTrivia;
}
