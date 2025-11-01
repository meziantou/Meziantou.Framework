using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Razor.TagHelpers;
using Microsoft.Extensions.Caching.Memory;

namespace Meziantou.AspNetCore.Mvc.TagHelpers;

/// <summary>
/// A TagHelper that inlines CSS files into &lt;style&gt; elements to reduce HTTP requests.
/// </summary>
/// <example>
/// <code language="razor">
/// &lt;!-- Input --&gt;
/// &lt;inline-style href="site.css" /&gt;
/// 
/// &lt;!-- Output --&gt;
/// &lt;style&gt;
/// body { margin: 0; padding: 0; }
/// /* ... CSS content ... */
/// &lt;/style&gt;
/// </code>
/// </example>
/// <remarks>
/// This TagHelper is particularly useful for critical CSS optimization, where you want to inline
/// above-the-fold styles to improve initial page render performance. The CSS content is cached
/// in memory and automatically invalidated when the source file changes.
/// </remarks>
public sealed class InlineStyleTagHelper : InlineTagHelper
{
    /// <summary>Gets or sets the path to the CSS file relative to the web root.</summary>
    [HtmlAttributeName("href")]
    public string? Href { get; set; }

    /// <summary>Initializes a new instance of the <see cref="InlineStyleTagHelper"/> class.</summary>
    /// <param name="webHostEnvironment">The web host environment for accessing web root files.</param>
    /// <param name="cache">The memory cache for storing file contents.</param>
    public InlineStyleTagHelper(IWebHostEnvironment webHostEnvironment, IMemoryCache cache)
        : base(webHostEnvironment, cache)
    {
    }

    /// <summary>Processes the tag helper by converting the external CSS reference to an inline &lt;style&gt; element.</summary>
    /// <param name="context">Contains information about the current tag.</param>
    /// <param name="output">A stateful HTML element used to generate an HTML tag.</param>
    public override async Task ProcessAsync(TagHelperContext context, TagHelperOutput output)
    {
        var fileContent = await GetFileContentAsync(Href);
        if (fileContent is null)
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

