using Meziantou.Framework.WebPlatform.Formatting;

namespace Meziantou.Framework.WebPlatform.Css.Om;

public partial class CssStyleDeclaration
{
    /// <summary>
    /// Returns the text of the declarations, formatted by <paramref name="formatter"/> (<see cref="CssFormatter.Default"/>
    /// when null, which returns the same text as <see cref="CssText"/>).
    /// </summary>
    [NotInIdl]
    public string ToCss(CssFormatter? formatter = null) => (formatter ?? CssFormatter.Default).Format(this);

    /// <summary>Same as <see cref="ToCss(CssFormatter?)"/>, but writes the text to <paramref name="output"/> instead of building a string.</summary>
    [NotInIdl]
    public void ToCss(TextWriter output, CssFormatter? formatter = null) => (formatter ?? CssFormatter.Default).Format(this, output);
}
