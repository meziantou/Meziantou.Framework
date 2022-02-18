using System.Text;

namespace Meziantou.Framework
{
    public static class ExceptionExtensions
    {
        public static string ToString(this Exception exception!!, bool includeInnerException)
        {
            if (!includeInnerException)
                return exception.ToString();

            var sb = new StringBuilder();
            var currentException = exception;
            while (currentException != null)
            {
                sb.Append(currentException).AppendLine();
                currentException = currentException.InnerException;
            }

            return sb.ToString();
        }
    }
}
