namespace Meziantou.Framework.Language.Shell;

/// <summary>Represents a <c>while</c> or <c>until</c> loop.</summary>
public sealed partial class PosixWhileStatementSyntax
{
    /// <summary>Returns <see langword="true"/> for an <c>until</c> loop, which repeats while the condition fails.</summary>
    public bool IsUntil => Kind() == SyntaxKind.PosixUntilStatement;
}
