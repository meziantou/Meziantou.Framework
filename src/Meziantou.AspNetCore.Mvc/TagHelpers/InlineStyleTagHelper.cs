using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Razor.TagHelpers;
using Microsoft.Extensions.Caching.Memory;

namespace Meziantou.AspNetCore.Mvc.TagHelpers
{
    public sealed class InlineStyleTagHelper : InlineTagHelper
    {
        [HtmlAttributeName("href")]
        public string? Href { get; set; }

        public InlineStyleTagHelper(IWebHostEnvironment webHostEnvironment, IMemoryCache cache)
            : base(webHostEnvironment, cache)
        {
        }

        public override async Task ProcessAsync(TagHelperContext context, TagHelperOutput output)
        {
            var fileContent = await GetFileContentAsync(Href);
            if (fileContent == null)
            {
                output.SuppressOutput();
                return;
            }

            output.TagName = "style";
            output.Attributes.RemoveAll("href");
            output.TagMode = TagMode.StartTagAndEndTag;
            output.Content.AppendHtml(fileContent);
        }
    }

}
