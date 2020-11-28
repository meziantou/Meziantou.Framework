using System.Threading.Tasks;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Razor.TagHelpers;
using Microsoft.AspNetCore.StaticFiles;
using Microsoft.Extensions.Caching.Memory;

namespace Meziantou.AspNetCore.Mvc.TagHelpers
{
    public sealed class InlineImgTagHelper : InlineTagHelper
    {
        private static readonly FileExtensionContentTypeProvider s_contentTypeProvider = new FileExtensionContentTypeProvider();

        [HtmlAttributeName("src")]
        public string? Src { get; set; }

        public InlineImgTagHelper(IWebHostEnvironment webHostEnvironment, IMemoryCache cache)
            : base(webHostEnvironment, cache)
        {
        }

        public override async Task ProcessAsync(TagHelperContext context, TagHelperOutput output)
        {
            var fileContent = await GetFileContentBase64Async(Src);
            if (fileContent == null)
            {
                output.SuppressOutput();
                return;
            }

            if (!s_contentTypeProvider.TryGetContentType(Src, out var contentType))
            {
                contentType = "application/octet-stream";
            }

            output.TagName = "img";
            var srcAttribute = $"data:{contentType};base64,{fileContent}";

            output.Attributes.RemoveAll("src");
            output.Attributes.Add("src", srcAttribute);
            output.TagMode = TagMode.SelfClosing;
            output.Content.AppendHtml(fileContent);
        }
    }

}
