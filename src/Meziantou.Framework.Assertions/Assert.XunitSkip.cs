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
}
