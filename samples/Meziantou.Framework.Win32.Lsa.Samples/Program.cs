using Meziantou.Framework.Win32;

// The readme examples (src/Meziantou.Framework.Win32.Lsa/readme.md) change the LSA private data of the machine and
// require administrator rights, so they are compiled in CI but not run. Run them from an elevated prompt with:
// dotnet run --project samples/Meziantou.Framework.Win32.Lsa.Samples
SetValue();
GetValue();
RemoveValue();

static void SetValue()
{
    #region LsaPrivateData_SetValue
    // Requires administrator privileges
    LsaPrivateData.SetValue("L$MySecretKey", "MySecretValue");
    #endregion
}

static void GetValue()
{
    #region LsaPrivateData_GetValue
    var value = LsaPrivateData.GetValue("L$MySecretKey");
    if (value is not null)
    {
        Console.WriteLine($"Retrieved value: {value}");
    }
    else
    {
        Console.WriteLine("Key not found");
    }
    #endregion
}

static void RemoveValue()
{
    #region LsaPrivateData_RemoveValue
    // Requires administrator privileges
    LsaPrivateData.RemoveValue("L$MySecretKey");
    #endregion
}
