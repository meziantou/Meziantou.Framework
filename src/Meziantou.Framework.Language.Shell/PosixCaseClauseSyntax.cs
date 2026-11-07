namespace Meziantou.Framework.Language.Shell;

public sealed partial class PosixCaseClauseSyntax
{
    /// <summary>The <c>|</c> tokens separating alternative patterns.</summary>
    public IReadOnlyList<SyntaxToken> PatternSeparatorTokens
    {
        get
        {
            var separators = new List<SyntaxToken>(Patterns.SeparatorCount);
            for (var index = 0; index < Patterns.SeparatorCount; index++)
            {
                separators.Add(Patterns.GetSeparator(index));
            }

            return separators;
        }
    }
}
