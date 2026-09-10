namespace Meziantou.Framework.Language.Shell;

/// <summary>Represents invalid or unrecognized shell text retained in the concrete syntax tree.</summary>
public sealed partial class ShellSkippedTextSyntax
{
    public string Text => ToFullString();
}
