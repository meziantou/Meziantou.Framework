namespace Meziantou.Framework.Language.Shell.Syntax.InternalSyntax;

/// <remarks>
/// A separator that follows nothing produces this, and it has no text of its own -- only the separator does. That
/// makes it the one node with no slots, so it writes nothing rather than walking children it does not have.
/// </remarks>
internal sealed partial class ShellEmptyStatementSyntax
{
    protected internal override void WriteTo(TextWriter writer, bool leading, bool trailing)
    {
    }
}
