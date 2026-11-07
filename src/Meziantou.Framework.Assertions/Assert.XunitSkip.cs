namespace Meziantou.Framework.Assertions;

public partial class Assert
{
    // xunit considers an exception to be a skip request when its message starts with this token.
    // Everything after the token is used as the skip reason.
    private const string XunitDynamicSkipToken = "$XunitDynamicSkip$";

    /// <summary>
    /// Skips the currently running xunit test by throwing an <see cref="AssertionException"/> whose message uses the format expected by xunit for dynamically skipped tests.
    /// </summary>
    /// <param name="reason">The reason why the test is skipped.</param>
    [DoesNotReturn]
    public static void XunitSkip(string reason)
    {
        throw new AssertionException(XunitDynamicSkipToken + reason);
    }

    // xunit only inspects the message of the exception that escapes the test, whatever its type (Xunit.Assert.Skip
    // throws its own exception type). Assertions that wrap exceptions thrown by user code must let these propagate
    // unchanged, otherwise the skip request turns into a test failure.
    private static bool IsXunitSkipException(Exception exception)
    {
        return exception.Message.StartsWith(XunitDynamicSkipToken, StringComparison.Ordinal);
    }
}
