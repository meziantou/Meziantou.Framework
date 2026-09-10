namespace Meziantou.Framework.Language.Shell;

/// <summary>Represents a <c>goto</c> statement.</summary>
public sealed partial class CmdGotoStatementSyntax
{
    /// <summary>The target label without any leading colon.</summary>
    public string Label => LabelToken.ValueText.TrimStart(':');
}
