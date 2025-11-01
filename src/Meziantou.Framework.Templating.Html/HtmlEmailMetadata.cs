namespace Meziantou.Framework.Templating;

/// <summary>Contains metadata extracted from an HTML email template, such as the title and content identifiers.</summary>
public class HtmlEmailMetadata
{
    /// <summary>Gets or sets the title extracted from the template's "title" section.</summary>
    public string? Title { get; set; }

    /// <summary>Gets or sets the list of content identifiers (CIDs) referenced in the template for embedded resources.</summary>
    public IList<string>? ContentIdentifiers { get; set; }
}
