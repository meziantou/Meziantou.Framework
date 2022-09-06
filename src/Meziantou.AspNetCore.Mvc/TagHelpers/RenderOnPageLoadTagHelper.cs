using Microsoft.AspNetCore.Razor.TagHelpers;

namespace Meziantou.AspNetCore.Mvc.TagHelpers;

/// <seealso href="https://www.meziantou.net/loading-stylesheets-asynchronously-using-a-taghelper-in-asp-net-core.htm"/>
[HtmlTargetElement("render-on-page-load")]
public sealed class RenderOnPageLoadTagHelper : TagHelper
{
    [HtmlAttributeName("id")]
    public string? Id { get; set; }

    public override void Process(TagHelperContext context, TagHelperOutput output)
    {
        output.TagName = "noscript";

        var id = string.IsNullOrEmpty(Id) ? "render-onload" : Id;
        output.Attributes.Add("id", id);
        output.PostElement.AppendHtml("<script>var renderOnLoad=function(){var e=document.getElementById('" + id + "'),n=document.createElement('div');n.innerHTML=e.textContent,document.body.appendChild(n),e.parentElement.removeChild(e)},r=window.requestAnimationFrame;r?r(function(){window.setTimeout(renderOnLoad,0)}):window.addEventListener('load',renderOnLoad);</script>");
    }
}
