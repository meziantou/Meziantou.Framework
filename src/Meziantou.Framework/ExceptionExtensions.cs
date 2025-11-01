namespace Meziantou.Framework;

/// <summary>
/// Provides extension methods for <see cref="Exception"/>.
/// </summary>
public static class ExceptionExtensions
{
    /// <summary>
    /// Converts the exception to a string representation, optionally including all inner exceptions.
    /// </summary>
    /// <param name="exception">The exception to convert.</param>
    /// <param name="includeInnerException">If <see langword="true"/>, includes all inner exceptions in the output; otherwise, uses the default <see cref="Exception.ToString"/> behavior.</param>
    /// <returns>A string representation of the exception and its inner exceptions (if requested).</returns>
    public static string ToString(this Exception exception, bool includeInnerException)
    {
        ArgumentNullException.ThrowIfNull(exception);

        if (!includeInnerException)
            return exception.ToString();

        var sb = new StringBuilder();
        var currentException = exception;
        while (currentException is not null)
        {
            sb.Append(currentException).AppendLine();
            currentException = currentException.InnerException;
        }

        return sb.ToString();
    }
}
