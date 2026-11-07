namespace Meziantou.Framework.Language.Shell;

public sealed partial class ShellStatementListSyntax
{
    /// <summary>The separators between the statements.</summary>
    public IReadOnlyList<SyntaxToken> SeparatorTokens
    {
        get
        {
            var separators = new List<SyntaxToken>(Statements.SeparatorCount);
            for (var index = 0; index < Statements.SeparatorCount; index++)
            {
                separators.Add(Statements.GetSeparator(index));
            }

            return separators;
        }
    }
}
