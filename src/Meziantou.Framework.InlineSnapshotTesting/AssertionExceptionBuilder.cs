namespace Meziantou.Framework.InlineSnapshotTesting;

public class AssertionExceptionBuilder
{
    internal static AssertionExceptionBuilder Default { get; } = new AssertionExceptionBuilder();

    public virtual Exception CreateException(string message)
    {
        // Try to find the current runner exception.
        // These exceptions are better formatted in the test output.
        var xunitAssertionType = Type.GetType("Xunit.Sdk.XunitException, xunit.assert");
        if (xunitAssertionType != null)
            return (Exception)Activator.CreateInstance(xunitAssertionType, message)!;

        var nunitAssertionType = Type.GetType("NUnit.Framework.AssertionException, nunit.framework");
        if (nunitAssertionType != null)
            return (Exception)Activator.CreateInstance(nunitAssertionType, message)!;

        var msTestV2AssertionType = Type.GetType("Microsoft.VisualStudio.TestTools.UnitTesting.AssertFailedException, Microsoft.VisualStudio.TestPlatform.TestFramework");
        if (msTestV2AssertionType != null)
            return (Exception)Activator.CreateInstance(msTestV2AssertionType, message)!;

        throw new InlineSnapshotAssertionException(message);
    }
}
