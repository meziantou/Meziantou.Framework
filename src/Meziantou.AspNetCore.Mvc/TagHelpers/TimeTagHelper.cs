using System.Globalization;
using Microsoft.AspNetCore.Razor.TagHelpers;

namespace Meziantou.AspNetCore.Mvc.TagHelpers;

[HtmlTargetElement(Attributes = "datetime")]
public sealed class TimeTagHelper : TagHelper
{
    public DateTimeOffset? Datetime { get; set; }

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
