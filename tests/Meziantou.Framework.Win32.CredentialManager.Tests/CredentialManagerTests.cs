using TestUtilities;
using Xunit;

namespace Meziantou.Framework.Win32.Tests
{
    [Collection("CredentialManagerTests")]
    public sealed class CredentialManagerTests
    {
        [RunIfWindowsFact]
        public void CredentialManager_01()
        {
            CredentialManager.WriteCredential("CredentialManagerTests", "John", "Doe", "Test", CredentialPersistence.Session);

            var cred = CredentialManager.ReadCredential("CredentialManagerTests");
            Assert.Equal("CredentialManagerTests", cred.ApplicationName);
            Assert.Equal("John", cred.UserName);
            Assert.Equal("Doe", cred.Password);
            Assert.Equal("Test", cred.Comment);

            CredentialManager.DeleteCredential("CredentialManagerTests");
            cred = CredentialManager.ReadCredential("CredentialManagerTests");
            Assert.Null(cred);
        }

        [RunIfWindowsFact]
        public void CredentialManager_Enumerate()
        {
            CredentialManager.WriteCredential("CredentialManagerTests", "John", "Doe", "Test", CredentialPersistence.Session);
            CredentialManager.WriteCredential("CredentialManagerTests2", "John", "Doe", "Test", CredentialPersistence.Session);
            try
            {
                var creds = CredentialManager.EnumerateCrendentials("CredentialManagerTests*");
                Assert.Equal(2, creds.Count);
            }
            finally
            {
                CredentialManager.DeleteCredential("CredentialManagerTests");
                CredentialManager.DeleteCredential("CredentialManagerTests2");
            }
        }

        [RunIfWindowsFact]
        public void CredentialManager_LimitComment()
        {
            var comment = new string('a', 255);
            CredentialManager.WriteCredential("CredentialManagerTests", "John", "Doe", comment, CredentialPersistence.Session);

            var cred = CredentialManager.ReadCredential("CredentialManagerTests");
            Assert.Equal("CredentialManagerTests", cred.ApplicationName);
            Assert.Equal("John", cred.UserName);
            Assert.Equal("Doe", cred.Password);
            Assert.Equal(comment, cred.Comment);

            CredentialManager.DeleteCredential("CredentialManagerTests");
            cred = CredentialManager.ReadCredential("CredentialManagerTests");
            Assert.Null(cred);
        }

        [RunIfWindowsFact]
        public void CredentialManager_LimitSecret()
        {
            var secret = new string('a', 512 * 5 / 2);
            CredentialManager.WriteCredential("CredentialManagerTests", "John", secret, CredentialPersistence.Session);

            var cred = CredentialManager.ReadCredential("CredentialManagerTests");
            Assert.Equal(secret, cred.Password);

            CredentialManager.DeleteCredential("CredentialManagerTests");
            cred = CredentialManager.ReadCredential("CredentialManagerTests");
            Assert.Null(cred);
        }
    }
}
