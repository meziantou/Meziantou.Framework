using TestUtilities;

namespace Meziantou.Framework.Win32.Lsa.Tests;

[Collection("LsaPrivateDataTests")]
public sealed class LsaPrivateDataTests
{
    [Fact, RunIfWindowsAdministrator]
    public void LsaPrivateData_SetGetRemove()
    {
        // The project is multi-targeted, so multiple process can run in parallel
        using var mutex = new Mutex(initiallyOwned: false, "MeziantouFrameworkLsaTests");
        mutex.WaitOne();
        try
        {
            // Set
            LsaPrivateData.SetValue("LsaPrivateDataTests", "test");

            // Get
            var value = LsaPrivateData.GetValue("LsaPrivateDataTests");
            Assert.Equal("test", value);

            // Remove
            LsaPrivateData.RemoveValue("LsaPrivateDataTests");
            value = LsaPrivateData.GetValue("LsaPrivateDataTests");
            Assert.Null(value);
        }
        finally
        {
            mutex.ReleaseMutex();
        }
    }

    [Fact, RunIfWindowsAdministrator]
    public void LsaPrivateData_GetUnsetValue()
    {
        // Get
        var value = LsaPrivateData.GetValue("LsaPrivateDataTestsUnset");
        Assert.Null(value);
    }
}
