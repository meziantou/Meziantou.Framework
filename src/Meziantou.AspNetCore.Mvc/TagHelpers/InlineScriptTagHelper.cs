using System.Threading.Tasks;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Razor.TagHelpers;
using Microsoft.Extensions.Caching.Memory;

namespace Meziantou.AspNetCore.Mvc.TagHelpers;

public sealed class InlineScriptTagHelper : InlineTagHelper
{
    [HtmlAttributeName("src")]
    public string? Src { get; set; }

    public InlineScriptTagHelper(IWebHostEnvironment webHostEnvironment, IMemoryCache cache)
        : base(webHostEnvironment, cache)
    {
    }

    public override async Task ProcessAsync(TagHelperContext context, TagHelperOutput output)
    {
        var fileContent = await GetFileContentAsync(Src);
        if (fileContent == null)
        {
            output.SuppressOutput();
            return;
        }

        output.TagName = "script";
        output.Attributes.RemoveAll("src");
        output.TagMode = TagMode.StartTagAndEndTag;
        output.Content.AppendHtml(fileContent);
    }
}
