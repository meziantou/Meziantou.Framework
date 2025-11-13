using Microsoft.AspNetCore.Razor.TagHelpers;

namespace Meziantou.AspNetCore.Mvc.TagHelpers;

/// <summary>A TagHelper that defers rendering of content until after the page has loaded, improving perceived performance.</summary>
/// <example>
/// <code language="razor">
/// &lt;!-- Defer loading of non-critical stylesheet --&gt;
/// &lt;render-on-page-load&gt;
///     &lt;link rel="stylesheet" href="/css/non-critical.css" /&gt;
/// &lt;/render-on-page-load&gt;
/// 
/// &lt;!-- Defer loading with custom ID --&gt;
/// &lt;render-on-page-load id="analytics-scripts"&gt;
///     &lt;script src="/js/analytics.js"&gt;&lt;/script&gt;
/// &lt;/render-on-page-load&gt;
/// </code>
/// </example>
/// <remarks>
/// This TagHelper wraps content in a &lt;noscript&gt; element and injects JavaScript that renders
/// the content after the page loads. This technique is particularly useful for non-critical resources
/// like analytics scripts or supplementary stylesheets that shouldn't block initial page rendering.
/// For more details, see <see href="https://www.meziantou.net/loading-stylesheets-asynchronously-using-a-taghelper-in-asp-net-core.htm"/>.
/// </remarks>
[HtmlTargetElement("render-on-page-load")]
public sealed class RenderOnPageLoadTagHelper : TagHelper
{
    /// <summary>Gets or sets the unique identifier for the noscript element. Defaults to "render-onload" if not specified.</summary>
    [HtmlAttributeName("id")]
    public string? Id { get; set; }

    /// <summary>Processes the tag helper by wrapping content in a noscript element and adding deferred rendering script.</summary>
    /// <param name="context">Contains information about the current tag.</param>
    /// <param name="output">A stateful HTML element used to generate an HTML tag.</param>
    public override void Process(TagHelperContext context, TagHelperOutput output)
    {
        output.TagName = "noscript";

        var id = string.IsNullOrEmpty(Id) ? "render-onload" : Id;
        output.Attributes.Add("id", id);
        output.PostElement.AppendHtml("<script>var renderOnLoad=function(){var e=document.getElementById('" + id + "'),n=document.createElement('div');n.innerHTML=e.textContent,document.body.appendChild(n),e.parentElement.removeChild(e)},r=window.requestAnimationFrame;r?r(function(){window.setTimeout(renderOnLoad,0)}):window.addEventListener('load',renderOnLoad);</script>");
    }
}
