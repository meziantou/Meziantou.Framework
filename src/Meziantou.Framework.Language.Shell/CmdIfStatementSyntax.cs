namespace Meziantou.Framework.Language.Shell;

/// <summary>Represents an <c>if</c> statement in its <c>errorlevel</c>, <c>defined</c>, <c>exist</c>, or comparison form.</summary>
public sealed partial class CmdIfStatementSyntax
{
    /// <summary>Returns <see langword="true"/> when the comparison ignores case.</summary>
    public bool IsCaseInsensitive => CaseInsensitiveToken.IsPresent();

    /// <summary>Returns <see langword="true"/> when the condition is negated.</summary>
    public bool IsNegated => NotKeyword.IsPresent();
}
