using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Meziantou.Framework.Templating
{
    public class HtmlEmailTemplate : Template
    {
        public HtmlEmailTemplate()
        {
            StartCodeBlockDelimiter = "{{";
            EndCodeBlockDelimiter = "}}";
        }

        protected override CodeBlock CreateCodeBlock(string text, int index)
        {
            return new HtmlEmailCodeBlock(this, text, index);
        }

        protected override object CreateOutput(TextWriter writer)
        {
            return new HtmlEmailOutput(this, writer);
        }

        public virtual string Run(out HtmlEmailMetadata metadata, IDictionary<string, object> parameters)
        {
            if (parameters == null)
                throw new ArgumentNullException(nameof(parameters));

            using (var writer = new StringWriter())
            {
                Run(writer, out metadata, parameters);
                return writer.ToString();
            }
        }

        public virtual string Run(out HtmlEmailMetadata metadata, params object[] parameters)
        {
            if (parameters == null)
                throw new ArgumentNullException(nameof(parameters));

            using (var writer = new StringWriter())
            {
                Run(writer, out metadata, parameters);
                return writer.ToString();
            }
        }

        public virtual string Run(out HtmlEmailMetadata metadata)
        {
            using (var writer = new StringWriter())
            {
                Run(writer, out metadata);
                return writer.ToString();
            }
        }

        public virtual void Run(TextWriter writer, out HtmlEmailMetadata metadata, IReadOnlyDictionary<string, object> parameters)
        {
            if (writer == null)
                throw new ArgumentNullException(nameof(writer));
            if (parameters == null)
                throw new ArgumentNullException(nameof(parameters));

            var p = CreateMethodParameters(writer, parameters);
            InvokeRunMethod(p);
            metadata = GetMetadata(p);
        }

        public virtual void Run(TextWriter writer, out HtmlEmailMetadata metadata, params object[] parameters)
        {
            if (writer == null)
                throw new ArgumentNullException(nameof(writer));
            if (parameters == null)
                throw new ArgumentNullException(nameof(parameters));

            var p = CreateMethodParameters(writer, parameters);
            InvokeRunMethod(p);
            metadata = GetMetadata(p);
        }

        public virtual void Run(TextWriter writer, out HtmlEmailMetadata metadata)
        {
            if (writer == null)
                throw new ArgumentNullException(nameof(writer));

            var p = CreateMethodParameters(writer, (object[])null);
            InvokeRunMethod(p);
            metadata = GetMetadata(p);
        }

        private static HtmlEmailMetadata GetMetadata(object[] parameters)
        {
            var htmlEmailOutput = parameters.OfType<HtmlEmailOutput>().FirstOrDefault();
            if (htmlEmailOutput != null)
            {
                return new HtmlEmailMetadata
                {
                    Title = htmlEmailOutput.GetSection(HtmlEmailOutput.TitleSectionName),
                    ContentIdentifiers = htmlEmailOutput.ContentIdentifiers,
                };
            }

            return null;
        }
    }
}
