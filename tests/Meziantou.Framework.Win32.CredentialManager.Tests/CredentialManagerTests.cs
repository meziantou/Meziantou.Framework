using System;
using System.Threading;
using TestUtilities;
using Xunit;

namespace Meziantou.Framework.Win32.Tests
{
    [Collection("CredentialManagerTests")]
    public sealed class CredentialManagerTests : IDisposable
    {
        private readonly Mutex _mutex;

        private readonly string _prefix;
        private readonly string _credentialName1;
        private readonly string _credentialName2;

        public CredentialManagerTests()
        {
            var guid = Guid.NewGuid().ToString("N");
            _prefix = "CredentialManagerTests_" + guid + "_";
            _credentialName1 = _prefix + "_1";
            _credentialName2 = _prefix + "_2";

            _mutex = new Mutex(initiallyOwned: false, typeof(CredentialManagerTests).FullName);
        }

        [RunIfWindowsFact]
        public void CredentialManager_01()
        {
            CredentialManager.WriteCredential(_credentialName1, "John", "Doe", "Test", CredentialPersistence.Session);

            var cred = CredentialManager.ReadCredential(_credentialName1);
            Assert.Equal(_credentialName1, cred.ApplicationName);
            Assert.Equal("John", cred.UserName);
            Assert.Equal("Doe", cred.Password);
            Assert.Equal("Test", cred.Comment);

            CredentialManager.DeleteCredential(_credentialName1);
            cred = CredentialManager.ReadCredential(_credentialName1);
            Assert.Null(cred);
        }

        [RunIfWindowsFact]
        public void CredentialManager_Enumerate()
        {
            CredentialManager.WriteCredential(_credentialName1, "John", "Doe", "Test", CredentialPersistence.Session);
            CredentialManager.WriteCredential(_credentialName2, "John", "Doe", "Test", CredentialPersistence.Session);
            try
            {
                var creds = CredentialManager.EnumerateCrendentials(_prefix + "*");
                Assert.Equal(2, creds.Count);
            }
            finally
            {
                CredentialManager.DeleteCredential(_credentialName1);
                CredentialManager.DeleteCredential(_credentialName2);
            }
        }

        [RunIfWindowsFact]
        public void CredentialManager_LimitComment()
        {
            var comment = new string('a', 255);
            CredentialManager.WriteCredential(_credentialName1, "John", "Doe", comment, CredentialPersistence.Session);

            var cred = CredentialManager.ReadCredential(_credentialName1);
            Assert.Equal(_credentialName1, cred.ApplicationName);
            Assert.Equal("John", cred.UserName);
            Assert.Equal("Doe", cred.Password);
            Assert.Equal(comment, cred.Comment);

            CredentialManager.DeleteCredential(_credentialName1);
            cred = CredentialManager.ReadCredential(_credentialName1);
            Assert.Null(cred);
        }

        [RunIfWindowsFact]
        public void CredentialManager_LimitSecret()
        {
            var secret = new string('a', 512 * 5 / 2);
            CredentialManager.WriteCredential(_credentialName1, "John", secret, CredentialPersistence.Session);

            var cred = CredentialManager.ReadCredential(_credentialName1);
            Assert.Equal(secret, cred.Password);

            CredentialManager.DeleteCredential(_credentialName1);
            cred = CredentialManager.ReadCredential(_credentialName1);
            Assert.Null(cred);
        }

        [RunIfWindowsFact]
        [Trait("Issue", "https://github.com/meziantou/Meziantou.Framework/issues/32")]
        public void CredentialManager_EnumerateCredential()
        {
            _mutex.WaitOne();
            try
            {
                CredentialManager.WriteCredential(_credentialName1, "John", "Doe", "Test", CredentialPersistence.Session);
                try
                {
                    var credentials = CredentialManager.EnumerateCrendentials();
                    foreach (var credential in credentials)
                    {
                        _ = credential.UserName;
                    }

                    Assert.NotEmpty(credentials);
                }
                finally
                {
                    CredentialManager.DeleteCredential(_credentialName1);
                }
            }
            finally
            {
                _mutex.ReleaseMutex();
            }
        }

        public void Dispose()
        {
            _mutex?.Dispose();
        }
    }
}
