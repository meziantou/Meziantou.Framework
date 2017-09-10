using System.Net;

namespace Meziantou.Framework.Templating
{
    public class HtmlEmailCodeBlock : CodeBlock
    {
        private const string BeginSectionPrefixString = "@begin section";
        private const string EndSectionPrefixString = "@end section";
        private const string HtmlEncodePrefixString = "#html ";
        private const string HtmlAttributeEncodePrefixString = "#attr ";
        private const string UrlEncodePrefixString = "#url ";
        private const string HtmlEncodedCodePrefixString = "html ";
        private const string CidPrefixString = "cid ";

        public HtmlEmailCodeBlock(Template template, string text, int index) : base(template, text, index)
        {
            EvalPrefixString = "#"; // Visual Studio colorizes "{{# Name }}" in HTML file in html file :)
        }

        protected virtual string HtmlDecode(string html)
        {
            if (html == null)
                return null;

            return WebUtility.HtmlDecode(html);
        }

        public override string BuildCode()
        {
            string text = Text.Trim();
            if (text.StartsWith(HtmlEncodedCodePrefixString))
            {
                string html = text.Substring(HtmlEncodedCodePrefixString.Length);
                return HtmlDecode(html);
            }

            if (text.StartsWith(BeginSectionPrefixString))
            {
                string sectionName = Nullify(text.Substring(BeginSectionPrefixString.Length));
                return Template.OutputParameterName + $".{nameof(HtmlEmailOutput.BeginSection)}(@\"{EscapeVerbatimString(sectionName)}\");";
            }

            if (text.StartsWith(EndSectionPrefixString))
            {
                string sectionName = Nullify(text.Substring(EndSectionPrefixString.Length));
                return Template.OutputParameterName + $".{nameof(HtmlEmailOutput.EndSection)}(@\"{EscapeVerbatimString(sectionName)}\");";
            }

            if (text.StartsWith(HtmlEncodePrefixString))
            {
                string html = Nullify(text.Substring(HtmlEncodePrefixString.Length));
                return Template.OutputParameterName + $".{nameof(HtmlEmailOutput.WriteHtmlEncode)}({html});";
            }

            if (text.StartsWith(HtmlAttributeEncodePrefixString))
            {
                string html = Nullify(text.Substring(HtmlAttributeEncodePrefixString.Length));
                return Template.OutputParameterName + $".{nameof(HtmlEmailOutput.WriteHtmlAttributeEncode)}({html});";
            }

            if (text.StartsWith(UrlEncodePrefixString))
            {
                string url = Nullify(text.Substring(UrlEncodePrefixString.Length));
                return Template.OutputParameterName + $".{nameof(HtmlEmailOutput.WriteUrlEncode)}({url});";
            }

            if (text.StartsWith(CidPrefixString))
            {
                string cid = Nullify(text.Substring(CidPrefixString.Length));
                return Template.OutputParameterName + $".{nameof(HtmlEmailOutput.WriteContentIdentifier)}(@\"{EscapeVerbatimString(cid)}\");";
            }

            return base.BuildCode();
        }

        private string Nullify(string text)
        {
            if (text == null)
                return null;

            text = text.Trim();
            return text == string.Empty ? null : text;
        }
    }
}