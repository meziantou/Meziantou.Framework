using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Meziantou.Framework.Win32.Tests
{
    [TestClass]
    public class CredentialManagerTests
    {
        [TestMethod]
        public void CredentialManager_01()
        {
            CredentialManager.WriteCredential("CredentialManagerTests", "John", "Doe", "Test", CredentialPersistence.Session);

            var cred = CredentialManager.ReadCredential("CredentialManagerTests");
            Assert.AreEqual("CredentialManagerTests", cred.ApplicationName);
            Assert.AreEqual("John", cred.UserName);
            Assert.AreEqual("Doe", cred.Password);
            Assert.AreEqual("Test", cred.Comment);

            CredentialManager.DeleteCredential("CredentialManagerTests");
            cred = CredentialManager.ReadCredential("CredentialManagerTests");
            Assert.IsNull(cred);
        }

        [TestMethod]
        public void CredentialManager_Enumerate()
        {
            CredentialManager.WriteCredential("CredentialManagerTests", "John", "Doe", "Test", CredentialPersistence.Session);
            CredentialManager.WriteCredential("CredentialManagerTests2", "John", "Doe", "Test", CredentialPersistence.Session);
            try
            {
                var creds = CredentialManager.EnumerateCrendentials("CredentialManagerTests*");
                Assert.AreEqual(2, creds.Count);
            }
            finally
            {
                CredentialManager.DeleteCredential("CredentialManagerTests");
                CredentialManager.DeleteCredential("CredentialManagerTests2");
            }
        }

        [TestMethod]
        public void CredentialManager_LimitComment()
        {
            var comment = new string('a', 255);
            CredentialManager.WriteCredential("CredentialManagerTests", "John", "Doe", comment, CredentialPersistence.Session);

            var cred = CredentialManager.ReadCredential("CredentialManagerTests");
            Assert.AreEqual("CredentialManagerTests", cred.ApplicationName);
            Assert.AreEqual("John", cred.UserName);
            Assert.AreEqual("Doe", cred.Password);
            Assert.AreEqual(comment, cred.Comment);

            CredentialManager.DeleteCredential("CredentialManagerTests");
            cred = CredentialManager.ReadCredential("CredentialManagerTests");
            Assert.IsNull(cred);
        }

        [TestMethod]
        public void CredentialManager_LimitSecret()
        {
            var secret = new string('a', 512 * 5 / 2);
            CredentialManager.WriteCredential("CredentialManagerTests", "John", secret, CredentialPersistence.Session);

            var cred = CredentialManager.ReadCredential("CredentialManagerTests");
            Assert.AreEqual(secret, cred.Password);

            CredentialManager.DeleteCredential("CredentialManagerTests");
            cred = CredentialManager.ReadCredential("CredentialManagerTests");
            Assert.IsNull(cred);
        }
    }
}
