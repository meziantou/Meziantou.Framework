using System.Globalization;
using Meziantou.Framework.WebPlatform.Css.Om;

namespace Meziantou.Framework.WebPlatform.Formatting;

/// <summary>
/// Converts a style sheet, a rule or a declaration block to CSS text. <see cref="Default"/> uses the CSSOM serialization
/// (https://drafts.csswg.org/cssom/#serialize-a-css-rule); <see cref="CssMinifyFormatter"/> and
/// <see cref="CssPrettyFormatter"/> produce a minified or an indented output.
/// </summary>
/// <remarks>
/// <para>
/// The object model is formatted, not the text it was parsed from: as in the browsers, the comments, the invalid rules and
/// the declarations of unsupported properties are not in the object model, and the values are written in their serialized
/// form (e.g. <c>#f00</c> is written <c>rgb(255, 0, 0)</c>).
/// </para>
/// <para>
/// A formatter is immutable and can be used concurrently: each call creates a <see cref="CssWriter"/> that holds the state of
/// the conversion. To customize the output, derive from <see cref="CssWriter"/> and return it from <see cref="CreateWriter"/>.
/// </para>
/// </remarks>
public abstract class CssFormatter
{
    /// <summary>
    /// The formatter that uses the CSSOM serialization: the output is the same as <see cref="CssRule.CssText"/> and
    /// <see cref="CssStyleDeclaration.CssText"/>; the rules of a style sheet are separated by a line feed.
    /// </summary>
    public static CssFormatter Default { get; } = new DefaultCssFormatter();

    /// <summary>Returns the text of the rules of <paramref name="styleSheet"/> (see <see cref="CssWriter.Write(CssStyleSheet)"/>).</summary>
    public string Format(CssStyleSheet styleSheet)
    {
        using var writer = new StringWriter(CultureInfo.InvariantCulture);
        Format(styleSheet, writer);
        return writer.ToString();
    }

    /// <summary>Writes the text of the rules of <paramref name="styleSheet"/> to <paramref name="output"/> (see <see cref="CssWriter.Write(CssStyleSheet)"/>).</summary>
    public void Format(CssStyleSheet styleSheet, TextWriter output)
    {
        ArgumentNullException.ThrowIfNull(styleSheet);
        ArgumentNullException.ThrowIfNull(output);
        CreateWriter(output).Write(styleSheet);
    }

    /// <summary>Returns the text of <paramref name="rule"/> (see <see cref="CssWriter.Write(CssRule)"/>).</summary>
    public string Format(CssRule rule)
    {
        using var writer = new StringWriter(CultureInfo.InvariantCulture);
        Format(rule, writer);
        return writer.ToString();
    }

    /// <summary>Writes the text of <paramref name="rule"/> to <paramref name="output"/> (see <see cref="CssWriter.Write(CssRule)"/>).</summary>
    public void Format(CssRule rule, TextWriter output)
    {
        ArgumentNullException.ThrowIfNull(rule);
        ArgumentNullException.ThrowIfNull(output);
        CreateWriter(output).Write(rule);
    }

    /// <summary>Returns the text of the declarations of <paramref name="declarations"/> (see <see cref="CssWriter.Write(CssStyleDeclaration)"/>).</summary>
    public string Format(CssStyleDeclaration declarations)
    {
        using var writer = new StringWriter(CultureInfo.InvariantCulture);
        Format(declarations, writer);
        return writer.ToString();
    }

    /// <summary>Writes the text of the declarations of <paramref name="declarations"/> to <paramref name="output"/> (see <see cref="CssWriter.Write(CssStyleDeclaration)"/>).</summary>
    public void Format(CssStyleDeclaration declarations, TextWriter output)
    {
        ArgumentNullException.ThrowIfNull(declarations);
        ArgumentNullException.ThrowIfNull(output);
        CreateWriter(output).Write(declarations);
    }

    /// <summary>Creates the writer that converts one style sheet, rule or declaration block to text.</summary>
    protected abstract CssWriter CreateWriter(TextWriter output);

    private sealed class DefaultCssFormatter : CssFormatter
    {
        protected override CssWriter CreateWriter(TextWriter output) => new(output);
    }
}
