using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Razor.TagHelpers;
using Microsoft.Extensions.Caching.Memory;

namespace Meziantou.AspNetCore.Mvc.TagHelpers;

/// <summary>
/// A TagHelper that inlines JavaScript files into &lt;script&gt; elements to reduce HTTP requests.
/// </summary>
/// <example>
/// <code language="razor">
/// &lt;!-- Input --&gt;
/// &lt;inline-script src="app.js" /&gt;
/// 
/// &lt;!-- Output --&gt;
/// &lt;script&gt;
/// function init() {
///     console.log('App initialized');
/// }
/// /* ... JavaScript content ... */
/// &lt;/script&gt;
/// </code>
/// </example>
/// <remarks>
/// This TagHelper is useful for embedding small, critical JavaScript files directly in HTML
/// to eliminate additional HTTP requests and improve initial page load performance.
/// The script content is cached in memory and automatically invalidated when the source file changes.
/// </remarks>
public sealed class InlineScriptTagHelper : InlineTagHelper
{
    /// <summary>Gets or sets the path to the JavaScript file relative to the web root.</summary>
    [HtmlAttributeName("src")]
    public string? Src { get; set; }

    /// <summary>Initializes a new instance of the <see cref="InlineScriptTagHelper"/> class.</summary>
    /// <param name="webHostEnvironment">The web host environment for accessing web root files.</param>
    /// <param name="cache">The memory cache for storing file contents.</param>
    public InlineScriptTagHelper(IWebHostEnvironment webHostEnvironment, IMemoryCache cache)
     : base(webHostEnvironment, cache)
    {
    }

    /// <summary>Processes the tag helper by converting the external script reference to an inline &lt;script&gt; element.</summary>
    /// <param name="context">Contains information about the current tag.</param>
    /// <param name="output">A stateful HTML element used to generate an HTML tag.</param>
    public override async Task ProcessAsync(TagHelperContext context, TagHelperOutput output)
    {
        var fileContent = await GetFileContentAsync(Src);
        if (fileContent is null)
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

