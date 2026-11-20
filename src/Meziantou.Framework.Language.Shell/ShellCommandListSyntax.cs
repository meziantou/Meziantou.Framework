using System.Collections.Immutable;
namespace Meziantou.Framework.Language.Shell;

public sealed partial class ShellCommandListSyntax
{
    /// <summary>The operators joining the pipelines.</summary>
    public ImmutableArray<SyntaxToken> OperatorTokens
    {
        get
        {
            var separators = ImmutableArray.CreateBuilder<SyntaxToken>(Pipelines.SeparatorCount);
            for (var index = 0; index < Pipelines.SeparatorCount; index++)
            {
                separators.Add(Pipelines.GetSeparator(index));
            }

            return separators.MoveToImmutable();
        }
    }
}
