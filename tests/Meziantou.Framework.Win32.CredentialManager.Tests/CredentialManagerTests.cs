using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;
using Meziantou.Xunit;

namespace Meziantou.Framework.Win32.Tests;

[Collection("CredentialManagerTests")]
public sealed partial class CredentialManagerTests
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
            Assert.All(creds, cred => Assert.Equal(credType, cred.CredentialType));
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
        Assert.StartsWith("Only CredentialType.Generic and CredentialType.DomainPassword is supported", ex.Message);
    }

    [Fact, RunIf(TestOperatingSystems.Windows)]
    public void CredentialManager_Enumerate_NoMatch_ReturnsEmpty()
    {
        using var context = new IsolatedContext();
        var credentials = CredentialManager.EnumerateCredentials(context.GetCredentialName("*"));
        Assert.Empty(credentials);
    }

    // The credential prompts need a UI, so the dialogs themselves cannot be tested. The two buffer helpers behind
    // them can: CredPackAuthenticationBuffer and CredUnPackAuthenticationBuffer are documented as a pair, so
    // GetInputBuffer's output is exactly what GetCredentialsFromOutputBuffer expects.
    [Theory, RunIf(TestOperatingSystems.Windows)]
    [InlineData("john", "Pa$$w0rd")]
    [InlineData("john", "")]
    [InlineData("CONTOSO\\john", "Pa$$w0rd")]
    [SupportedOSPlatform("windows6.0.6000")]
    public unsafe void CredentialManager_AuthenticationBuffer_RoundTrips(string user, string password)
    {
        CredentialManager.GetInputBuffer(user, password, out var buffer, out var size);
        Assert.NotEqual(IntPtr.Zero, (IntPtr)buffer);

        // GetCredentialsFromOutputBuffer zeroes and frees the buffer it is given.
        Assert.True(CredentialManager.GetCredentialsFromOutputBuffer(buffer, size, out var actualUser, out var actualPassword, out var actualDomain));

        var (expectedDomain, expectedUser) = user.Split('\\') is [var d, var u] ? (d, u) : ("", user);
        Assert.Equal(expectedUser, actualUser);
        Assert.Equal(expectedDomain, actualDomain);
        Assert.Equal(password, actualPassword);
    }

    // 1024 bytes, the size this used to hard-code, is not enough for a long user name plus a password.
    [Theory, RunIf(TestOperatingSystems.Windows)]
    [InlineData(400, 200)]
    [InlineData(513, 256)]
    [SupportedOSPlatform("windows6.0.6000")]
    public unsafe void CredentialManager_AuthenticationBuffer_RoundTripsLongCredentials(int userLength, int passwordLength)
    {
        var user = new string('u', userLength);
        var password = new string('p', passwordLength);

        CredentialManager.GetInputBuffer(user, password, out var buffer, out var size);
        Assert.NotEqual(IntPtr.Zero, (IntPtr)buffer);
        Assert.True(size > 1024, $"expected the packed buffer to exceed 1024 bytes, was {size}");

        Assert.True(CredentialManager.GetCredentialsFromOutputBuffer(buffer, size, out var actualUser, out var actualPassword, out _));
        Assert.Equal(user, actualUser);
        Assert.Equal(password, actualPassword);
    }

    [Fact, RunIf(TestOperatingSystems.Windows)]
    [SupportedOSPlatform("windows6.0.6000")]
    public unsafe void CredentialManager_GetInputBuffer_NoUserName_PacksNothing()
    {
        CredentialManager.GetInputBuffer(user: null, password: "Pa$$w0rd", out var buffer, out var size);
        Assert.Equal(IntPtr.Zero, (IntPtr)buffer);
        Assert.Equal(0u, size);

        CredentialManager.GetInputBuffer(user: "", password: "Pa$$w0rd", out buffer, out size);
        Assert.Equal(IntPtr.Zero, (IntPtr)buffer);
        Assert.Equal(0u, size);
    }

    [Fact, RunIf(TestOperatingSystems.Windows)]
    public void CredentialManager_ToString_DoesNotExposeThePassword()
    {
        using var context = new IsolatedContext();
        var credentialName = context.GetCredentialName();
        CredentialManager.WriteCredential(credentialName, "John", "Pa$$w0rd", "Test", CredentialPersistence.Session);
        try
        {
            var cred = CredentialManager.ReadCredential(credentialName);
            Assert.NotNull(cred);
            Assert.Equal("Pa$$w0rd", cred.Password);

            var text = cred.ToString();
            Assert.DoesNotContain("Pa$$w0rd", text);
            Assert.Contains("Password: ******", text);
            Assert.Contains("UserName: John", text);
        }
        finally
        {
            CredentialManager.DeleteCredential(credentialName);
        }
    }

    [Fact]
    public void Credential_ToString_WithoutPassword_DoesNotShowTheMask()
    {
        var cred = new Credential(CredentialType.Generic, "App", "John", password: null, "Test");
        Assert.Equal("CredentialType: Generic, ApplicationName: App, UserName: John, Password: , Comment: Test", cred.ToString());
    }

    [Fact, RunIf(TestOperatingSystems.Windows)]
    public void CredentialManager_TryDeleteCredential()
    {
        using var context = new IsolatedContext();
        var credentialName = context.GetCredentialName();
        CredentialManager.WriteCredential(credentialName, "John", "Doe", CredentialPersistence.Session);

        Assert.True(CredentialManager.TryDeleteCredential(credentialName));
        Assert.Null(CredentialManager.ReadCredential(credentialName));

        Assert.False(CredentialManager.TryDeleteCredential(credentialName));
    }

    [Fact, RunIf(TestOperatingSystems.Windows)]
    public void CredentialManager_TryDeleteCredential_DomainPassword()
    {
        using var context = new IsolatedContext();
        var credentialName = context.GetCredentialName();
        CredentialManager.WriteCredential(credentialName, "John", "Doe", "Test", CredentialPersistence.Session, CredentialType.DomainPassword);

        Assert.False(CredentialManager.TryDeleteCredential(credentialName, CredentialType.Generic));
        Assert.True(CredentialManager.TryDeleteCredential(credentialName, CredentialType.DomainPassword));
        Assert.False(CredentialManager.TryDeleteCredential(credentialName, CredentialType.DomainPassword));
    }

    [Fact, RunIf(TestOperatingSystems.Windows)]
    public void CredentialManager_DeleteCredential_WhenMissing_Throws()
    {
        using var context = new IsolatedContext();
        Assert.Throws<Win32Exception>(() => CredentialManager.DeleteCredential(context.GetCredentialName()));
    }

    [Fact, RunIf(TestOperatingSystems.Windows)]
    public void CredentialManager_ReadCredential_NullApplicationName_Throws()
    {
        var ex = Assert.Throws<ArgumentNullException>(() => CredentialManager.ReadCredential(null!));
        Assert.Equal("applicationName", ex.ParamName);

        ex = Assert.Throws<ArgumentNullException>(() => CredentialManager.ReadCredential(null!, CredentialType.DomainPassword));
        Assert.Equal("applicationName", ex.ParamName);
    }

    [Fact, RunIf(TestOperatingSystems.Windows)]
    public void CredentialManager_LimitComment_TooLong()
    {
        using var context = new IsolatedContext();
        var ex = Assert.Throws<ArgumentOutOfRangeException>(() => CredentialManager.WriteCredential(context.GetCredentialName(), "John", "Doe", new string('a', 256), CredentialPersistence.Session));
        Assert.Equal("comment", ex.ParamName);
        Assert.StartsWith("The comment message has exceeded 255 characters.", ex.Message);
    }

    [Fact, RunIf(TestOperatingSystems.Windows)]
    public void CredentialManager_LimitSecret_TooLong()
    {
        using var context = new IsolatedContext();
        var ex = Assert.Throws<ArgumentOutOfRangeException>(() => CredentialManager.WriteCredential(context.GetCredentialName(), "John", new string('a', 1281), CredentialPersistence.Session));
        Assert.Equal("secret", ex.ParamName);
        Assert.StartsWith("The secret message has exceeded 2560 bytes.", ex.Message);
    }

    [Theory, RunIf(TestOperatingSystems.Windows)]
    [InlineData(new byte[] { 0xDE, 0xAD, 0xBE, 0xEF, 0x01, 0x02, 0x03 })] // odd length
    [InlineData(new byte[] { 0x00, 0xD8, 0x41, 0x00 })] // unpaired high surrogate
    [InlineData(new byte[] { 0x00 })]
    public void CredentialManager_Secret_RoundTripsBinaryBlobs(byte[] blob)
    {
        using var context = new IsolatedContext();
        var credentialName = context.GetCredentialName();
        WriteRawCredential(credentialName, "svc", blob);
        try
        {
            var cred = CredentialManager.ReadCredential(credentialName);
            Assert.NotNull(cred);
            Assert.Equal(blob, cred.Secret.ToArray());
        }
        finally
        {
            CredentialManager.DeleteCredential(credentialName);
        }
    }

    [Fact, RunIf(TestOperatingSystems.Windows)]
    public void CredentialManager_Secret_MatchesPasswordForTextSecrets()
    {
        using var context = new IsolatedContext();
        var credentialName = context.GetCredentialName();
        CredentialManager.WriteCredential(credentialName, "John", "Pa$$w0rd", CredentialPersistence.Session);
        try
        {
            var cred = CredentialManager.ReadCredential(credentialName);
            Assert.NotNull(cred);
            Assert.Equal("Pa$$w0rd", cred.Password);
            Assert.Equal(Encoding.Unicode.GetBytes("Pa$$w0rd"), cred.Secret.ToArray());
        }
        finally
        {
            CredentialManager.DeleteCredential(credentialName);
        }
    }

    [Fact, RunIf(TestOperatingSystems.Windows)]
    public void CredentialManager_Secret_IsEmptyWhenTheCredentialHasNoBlob()
    {
        using var context = new IsolatedContext();
        var credentialName = context.GetCredentialName();
        CredentialManager.WriteCredential(credentialName, "John", "Doe", "Test", CredentialPersistence.Session, CredentialType.DomainPassword);
        try
        {
            var cred = CredentialManager.ReadCredential(credentialName, CredentialType.DomainPassword);
            Assert.NotNull(cred);
            Assert.Null(cred.Password); // Domain Passwords can not be read back using CredRead API
            Assert.True(cred.Secret.IsEmpty);
        }
        finally
        {
            CredentialManager.DeleteCredential(credentialName, CredentialType.DomainPassword);
        }
    }

    /// <summary>Stores a credential whose blob is arbitrary bytes, the way another application could.</summary>
    private static unsafe void WriteRawCredential(string targetName, string userName, byte[] blob)
    {
        fixed (byte* blobPtr = blob)
        fixed (char* targetNamePtr = targetName)
        fixed (char* userNamePtr = userName)
        {
            var credential = new NativeMethods.CREDENTIALW
            {
                Type = 1, // CRED_TYPE_GENERIC
                Persist = 1, // CRED_PERSIST_SESSION
                TargetName = targetNamePtr,
                UserName = userNamePtr,
                CredentialBlob = blobPtr,
                CredentialBlobSize = (uint)blob.Length,
            };

            if (!NativeMethods.CredWriteW(in credential, Flags: 0))
                throw new Win32Exception(Marshal.GetLastWin32Error());
        }
    }

    private static unsafe partial class NativeMethods
    {
        [StructLayout(LayoutKind.Sequential)]
        public struct CREDENTIALW
        {
            public uint Flags;
            public uint Type;
            public char* TargetName;
            public char* Comment;
            public long LastWritten;
            public uint CredentialBlobSize;
            public byte* CredentialBlob;
            public uint Persist;
            public uint AttributeCount;
            public nint Attributes;
            public char* TargetAlias;
            public char* UserName;
        }

        [LibraryImport("advapi32.dll", SetLastError = true)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static partial bool CredWriteW(in CREDENTIALW credential, uint Flags);
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
