using FluentAssertions;
using TestUtilities;
using Xunit;

namespace Meziantou.Framework.Win32.Tests;

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

    [Fact, RunIf(FactOperatingSystem.Windows)]
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
        cred.Should().BeNull();
    }

    [Fact, RunIf(FactOperatingSystem.Windows)]
    public void CredentialManager_Enumerate()
    {
        CredentialManager.WriteCredential(_credentialName1, "John", "Doe", "Test", CredentialPersistence.Session);
        CredentialManager.WriteCredential(_credentialName2, "John", "Doe", "Test", CredentialPersistence.Session);
        try
        {
            var creds = CredentialManager.EnumerateCredentials(_prefix + "*");
            Assert.Equal(2, creds.Count);
        }
        finally
        {
            CredentialManager.DeleteCredential(_credentialName1);
            CredentialManager.DeleteCredential(_credentialName2);
        }
    }

    [Fact, RunIf(FactOperatingSystem.Windows)]
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
        cred.Should().BeNull();
    }

    [Theory, RunIf(FactOperatingSystem.Windows)]
    [InlineData(512)]
    [InlineData(513)]
    [InlineData(1024)]
    [InlineData(512 * 5 / 2)]
    public void CredentialManager_LimitSecret(int secretLength)
    {
        var secret = new string('a', secretLength);
        CredentialManager.WriteCredential(_credentialName1, "John", secret, CredentialPersistence.Session);

        var cred = CredentialManager.ReadCredential(_credentialName1);
        Assert.Equal(secret, cred.Password);

        CredentialManager.DeleteCredential(_credentialName1);
        cred = CredentialManager.ReadCredential(_credentialName1);
        cred.Should().BeNull();
    }

    [Fact, RunIf(FactOperatingSystem.Windows)]
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

    [Theory, RunIf(FactOperatingSystem.Windows)]
    [Trait("Issue", "https://github.com/meziantou/Meziantou.Framework/issues/263")]
    [InlineData(null)]
    [InlineData("*")]
    public void CredentialManager_EnumerateCredential_FilterNull(string filter)
    {
        _mutex.WaitOne();
        try
        {
            CredentialManager.WriteCredential(_credentialName1, "John", "Doe", "Test", CredentialPersistence.Session);
            try
            {
                var credentials = CredentialManager.EnumerateCredentials(filter);
                foreach (var credential in credentials)
                {
                    Assert.NotEmpty(credential.UserName);
                    Assert.NotEmpty(credential.Password);
                }

                credentials.Should().NotBeEmpty().And.ContainSingle(cred => cred.ApplicationName == _credentialName1);
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

    [Fact, RunIf(FactOperatingSystem.Windows)]
    public void CredentialManager_CredentialType_DomainPassword()
    {
        var credType = CredentialType.DomainPassword;

        CredentialManager.WriteCredential(_credentialName1, "John", "Doe", "Test", CredentialPersistence.Session, credType);

        var cred = CredentialManager.ReadCredential(_credentialName1, credType);
        cred.Should().NotBeNull();
        Assert.Equal(_credentialName1, cred.ApplicationName);
        Assert.Equal("John", cred.UserName);
        cred.Password.Should().BeNull(); // Domain Passwords can not be read back using CredRead API
        Assert.Equal("Test", cred.Comment);
        Assert.Equal(credType, cred.CredentialType);

        CredentialManager.DeleteCredential(_credentialName1, credType);
        cred = CredentialManager.ReadCredential(_credentialName1, credType);
        cred.Should().BeNull();
    }

    [Fact, RunIf(FactOperatingSystem.Windows)]
    public void CredentialManager_CredentialType_DomainPassword_Enumerate()
    {
        var credType = CredentialType.DomainPassword;

        CredentialManager.WriteCredential(_credentialName1, "John", "Doe", "Test", CredentialPersistence.Session, credType);
        CredentialManager.WriteCredential(_credentialName2, "John", "Doe", "Test", CredentialPersistence.Session, credType);
        try
        {
            var creds = CredentialManager.EnumerateCredentials(_prefix + "*");
            Assert.Equal(2, creds.Count);
            Assert.True(creds.All(cred => cred.CredentialType == credType));
        }
        finally
        {
            CredentialManager.DeleteCredential(_credentialName1, credType);
            CredentialManager.DeleteCredential(_credentialName2, credType);
        }
    }

    [Fact, RunIf(FactOperatingSystem.Windows)]
    public void CredentialManager_CredentialType_Invalid()
    {
        var act = () => CredentialManager.WriteCredential(_credentialName1, "John", "Doe", "Test", CredentialPersistence.Session, CredentialType.DomainCertificate);

        act.Should().Throw<ArgumentOutOfRangeException>().WithMessage("Only CredentialType.Generic and CredentialType.DomainPassword is supported*");
    }

    public void Dispose()
    {
        _mutex?.Dispose();
    }
}
