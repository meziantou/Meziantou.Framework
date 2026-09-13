namespace Meziantou.Framework.Language.Shell;

/// <summary>
/// Represents a compound command followed by redirections, such as <c>{ ...; } &gt; out</c> or
/// <c>while read line; do ...; done &lt; file</c>.
/// </summary>
/// <remarks>
/// A simple command keeps its redirections among its own elements. A compound command has no such slot, so the
/// redirections that follow it wrap it instead, and they apply to the whole of <see cref="Statement"/>.
/// </remarks>
public sealed partial class PosixRedirectedStatementSyntax
{
}
