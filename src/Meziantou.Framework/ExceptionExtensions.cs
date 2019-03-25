using System;
using System.Text;

namespace Meziantou.Framework
{
    public static class ExceptionExtensions
    {
        public static string ToString(this Exception exception, bool includeInnerException)
        {
            if (exception == null)
                return null;

            if (!includeInnerException)
                return exception.ToString();

            var sb = new StringBuilder();
            while (exception != null)
            {
                sb.Append(exception).AppendLine();
                exception = exception.InnerException;
            }

            return sb.ToString();
        }
    }
}
