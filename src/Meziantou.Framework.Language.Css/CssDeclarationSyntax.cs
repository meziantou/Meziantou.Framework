namespace Meziantou.Framework.Language.Css;

/// <summary>Represents a declaration, such as <c>color: red !important;</c>.</summary>
/// <remarks>
/// <see cref="Values"/> holds the value without the whitespace and comments around it, which are trivia, and without
/// the <c>!important</c> that ends it, which is <see cref="Important"/>.
/// </remarks>
public sealed partial class CssDeclarationSyntax
{
    /// <summary>Gets the name of the property, with its escapes resolved, such as <c>color</c> or <c>--main-color</c>.</summary>
    public string Name => NameToken.ValueText;

    /// <summary>Determines whether the declaration sets a custom property, whose name starts with two dashes.</summary>
    public bool IsCustomProperty => SyntaxFacts.IsCustomPropertyName(Name);

    /// <summary>Determines whether the declaration ends with <c>!important</c>.</summary>
    public bool IsImportant => Important is not null;

    /// <summary>Gets the text of the value as written, from its first token to its last, comments and line breaks included.</summary>
    public string GetValueText()
    {
        if (Values.Count == 0)
            return "";

        var start = Values[0].SpanStart;
        var end = Values[Values.Count - 1].Span.End;
        return ToFullString().Substring(start - FullSpan.Start, end - start);
    }
}
