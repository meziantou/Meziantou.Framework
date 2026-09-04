using Microsoft.AspNetCore.Razor.TagHelpers;

namespace Meziantou.AspNetCore.Mvc.TagHelpers;

/// <summary>Converts a <see cref="DateTimeOffset"/> value to a properly formatted <c>datetime</c> attribute for HTML elements.</summary>
/// <example>
/// <code language="razor">
/// &lt;time datetime-value="@DateTimeOffset.Now"&gt;Just now&lt;/time&gt;
/// &lt;!-- Outputs: &lt;time datetime="2024-01-15T10:30:45.123Z"&gt;Just now&lt;/time&gt; --&gt;
/// </code>
/// </example>
/// <remarks>
/// The value is normalized to UTC and always written with the <c>Z</c> designator. A <c>datetime</c> attribute
/// without a time zone designator is interpreted as local time by the reader, which would shift the value by
/// the reader's UTC offset.
/// </remarks>
[HtmlTargetElement("time", Attributes = DatetimeValueAttributeName)]
[HtmlTargetElement("del", Attributes = DatetimeValueAttributeName)]
[HtmlTargetElement("ins", Attributes = DatetimeValueAttributeName)]
public sealed class TimeTagHelper : TagHelper
{
    private const string DatetimeValueAttributeName = "datetime-value";

    /// <summary>Gets or sets the date and time to format for the <c>datetime</c> attribute.</summary>
    [HtmlAttributeName(DatetimeValueAttributeName)]
    public DateTimeOffset? Datetime { get; set; }

    /// <summary>Processes the tag helper and sets the <c>datetime</c> attribute in ISO 8601 format.</summary>
    public override void Process(TagHelperContext context, TagHelperOutput output)
    {
        if (Datetime.HasValue)
        {
            output.Attributes.SetAttribute("datetime", Datetime.Value.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ss.fff'Z'", CultureInfo.InvariantCulture));
        }
        else
        {
            output.Attributes.RemoveAll("datetime");
        }
    }
}
