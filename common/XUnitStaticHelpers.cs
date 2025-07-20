using System.Diagnostics;

namespace TestUtilities;

internal static class XUnitStaticHelpers
{
    private const int DefaultRetryCount = 10;

    public static CancellationToken XunitCancellationToken => Xunit.TestContext.Current.CancellationToken;

    public static async Task Retry(Func<Task> action)
    {
        for (var i = DefaultRetryCount; i >= 0; i--)
        {
            try
            {
                await action().ConfigureAwait(false);
                return;
            }
            catch when (i > 0)
            {
                await Task.Delay(50, XunitCancellationToken).ConfigureAwait(false);
            }
        }

        throw new InvalidOperationException("unreachable");
    }

    public static async Task<T> Retry<T>(Func<Task<T>> action)
    {
        for (var i = DefaultRetryCount; i >= 0; i--)
        {
            try
            {
                return await action().ConfigureAwait(false);
            }
            catch when (i > 0)
            {
                await Task.Delay(50, XunitCancellationToken).ConfigureAwait(false);
            }
        }

        throw new InvalidOperationException("unreachable");
    }

    public static void Retry(Action action)
    {
        for (var i = DefaultRetryCount; i >= 0; i--)
        {
            try
            {
                action();
                return;
            }
            catch when (i > 0)
            {
                Thread.Sleep(50);
            }
        }
    }
}