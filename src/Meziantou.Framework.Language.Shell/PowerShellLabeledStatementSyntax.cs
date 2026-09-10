namespace Meziantou.Framework.Language.Shell;

/// <summary>Represents a loop preceded by a label, <c>:outer while (...) { }</c>.</summary>
public sealed partial class PowerShellLabeledStatementSyntax
{
    /// <summary>The label without its leading colon.</summary>
    public string Label => LabelToken.Text.TrimStart(':');
}
