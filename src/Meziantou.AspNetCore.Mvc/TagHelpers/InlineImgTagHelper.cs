using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Razor.TagHelpers;
using Microsoft.AspNetCore.StaticFiles;
using Microsoft.Extensions.Caching.Memory;

namespace Meziantou.AspNetCore.Mvc.TagHelpers;

/// <summary>A TagHelper that inlines image files as Base64-encoded data URIs to reduce HTTP requests.</summary>
/// <example>
/// <code language="razor">
/// &lt;!-- Input --&gt;
/// &lt;inline-img src="logo.png" alt="Company Logo" /&gt;
/// 
/// &lt;!-- Output --&gt;
/// &lt;img src="data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAA..." alt="Company Logo" /&gt;
/// </code>
/// </example>
/// <remarks>
/// This TagHelper is useful for embedding small images directly in HTML to eliminate additional HTTP requests.
/// The image content is cached in memory and automatically invalidated when the source file changes.
/// </remarks>
public sealed class InlineImgTagHelper : InlineTagHelper
{
    private static readonly FileExtensionContentTypeProvider ContentTypeProvider = new();

    /// <summary>Gets or sets the path to the image file relative to the web root.</summary>
    [HtmlAttributeName("src")]
    public string? Src { get; set; }

    /// <summary>Initializes a new instance of the <see cref="InlineImgTagHelper"/> class.</summary>
    /// <param name="webHostEnvironment">The web host environment for accessing web root files.</param>
    /// <param name="cache">The memory cache for storing file contents.</param>
    public InlineImgTagHelper(IWebHostEnvironment webHostEnvironment, IMemoryCache cache)
   : base(webHostEnvironment, cache)
    {
    }

    /// <summary>Processes the tag helper by converting the image source to an inline Base64 data URI.</summary>
    /// <param name="context">Contains information about the current tag.</param>
    /// <param name="output">A stateful HTML element used to generate an HTML tag.</param>
    public override async Task ProcessAsync(TagHelperContext context, TagHelperOutput output)
    {
        var fileContent = await GetFileContentBase64Async(Src);
        if (fileContent is null)
        {
            output.SuppressOutput();
            return;
        }

        System.Diagnostics.Debug.Assert(Src is not null);
        if (!ContentTypeProvider.TryGetContentType(Src, out var contentType))
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

