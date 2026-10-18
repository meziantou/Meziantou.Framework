using Meziantou.Framework.WebPlatform.Formatting;

namespace Meziantou.Framework.WebPlatform.Css.Om;

public sealed partial class CssStyleSheet
{
    /// <summary>Returns the text of the rules of the style sheet, formatted by <paramref name="formatter"/> (<see cref="CssFormatter.Default"/> when null).</summary>
    [NotInIdl]
    public string ToCss(CssFormatter? formatter = null) => (formatter ?? CssFormatter.Default).Format(this);

    /// <summary>Same as <see cref="ToCss(CssFormatter?)"/>, but writes the text to <paramref name="output"/> instead of building a string.</summary>
    [NotInIdl]
    public void ToCss(TextWriter output, CssFormatter? formatter = null) => (formatter ?? CssFormatter.Default).Format(this, output);
}
