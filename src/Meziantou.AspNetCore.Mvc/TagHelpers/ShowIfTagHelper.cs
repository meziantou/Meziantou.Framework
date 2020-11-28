using Microsoft.AspNetCore.Razor.TagHelpers;

namespace Meziantou.AspNetCore.Mvc.TagHelpers
{
    [HtmlTargetElement(Attributes = "show-if")]
    public sealed class ShowIfTagHelper : TagHelper
    {
        [HtmlAttributeName("show-if")]
        public bool Value { get; set; }

        public override void Process(TagHelperContext context, TagHelperOutput output)
        {
            if (Value)
            {
                output.SuppressOutput();
                return;
            }

            output.Attributes.RemoveAll("show-if");
        }
    }
}
