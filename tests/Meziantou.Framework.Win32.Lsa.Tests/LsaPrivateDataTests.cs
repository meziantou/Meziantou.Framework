using System.Security.Principal;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Meziantou.Framework.Win32.Lsa.Tests
{
    [TestClass]
    public class LsaPrivateDataTests
    {
        [TestMethod]
        public void LsaPrivateData_SetGetRemove()
        {
            var identity = WindowsIdentity.GetCurrent();
            var principal = new WindowsPrincipal(identity);
            if (!principal.IsInRole(WindowsBuiltInRole.Administrator))
            {
                Assert.Inconclusive("Current user is not in the administator group");
            }

            // Set
            LsaPrivateData.SetValue("LsaPrivateDataTests", "test");

            // Get
            var value = LsaPrivateData.GetValue("LsaPrivateDataTests");
            Assert.AreEqual("test", value);

            // Remove
            LsaPrivateData.RemoveValue("LsaPrivateDataTests");
            value = LsaPrivateData.GetValue("LsaPrivateDataTests");
            Assert.AreEqual("", value);
        }

        [TestMethod]
        public void LsaPrivateData_GetUnsetValue()
        {
            var identity = WindowsIdentity.GetCurrent();
            var principal = new WindowsPrincipal(identity);
            if (!principal.IsInRole(WindowsBuiltInRole.Administrator))
            {
                Assert.Inconclusive("Current user is not in the administator group");
            }

            // Get
            var value = LsaPrivateData.GetValue("LsaPrivateDataTestsUnset");
            Assert.IsNull(value);
        }
    }
}
