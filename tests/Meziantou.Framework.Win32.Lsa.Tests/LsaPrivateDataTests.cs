using Meziantou.Xunit;

namespace Meziantou.Framework.Win32.Lsa.Tests;

[Collection("LsaPrivateDataTests")]
public sealed class LsaPrivateDataTests
{
    [Fact, RunIf(WindowsGroups.Administrator)]
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

    [Fact, RunIf(WindowsGroups.Administrator)]
    public void LsaPrivateData_GetUnsetValue()
    {
        // Get
        var value = LsaPrivateData.GetValue("LsaPrivateDataTestsUnset");
        Assert.Null(value);
    }

    // LSA_UNICODE_STRING holds the length in bytes in a ushort, so 32767 characters is the last length that fits
    [Fact, RunIf(TestOperatingSystems.Windows)]
    public void SetValue_KeyLongerThanMaxLength_Throws()
    {
        var exception = Assert.Throws<ArgumentOutOfRangeException>(() => LsaPrivateData.SetValue(new string('a', 32768), "test"));
        Assert.Equal("key", exception.ParamName);
    }

    [Fact, RunIf(TestOperatingSystems.Windows)]
    public void SetValue_ValueLongerThanMaxLength_Throws()
    {
        var exception = Assert.Throws<ArgumentOutOfRangeException>(() => LsaPrivateData.SetValue("LsaPrivateDataTests", new string('a', 32768)));
        Assert.Equal("value", exception.ParamName);
    }

    [Fact, RunIf(TestOperatingSystems.Windows)]
    public void GetValue_KeyLongerThanMaxLength_Throws()
    {
        var exception = Assert.Throws<ArgumentOutOfRangeException>(() => LsaPrivateData.GetValue(new string('a', 32768)));
        Assert.Equal("key", exception.ParamName);
    }
}
