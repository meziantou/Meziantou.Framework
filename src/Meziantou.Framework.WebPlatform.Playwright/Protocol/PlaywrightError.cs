using System.Text.Json.Nodes;

namespace Meziantou.Framework.WebPlatform.Playwright.Protocol;

/// <summary>
/// An error returned to the client of a protocol call: its name is the type of the error of the client (e.g.
/// <c>TimeoutError</c> is a <c>TimeoutException</c> of the .NET client).
/// </summary>
#pragma warning disable CA1064 // The errors of the protocol are not exposed to the users of the library
internal sealed class PlaywrightError : Exception
#pragma warning restore CA1064
{
    public const string GenericName = "Error";

    public PlaywrightError(string message)
        : this(GenericName, message)
    {
    }

    public PlaywrightError(string name, string message, Exception? innerException = null)
        : base(message, innerException)
    {
        ErrorName = name;
    }

    public PlaywrightError()
        : this(GenericName, "Error")
    {
    }

    public PlaywrightError(string message, Exception? innerException)
        : this(GenericName, message, innerException)
    {
    }

    /// <summary>Gets the name of the error (<c>Error</c>, <c>TimeoutError</c>, <c>TargetClosedError</c>, <c>AbortError</c>).</summary>
    public string ErrorName { get; }

    /// <summary>Gets the details of the error (the <c>errorDetails</c> of the response, e.g. the received value of a failed assertion).</summary>
    public JsonObject? Details { get; init; }

    /// <summary>Gets the serialized value of a thrown value that is not an error, or null.</summary>
    public JsonNode? Value { get; init; }

    /// <summary>Gets the call stack of the error, or null.</summary>
    public string? ErrorStack { get; init; }

    public bool IsTimeout => ErrorName == "TimeoutError";

    /// <summary>Gets whether the operations that retry (e.g. the actions that wait for an element) fail at once with this error.</summary>
    public bool IsNonRetriable { get; init; }

    public static PlaywrightError Timeout(double timeout) => new("TimeoutError", $"Timeout {timeout.ToString(System.Globalization.CultureInfo.InvariantCulture)}ms exceeded.");

    public static PlaywrightError TargetClosed(string? reason = null) => new("TargetClosedError", reason ?? "Target page, context or browser has been closed");

    public static PlaywrightError Aborted(string? reason = null) => new("AbortError", string.IsNullOrEmpty(reason) ? "The operation was aborted" : reason);
}
