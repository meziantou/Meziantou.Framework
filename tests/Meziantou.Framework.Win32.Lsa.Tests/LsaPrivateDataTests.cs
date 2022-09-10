using FluentAssertions;
using TestUtilities;
using Xunit;

namespace Meziantou.Framework.Win32.Lsa.Tests;

[Collection("LsaPrivateDataTests")]
public sealed class LsaPrivateDataTests
{
    [RunIfWindowsAdministratorFact]
    public void LsaPrivateData_SetGetRemove()
    {
        // Set
        LsaPrivateData.SetValue("LsaPrivateDataTests", "test");

        // Get
        var value = LsaPrivateData.GetValue("LsaPrivateDataTests");
        value.Should().Be("test");

        // Remove
        LsaPrivateData.RemoveValue("LsaPrivateDataTests");
        value = LsaPrivateData.GetValue("LsaPrivateDataTests");
        value.Should().BeNull();
    }

    [RunIfWindowsAdministratorFact]
    public void LsaPrivateData_GetUnsetValue()
    {
        // Get
        var value = LsaPrivateData.GetValue("LsaPrivateDataTestsUnset");
        value.Should().BeNull();
    }
}
