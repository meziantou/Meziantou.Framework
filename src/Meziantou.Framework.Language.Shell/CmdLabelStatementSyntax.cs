namespace Meziantou.Framework.Language.Shell;

/// <summary>Represents a label, <c>:name</c>, which is a jump target for <c>goto</c> and <c>call</c>.</summary>
public sealed partial class CmdLabelStatementSyntax
{
    /// <summary>The label name without its leading colon.</summary>
    public string Name => NameToken.ValueText;
}
