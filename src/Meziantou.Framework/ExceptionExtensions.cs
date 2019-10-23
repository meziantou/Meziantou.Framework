using System;
using System.Diagnostics.CodeAnalysis;
using System.Text;

namespace Meziantou.Framework
{
    public static class ExceptionExtensions
    {
        [return: NotNullIfNotNull(parameterName: "exception")]
        public static string? ToString(this Exception? exception, bool includeInnerException)
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
