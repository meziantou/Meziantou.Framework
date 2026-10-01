namespace Meziantou.Framework.Language.Css;

/// <summary>Represents text the parser kept without using, such as the content of a block nested beyond the depth limit.</summary>
public sealed partial class CssSkippedTextSyntax
{
    public string Text => ToFullString();
}
