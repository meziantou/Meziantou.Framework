using System.Collections.Immutable;
namespace Meziantou.Framework.Language.Shell;

public sealed partial class ShellPipelineSyntax
{
    /// <summary>The operators joining the commands.</summary>
    public ImmutableArray<SyntaxToken> OperatorTokens
    {
        get
        {
            var separators = ImmutableArray.CreateBuilder<SyntaxToken>(Commands.SeparatorCount);
            for (var index = 0; index < Commands.SeparatorCount; index++)
            {
                separators.Add(Commands.GetSeparator(index));
            }

            return separators.MoveToImmutable();
        }
    }
}
