#nullable disable
using System;
using System.Globalization;

namespace Meziantou.Framework.Html
{
    public sealed class HtmlException : Exception
    {
        public HtmlException()
            : base("HTML0001: Html exception")
        {
        }

        public HtmlException(string message)
            : base(message)
        {
        }

        public HtmlException(string message, Exception innerException)
            : base(message, innerException)
        {
        }

        private HtmlException(System.Runtime.Serialization.SerializationInfo info, System.Runtime.Serialization.StreamingContext context)
            : base(info, context)
        {
        }

        public static int GetCode(string message)
        {
            if (message == null)
                return -1;

            const string prefix = "HTML";

            if (!message.StartsWith(prefix, StringComparison.Ordinal))
                return -1;

            var pos = message.IndexOf(':', prefix.Length);
            if (pos < 0)
                return -1;

            if (int.TryParse(message.Substring(prefix.Length, pos - prefix.Length), NumberStyles.None, CultureInfo.InvariantCulture, out var i))
                return i;

            return -1;
        }

        public int Code => GetCode(Message);
    }
}
