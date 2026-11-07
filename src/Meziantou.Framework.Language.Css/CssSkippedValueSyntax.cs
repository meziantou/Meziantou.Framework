namespace Meziantou.Framework.Language.Css;

/// <summary>Represents a function or a block nested beyond the depth limit, kept as its tokens.</summary>
public sealed partial class CssSkippedValueSyntax
{
    public string Text => ToFullString();
}
