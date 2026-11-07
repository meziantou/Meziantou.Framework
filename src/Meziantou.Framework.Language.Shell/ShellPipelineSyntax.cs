namespace Meziantou.Framework.Language.Shell;

public sealed partial class ShellPipelineSyntax
{
    /// <summary>The operators joining the commands.</summary>
    public IReadOnlyList<SyntaxToken> OperatorTokens
    {
        get
        {
            var separators = new List<SyntaxToken>(Commands.SeparatorCount);
            for (var index = 0; index < Commands.SeparatorCount; index++)
            {
                separators.Add(Commands.GetSeparator(index));
            }

            return separators;
        }
    }
}
