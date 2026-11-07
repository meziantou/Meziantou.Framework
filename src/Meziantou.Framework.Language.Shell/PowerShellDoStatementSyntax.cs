namespace Meziantou.Framework.Language.Shell;

/// <summary>Represents a <c>do ... while</c> or <c>do ... until</c> loop.</summary>
public sealed partial class PowerShellDoStatementSyntax
{
    /// <summary>Returns <see langword="true"/> when the loop repeats until the condition becomes true.</summary>
    public bool IsUntil => string.Equals(ConditionKeyword.Text, "until", StringComparison.OrdinalIgnoreCase);
}
