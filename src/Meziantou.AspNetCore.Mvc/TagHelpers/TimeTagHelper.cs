using Microsoft.AspNetCore.Razor.TagHelpers;

namespace Meziantou.AspNetCore.Mvc.TagHelpers;

/// <summary>Converts a <see cref="DateTimeOffset"/> value to a properly formatted datetime attribute for HTML time elements.</summary>
/// <example>
/// <code language="razor">
/// &lt;time datetime="@DateTime.Now"&gt;Just now&lt;/time&gt;
/// &lt;!-- Outputs: &lt;time datetime="2024-01-15T10:30:45.123"&gt;Just now&lt;/time&gt; --&gt;
/// </code>
/// </example>
[HtmlTargetElement(Attributes = "datetime")]
public sealed class TimeTagHelper : TagHelper
{
    /// <summary>Gets or sets the date and time to format for the datetime attribute.</summary>
    public DateTimeOffset? Datetime { get; set; }

    /// <summary>Processes the tag helper and sets the datetime attribute in ISO 8601 format.</summary>
    public override void Process(TagHelperContext context, TagHelperOutput output)
    {
        if (Datetime.HasValue)
        {
            output.Attributes.SetAttribute("datetime", Datetime?.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ss.fff", CultureInfo.InvariantCulture));
        }
        else
        {
            output.Attributes.RemoveAll("datetime");
        }
    }
}
