namespace Meziantou.Framework.Language.Shell;

/// <summary>Represents a number, a verbatim string, or a bare word used as a value.</summary>
public sealed partial class PowerShellLiteralExpressionSyntax
{
    /// <summary>The literal value with quoting resolved.</summary>
    public string Value => Token.ValueText;
}
