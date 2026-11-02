namespace Meziantou.Framework;

/// <summary>
/// Provides extension methods for <see cref="Exception"/>.
/// </summary>
public static class ExceptionExtensions
{
    /// <summary>Converts the exception to a string representation, optionally including all inner exceptions.</summary>
    /// <param name="exception">The exception to convert.</param>
    /// <param name="includeInnerException">
    /// <see langword="true"/> to return <see cref="Exception.ToString"/>, which includes the inner exceptions;
    /// <see langword="false"/> to return only the type, message and stack trace of <paramref name="exception"/>.
    /// </param>
    public static string ToString(this Exception exception, bool includeInnerException)
    {
        ArgumentNullException.ThrowIfNull(exception);

        if (includeInnerException)
            return exception.ToString();

        var sb = new StringBuilder();
        sb.Append(exception.GetType());

        var message = exception.Message;
        if (!string.IsNullOrEmpty(message))
            sb.Append(": ").Append(message);

        var stackTrace = exception.StackTrace;
        if (stackTrace is not null)
            sb.AppendLine().Append(stackTrace);

        return sb.ToString();
    }
}
