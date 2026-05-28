using Meziantou.Xunit;

namespace Meziantou.Framework.Win32.Tests;

[Collection("CredentialManagerTests")]
public sealed class CredentialManagerTests
{
    [Fact, RunIf(TestOperatingSystems.Windows)]
    public void CredentialManager_01()
    {
        using var context = new IsolatedContext();
        var credentialName = context.GetCredentialName();
        CredentialManager.WriteCredential(credentialName, "John", "Doe", "Test", CredentialPersistence.Session);

        var cred = CredentialManager.ReadCredential(credentialName);
        Assert.NotNull(cred);
        Assert.Equal(credentialName, cred.ApplicationName);
        Assert.Equal("John", cred.UserName);
        Assert.Equal("Doe", cred.Password);
        Assert.Equal("Test", cred.Comment);

        CredentialManager.DeleteCredential(credentialName);
        cred = CredentialManager.ReadCredential(credentialName);
        Assert.Null(cred);
    }

    [Fact, RunIf(TestOperatingSystems.Windows)]
    public void CredentialManager_Enumerate()
    {
        using var context = new IsolatedContext();
        var credentialName1 = context.GetCredentialName("1");
        var credentialName2 = context.GetCredentialName("2");
        CredentialManager.WriteCredential(credentialName1, "John", "Doe", "Test", CredentialPersistence.Session);
        CredentialManager.WriteCredential(credentialName2, "John", "Doe", "Test", CredentialPersistence.Session);
        try
        {
            var creds = CredentialManager.EnumerateCredentials(context.GetCredentialName("*"));
            Assert.Equal(2, creds.Count);
        }
        finally
        {
            CredentialManager.DeleteCredential(credentialName1);
            CredentialManager.DeleteCredential(credentialName2);
        }
    }

    [Fact, RunIf(TestOperatingSystems.Windows)]
    public void CredentialManager_LimitComment()
    {
        using var context = new IsolatedContext();
        var comment = new string('a', 255);
        var credentialName = context.GetCredentialName();
        CredentialManager.WriteCredential(credentialName, "John", "Doe", comment, CredentialPersistence.Session);

        var cred = CredentialManager.ReadCredential(credentialName);
        Assert.NotNull(cred);
        Assert.Equal(credentialName, cred.ApplicationName);
        Assert.Equal("John", cred.UserName);
        Assert.Equal("Doe", cred.Password);
        Assert.Equal(comment, cred.Comment);

        CredentialManager.DeleteCredential(credentialName);
        cred = CredentialManager.ReadCredential(credentialName);
        Assert.Null(cred);
    }

    [Theory, RunIf(TestOperatingSystems.Windows)]
    [InlineData(512)]
    [InlineData(513)]
    [InlineData(1024)]
    [InlineData(512 * 5 / 2)]
    public void CredentialManager_LimitSecret(int secretLength)
    {
        using var context = new IsolatedContext();
        var secret = new string('a', secretLength);
        var credentialName = context.GetCredentialName();
        CredentialManager.WriteCredential(credentialName, "John", secret, CredentialPersistence.Session);

        var cred = CredentialManager.ReadCredential(credentialName);
        Assert.NotNull(cred);
        Assert.Equal(secret, cred.Password);

        CredentialManager.DeleteCredential(credentialName);
        cred = CredentialManager.ReadCredential(credentialName);
        Assert.Null(cred);
    }

    [Fact, RunIf(TestOperatingSystems.Windows)]
    [Trait("Issue", "https://github.com/meziantou/Meziantou.Framework/issues/32")]
    public void CredentialManager_EnumerateCredential()
    {
        using var context = new IsolatedContext(requiresMutex: true);
        var credentialName = context.GetCredentialName();
        CredentialManager.WriteCredential(credentialName, "John", "Doe", "Test", CredentialPersistence.Session);
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
            CredentialManager.DeleteCredential(credentialName);
        }
    }

    [Theory, RunIf(TestOperatingSystems.Windows)]
    [Trait("Issue", "https://github.com/meziantou/Meziantou.Framework/issues/263")]
    [InlineData(null)]
    [InlineData("*")]
    public void CredentialManager_EnumerateCredential_FilterNull(string? filter)
    {
        using var context = new IsolatedContext(requiresMutex: true);
        var credentialName = context.GetCredentialName();
        CredentialManager.WriteCredential(credentialName, "John", "Doe", "Test", CredentialPersistence.Session);
        try
        {
            var credentials = CredentialManager.EnumerateCredentials(filter);
            foreach (var credential in credentials)
            {
                Assert.NotEmpty(credential.ApplicationName);
            }

            Assert.Single(credentials, cred => cred.ApplicationName == credentialName);
        }
        finally
        {
            CredentialManager.DeleteCredential(credentialName);
        }
    }

    [Fact, RunIf(TestOperatingSystems.Windows)]
    public void CredentialManager_CredentialType_DomainPassword()
    {
        using var context = new IsolatedContext();
        var credType = CredentialType.DomainPassword;

        var credentialName = context.GetCredentialName();
        CredentialManager.WriteCredential(credentialName, "John", "Doe", "Test", CredentialPersistence.Session, credType);

        var cred = CredentialManager.ReadCredential(credentialName, credType);
        Assert.NotNull(cred);
        Assert.Equal(credentialName, cred.ApplicationName);
        Assert.Equal("John", cred.UserName);
        Assert.Null(cred.Password); // Domain Passwords can not be read back using CredRead API
        Assert.Equal("Test", cred.Comment);
        Assert.Equal(credType, cred.CredentialType);

        CredentialManager.DeleteCredential(credentialName, credType);
        cred = CredentialManager.ReadCredential(credentialName, credType);
        Assert.Null(cred);
    }

    [Fact, RunIf(TestOperatingSystems.Windows)]
    public void CredentialManager_CredentialType_DomainPassword_Enumerate()
    {
        var credType = CredentialType.DomainPassword;

        using var context = new IsolatedContext();
        var credentialName1 = context.GetCredentialName("1");
        var credentialName2 = context.GetCredentialName("2");

        CredentialManager.WriteCredential(credentialName1, "John", "Doe", "Test", CredentialPersistence.Session, credType);
        CredentialManager.WriteCredential(credentialName2, "John", "Doe", "Test", CredentialPersistence.Session, credType);
        try
        {
            var creds = CredentialManager.EnumerateCredentials(context.GetCredentialName("*"));
            Assert.Equal(2, creds.Count);
            Assert.True(creds.All(cred => cred.CredentialType == credType));
        }
        finally
        {
            CredentialManager.DeleteCredential(credentialName1, credType);
            CredentialManager.DeleteCredential(credentialName2, credType);
        }
    }

    [Fact, RunIf(TestOperatingSystems.Windows)]
    public void CredentialManager_CredentialType_Invalid()
    {
        using var context = new IsolatedContext();
        var ex = Assert.Throws<ArgumentOutOfRangeException>(() => CredentialManager.WriteCredential(context.GetCredentialName(), "John", "Doe", "Test", CredentialPersistence.Session, CredentialType.DomainCertificate));
        Assert.StartsWith("Only CredentialType.Generic and CredentialType.DomainPassword is supported", ex.Message, StringComparison.Ordinal);
    }

    private sealed class IsolatedContext : IDisposable
    {
        private readonly Mutex? _mutex;

        public string ScopeName { get; }

        public string GetCredentialName(string? context = null) => ScopeName + "_" + (context ?? "default");

        public IsolatedContext(bool requiresMutex = false)
        {
            var guid = Guid.NewGuid().ToString("N");
            ScopeName = "CredentialManagerTests_" + guid;

            if (requiresMutex)
            {
                _mutex = new Mutex(initiallyOwned: false, typeof(CredentialManagerTests).FullName);
                try
                {
                    _mutex.WaitOne();
                }
                catch (AbandonedMutexException)
                {
                    // The mutex was abandoned, which means that the previous owner terminated without releasing it.
                    // We can still acquire the mutex, but we should be aware that the state of the shared resource may be inconsistent.
                }
            }
        }

        public void Dispose()
        {
            _mutex?.ReleaseMutex();
            _mutex?.Dispose();
        }
    }
}
