namespace Meziantou.Framework.Language.Shell;

/// <summary>Represents an array assignment such as <c>files=(a b c)</c>.</summary>
public sealed partial class PosixArrayAssignmentSyntax
{
    public string Name => NameToken.ValueText;
}
