using System;
using System.Threading;
using FluentAssertions;
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

        [RunIfFact(FactOperatingSystem.Windows)]
        public void CredentialManager_01()
        {
            CredentialManager.WriteCredential(_credentialName1, "John", "Doe", "Test", CredentialPersistence.Session);

            var cred = CredentialManager.ReadCredential(_credentialName1);
            cred.ApplicationName.Should().Be(_credentialName1);
            cred.UserName.Should().Be("John");
            cred.Password.Should().Be("Doe");
            cred.Comment.Should().Be("Test");

            CredentialManager.DeleteCredential(_credentialName1);
            cred = CredentialManager.ReadCredential(_credentialName1);
            cred.Should().BeNull();
        }

        [RunIfFact(FactOperatingSystem.Windows)]
        public void CredentialManager_Enumerate()
        {
            CredentialManager.WriteCredential(_credentialName1, "John", "Doe", "Test", CredentialPersistence.Session);
            CredentialManager.WriteCredential(_credentialName2, "John", "Doe", "Test", CredentialPersistence.Session);
            try
            {
                var creds = CredentialManager.EnumerateCredentials(_prefix + "*");
                creds.Count.Should().Be(2);
            }
            finally
            {
                CredentialManager.DeleteCredential(_credentialName1);
                CredentialManager.DeleteCredential(_credentialName2);
            }
        }

        [RunIfFact(FactOperatingSystem.Windows)]
        public void CredentialManager_LimitComment()
        {
            var comment = new string('a', 255);
            CredentialManager.WriteCredential(_credentialName1, "John", "Doe", comment, CredentialPersistence.Session);

            var cred = CredentialManager.ReadCredential(_credentialName1);
            cred.ApplicationName.Should().Be(_credentialName1);
            cred.UserName.Should().Be("John");
            cred.Password.Should().Be("Doe");
            cred.Comment.Should().Be(comment);

            CredentialManager.DeleteCredential(_credentialName1);
            cred = CredentialManager.ReadCredential(_credentialName1);
            cred.Should().BeNull();
        }

        [RunIfFact(FactOperatingSystem.Windows)]
        public void CredentialManager_LimitSecret()
        {
            var secret = new string('a', 512 * 5 / 2);
            CredentialManager.WriteCredential(_credentialName1, "John", secret, CredentialPersistence.Session);

            var cred = CredentialManager.ReadCredential(_credentialName1);
            cred.Password.Should().Be(secret);

            CredentialManager.DeleteCredential(_credentialName1);
            cred = CredentialManager.ReadCredential(_credentialName1);
            cred.Should().BeNull();
        }

        [RunIfFact(FactOperatingSystem.Windows)]
        [Trait("Issue", "https://github.com/meziantou/Meziantou.Framework/issues/32")]
        public void CredentialManager_EnumerateCredential()
        {
            _mutex.WaitOne();
            try
            {
                CredentialManager.WriteCredential(_credentialName1, "John", "Doe", "Test", CredentialPersistence.Session);
                try
                {
                    var credentials = CredentialManager.EnumerateCredentials();
                    foreach (var credential in credentials)
                    {
                        _ = credential.UserName;
                    }

                    credentials.Should().NotBeEmpty();
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
