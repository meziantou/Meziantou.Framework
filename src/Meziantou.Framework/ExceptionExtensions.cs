namespace Meziantou.Framework;

/// <summary>
/// Provides extension methods for <see cref="Exception"/>.
/// </summary>
/// <example>
/// <code>
/// try
/// {
///     throw new Exception("Outer", new Exception("Inner"));
/// }
/// catch (Exception ex)
/// {
///     string fullDetails = ex.ToString(includeInnerException: true);
/// }
/// </code>
/// </example>
public static class ExceptionExtensions
{
    /// <summary>Converts the exception to a string representation, optionally including all inner exceptions.</summary>
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
