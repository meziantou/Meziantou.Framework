using Meziantou.Xunit;

namespace Meziantou.Framework.Win32.Lsa.Tests;

[Collection("LsaPrivateDataTests")]
public sealed class LsaPrivateDataTests
{
    [Fact, RunIf(WindowsGroups.Administrator)]
    public void LsaPrivateData_SetGetRemove()
    {
        WithLsaLock(() =>
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
        });
    }

    [Fact, RunIf(WindowsGroups.Administrator)]
    public void LsaPrivateData_GetUnsetValue()
    {
        // Get
        var value = LsaPrivateData.GetValue("LsaPrivateDataTestsUnset");
        Assert.Null(value);
    }

    [Fact, RunIf(WindowsGroups.Administrator)]
    public void LsaPrivateData_SetGetRemoveNonAsciiValue()
    {
        WithLsaLock(() =>
        {
            LsaPrivateData.SetValue("LsaPrivateDataTestsNonAscii", "\u00e9\u4e2d\u6587\ud83d\ude00");

            var value = LsaPrivateData.GetValue("LsaPrivateDataTestsNonAscii");
            Assert.Equal("\u00e9\u4e2d\u6587\ud83d\ude00", value);

            LsaPrivateData.RemoveValue("LsaPrivateDataTestsNonAscii");
            Assert.Null(LsaPrivateData.GetValue("LsaPrivateDataTestsNonAscii"));
        });
    }

    [Fact, RunIf(TestOperatingSystems.Windows)]
    public void SetValue_NullKey_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => LsaPrivateData.SetValue(key: null!, "test"));
    }

    [Fact, RunIf(TestOperatingSystems.Windows)]
    public void GetValue_NullKey_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => LsaPrivateData.GetValue(key: null!));
    }

    [Fact, RunIf(TestOperatingSystems.Windows)]
    public void RemoveValue_NullKey_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => LsaPrivateData.RemoveValue(key: null!));
    }

    [Fact, RunIf(TestOperatingSystems.Windows)]
    public void SetValue_EmptyKey_Throws()
    {
        Assert.Throws<ArgumentException>(() => LsaPrivateData.SetValue("", "test"));
    }

    [Fact, RunIf(TestOperatingSystems.Windows)]
    public void GetValue_EmptyKey_Throws()
    {
        Assert.Throws<ArgumentException>(() => LsaPrivateData.GetValue(""));
    }

    [Fact, RunIf(TestOperatingSystems.Windows)]
    public void RemoveValue_EmptyKey_Throws()
    {
        Assert.Throws<ArgumentException>(() => LsaPrivateData.RemoveValue(""));
    }

    private static void WithLsaLock(Action action)
    {
        // The project is multi-targeted, so multiple process can run in parallel
        using var mutex = new Mutex(initiallyOwned: false, "MeziantouFrameworkLsaTests");
        mutex.WaitOne();
        try
        {
            action();
        }
        finally
        {
            mutex.ReleaseMutex();
        }
    }
}
