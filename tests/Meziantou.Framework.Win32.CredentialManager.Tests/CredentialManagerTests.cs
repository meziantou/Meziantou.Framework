using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Meziantou.Framework.Win32.Tests
{
    [TestClass]
    public class CredentialManagerTests
    {
        [TestMethod]
        public void CredentialManager_01()
        {
            CredentialManager.WriteCredential("CredentialManagerTests", "John", "Doe", CredentialPersistence.Session);

            var cred = CredentialManager.ReadCredential("CredentialManagerTests");
            Assert.AreEqual("CredentialManagerTests", cred.ApplicationName);
            Assert.AreEqual("John", cred.UserName);
            Assert.AreEqual("Doe", cred.Password);

            CredentialManager.DeleteCredential("CredentialManagerTests");
            cred = CredentialManager.ReadCredential("CredentialManagerTests");
            Assert.IsNull(cred);
        }
    }
}
