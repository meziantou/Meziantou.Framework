using System.Collections.Immutable;
namespace Meziantou.Framework.Language.Shell;

public sealed partial class ShellStatementListSyntax
{
    /// <summary>The separators between the statements.</summary>
    public ImmutableArray<SyntaxToken> SeparatorTokens
    {
        get
        {
            var separators = ImmutableArray.CreateBuilder<SyntaxToken>(Statements.SeparatorCount);
            for (var index = 0; index < Statements.SeparatorCount; index++)
            {
                separators.Add(Statements.GetSeparator(index));
            }

            return separators.MoveToImmutable();
        }
    }
}
