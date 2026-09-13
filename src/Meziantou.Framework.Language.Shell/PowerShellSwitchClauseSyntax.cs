using System.ComponentModel;

namespace Meziantou.Framework.Language.Shell;

/// <summary>Represents one clause of a <c>switch</c> statement: a pattern and the script block it runs.</summary>
public sealed partial class PowerShellSwitchClauseSyntax
{
    /// <summary>The signature of version 3.0.0, from before <see cref="SeparatorToken"/> was added; the separator is kept.</summary>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public PowerShellSwitchClauseSyntax Update(ShellSyntaxNode pattern, PowerShellScriptBlockSyntax body)
        => Update(pattern, body, SeparatorToken);
}
