namespace TestUtilities;

internal static class XUnitStaticHelpers
{
    public static CancellationToken XunitCancellationToken => Xunit.TestContext.Current.CancellationToken;
}