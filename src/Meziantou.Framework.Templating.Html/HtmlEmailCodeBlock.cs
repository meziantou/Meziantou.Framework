using System;
using System.Diagnostics.CodeAnalysis;
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

        public HtmlEmailCodeBlock(Template template, string text, int index)
            : base(template, text, index)
        {
            EvalPrefixString = "#"; // Visual Studio colorizes "{{# Name }}" in HTML file in html file :)
        }

        [return: NotNullIfNotNull(parameterName: "html")]
        protected virtual string? HtmlDecode(string? html)
        {
            if (html == null)
                return null;

            return WebUtility.HtmlDecode(html);
        }

        public override string BuildCode()
        {
            var text = Text.Trim();
            if (text.StartsWith(HtmlEncodedCodePrefixString, StringComparison.Ordinal))
            {
                var html = text[HtmlEncodedCodePrefixString.Length..];
                return HtmlDecode(html);
            }

            if (text.StartsWith(BeginSectionPrefixString, StringComparison.Ordinal))
            {
                var sectionName = Nullify(text[BeginSectionPrefixString.Length..]);
                return Template.OutputParameterName + $".{nameof(HtmlEmailOutput.BeginSection)}(@\"{EscapeVerbatimString(sectionName)}\");";
            }

            if (text.StartsWith(EndSectionPrefixString, StringComparison.Ordinal))
            {
                var sectionName = Nullify(text[EndSectionPrefixString.Length..]);
                return Template.OutputParameterName + $".{nameof(HtmlEmailOutput.EndSection)}(@\"{EscapeVerbatimString(sectionName)}\");";
            }

            if (text.StartsWith(HtmlEncodePrefixString, StringComparison.Ordinal))
            {
                var html = Nullify(text[HtmlEncodePrefixString.Length..]);
                return Template.OutputParameterName + $".{nameof(HtmlEmailOutput.WriteHtmlEncode)}({html});";
            }

            if (text.StartsWith(HtmlAttributeEncodePrefixString, StringComparison.Ordinal))
            {
                var html = Nullify(text[HtmlAttributeEncodePrefixString.Length..]);
                return Template.OutputParameterName + $".{nameof(HtmlEmailOutput.WriteHtmlAttributeEncode)}({html});";
            }

            if (text.StartsWith(UrlEncodePrefixString, StringComparison.Ordinal))
            {
                var url = Nullify(text[UrlEncodePrefixString.Length..]);
                return Template.OutputParameterName + $".{nameof(HtmlEmailOutput.WriteUrlEncode)}({url});";
            }

            if (text.StartsWith(CidPrefixString, StringComparison.Ordinal))
            {
                var cid = Nullify(text[CidPrefixString.Length..]);
                return Template.OutputParameterName + $".{nameof(HtmlEmailOutput.WriteContentIdentifier)}(@\"{EscapeVerbatimString(cid)}\");";
            }

            return base.BuildCode();
        }

        private static string? Nullify(string? text)
        {
            if (text == null)
                return null;

            text = text.Trim();
            return text.Length == 0 ? null : text;
        }
    }
}
