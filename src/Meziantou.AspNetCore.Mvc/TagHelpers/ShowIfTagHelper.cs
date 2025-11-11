using Microsoft.AspNetCore.Razor.TagHelpers;

namespace Meziantou.AspNetCore.Mvc.TagHelpers;

/// <summary>
/// A TagHelper that conditionally renders elements based on a boolean expression.
/// </summary>
/// <example>
/// <code language="razor">
/// @* Show content only if user is authenticated *@
/// &lt;div show-if="@User.Identity.IsAuthenticated"&gt;
///   Welcome back, @User.Identity.Name!
/// &lt;/div&gt;
/// 
/// @* Show error message conditionally *@
/// &lt;div class="alert alert-danger" show-if="@Model.HasErrors"&gt;
///     Please correct the errors below.
/// &lt;/div&gt;
/// </code>
/// </example>
[HtmlTargetElement(Attributes = "show-if")]
public sealed class ShowIfTagHelper : TagHelper
{
    /// <summary>Gets or sets the condition that determines whether the element should be rendered.</summary>
    /// <value>When <see langword="true"/>, the element is rendered; when <see langword="false"/>, it is suppressed.</value>
    [HtmlAttributeName("show-if")]
    public bool Value { get; set; }

    /// <summary>Processes the tag helper by either rendering or suppressing the element based on the condition.</summary>
    /// <param name="context">Contains information about the current tag.</param>
    /// <param name="output">A stateful HTML element used to generate an HTML tag.</param>
    public override void Process(TagHelperContext context, TagHelperOutput output)
    {
        if (!Value)
        {
            output.SuppressOutput();
            return;
        }

        output.Attributes.RemoveAll("show-if");
    }
}
