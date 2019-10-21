#nullable disable
using System;

namespace Meziantou.Framework.Templating
{
    public class CodeBlock : ParsedBlock
    {
        protected string EvalPrefixString { get; set; } = "=";

        public CodeBlock(Template template, string text, int index) : base(template, text, index)
        {
        }

        public override string BuildCode()
        {
            if (Text.StartsWith(EvalPrefixString, StringComparison.Ordinal))
            {
                return Template.OutputParameterName + ".Write(\"{0}\", " + Text.Substring(EvalPrefixString.Length) + ");";
            }

            return Text;
        }
    }
}
