using System;
using System.Globalization;

namespace Meziantou.Framework.Html
{
    public class HtmlException: Exception
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

        public static int GetCode(string message)
        {
            if (message == null)
                return -1;

            const string prefix = "HTML";

            if (!message.StartsWith(prefix, StringComparison.Ordinal))
                return -1;

            int pos = message.IndexOf(':', prefix.Length);
            if (pos < 0)
                return -1;

            if (int.TryParse(message.Substring(prefix.Length, pos - prefix.Length), NumberStyles.None, CultureInfo.InvariantCulture, out int i))
                return i;

            return -1;
        }

        public int Code
        {
            get
            {
                return GetCode(Message);
            }
        }
    }
}
