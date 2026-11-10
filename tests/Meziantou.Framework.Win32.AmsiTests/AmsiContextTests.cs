using System.Runtime.Versioning;
using Meziantou.Xunit;
using Microsoft.Win32;

namespace Meziantou.Framework.Win32.AmsiTests;

[Collection("AmsiContextTests")]
public class AmsiContextTests
{
    [Fact, RunIf(TestOperatingSystems.Windows), SkipIf(ContinuousIntegration = ContinuousIntegrationEnvironments.GitHubActions)]
    public void ReadmeExample_Scan()
    {
        #region AmsiContext_IsMalwareString
        using var context = AmsiContext.Create("MyApplication");

        var isMalware = context.IsMalware(@"X5O!P%@AP[4\PZX54(P^)7CC)7}$EICAR-STANDARD-ANTIVIRUS-TEST-FILE!$H+H*", "test.txt");
        Assert.True(isMalware);
        #endregion

        #region AmsiContext_IsMalwareBuffer
        var payload = Encoding.ASCII.GetBytes(@"X5O!P%@AP[4\PZX54(P^)7CC)7}$EICAR-STANDARD-ANTIVIRUS-TEST-FILE!$H+H*");
        Assert.True(context.IsMalware(payload, "document.txt"));
        Assert.False(context.IsMalware([0, 0, 0, 0], "document.bin"));
        #endregion
    }

    [Fact, RunIf(TestOperatingSystems.Windows), SkipIf(ContinuousIntegration = ContinuousIntegrationEnvironments.GitHubActions)]
    public void ReadmeExample_Session()
    {
        #region AmsiContext_Session
        string[] fragments = ["Write-Host 'Hello'", @"X5O!P%@AP[4\PZX54(P^)7CC)7}$EICAR-STANDARD-ANTIVIRUS-TEST-FILE!$H+H*"];

        using var context = AmsiContext.Create("MyApplication");
        using var session = context.CreateSession();

        var detected = false;
        foreach (var fragment in fragments)
        {
            if (session.IsMalware(fragment, "script.ps1"))
            {
                detected = true;
                break;
            }
        }

        Assert.True(detected);
        #endregion
    }

    [Fact, RunIf(TestOperatingSystems.Windows), SkipIf(ContinuousIntegration = ContinuousIntegrationEnvironments.GitHubActions)]
    public void AmsiShouldDetectMalware_Buffer()
    {
        using var application = AmsiContext.Create("MyApplication");
        Assert.True(application.IsMalware(Encoding.ASCII.GetBytes(@"X5O!P%@AP[4\PZX54(P^)7CC)7}$EICAR-STANDARD-ANTIVIRUS-TEST-FILE!$H+H*"), "EICAR"));
        Assert.False(application.IsMalware(new byte[] { 0, 0, 0, 0 }, "EICAR"));
    }

    [Fact, RunIf(TestOperatingSystems.Windows), SkipIf(ContinuousIntegration = ContinuousIntegrationEnvironments.GitHubActions)]
    public void AmsiShouldDetectMalware_String()
    {
        using var application = AmsiContext.Create("MyApplication");
        Assert.True(application.IsMalware(@"X5O!P%@AP[4\PZX54(P^)7CC)7}$EICAR-STANDARD-ANTIVIRUS-TEST-FILE!$H+H*", "EICAR"));
        Assert.False(application.IsMalware("0000", "EICAR"));
    }

    [Fact, RunIf(TestOperatingSystems.Windows), SkipIf(ContinuousIntegration = ContinuousIntegrationEnvironments.GitHubActions)]
    public void AmsiSessionShouldDetectMalware_Buffer()
    {
        using var application = AmsiContext.Create("MyApplication");
        using var session = application.CreateSession();
        Assert.True(session.IsMalware(Encoding.ASCII.GetBytes(@"X5O!P%@AP[4\PZX54(P^)7CC)7}$EICAR-STANDARD-ANTIVIRUS-TEST-FILE!$H+H*"), "EICAR"));
        Assert.False(session.IsMalware([0, 0, 0, 0], "EICAR"));
    }

    [Fact, RunIf(TestOperatingSystems.Windows), SkipIf(ContinuousIntegration = ContinuousIntegrationEnvironments.GitHubActions)]
    public void AmsiSessionShouldDetectMalware_String()
    {
        using var application = AmsiContext.Create("MyApplication");
        using var session = application.CreateSession();
        Assert.True(session.IsMalware(@"X5O!P%@AP[4\PZX54(P^)7CC)7}$EICAR-STANDARD-ANTIVIRUS-TEST-FILE!$H+H*", "EICAR"));
        Assert.False(session.IsMalware("0000", "EICAR"));
    }

    [Fact, RunIf(TestOperatingSystems.Windows), SkipIf(ContinuousIntegration = ContinuousIntegrationEnvironments.GitHubActions)]
    public void AmsiContextDocumentationExample()
    {
        #region AmsiContext_IsMalware
        using var context = AmsiContext.Create("MyApplication");

        // The EICAR test file is detected as malware by every antivirus
        Assert.True(context.IsMalware(@"X5O!P%@AP[4\PZX54(P^)7CC)7}$EICAR-STANDARD-ANTIVIRUS-TEST-FILE!$H+H*", "test.txt"));
        #endregion
    }

    [Fact, RunIf(TestOperatingSystems.Windows), SkipIf(ContinuousIntegration = ContinuousIntegrationEnvironments.GitHubActions)]
    public void AmsiSessionDocumentationExample()
    {
        #region AmsiSession_IsMalware
        using var context = AmsiContext.Create("MyApplication");

        // The scans of a session are correlated, e.g. the fragments of a script
        using var session = context.CreateSession();
        Assert.False(session.IsMalware("Hello world", "file1.txt"));
        Assert.True(session.IsMalware(@"X5O!P%@AP[4\PZX54(P^)7CC)7}$EICAR-STANDARD-ANTIVIRUS-TEST-FILE!$H+H*", "file2.txt"));
        #endregion
    }

    [Fact, RunIf(TestOperatingSystems.Windows)]
    public void DisposingTheContextBeforeTheSessionIsSafe()
    {
        var context = AmsiContext.Create("MyApplication");
        var session = context.CreateSession();

        context.Dispose();
        session.Dispose();

        // Closing a session against an uninitialized context corrupts the provider state, so assert AMSI is
        // still usable afterwards. Scanning is not asserted here: hosted CI agents can initialize AMSI but
        // have no provider ready to scan, which is why the EICAR tests below skip on GitHub Actions.
        using var other = AmsiContext.Create("MyApplication");
        using var otherSession = other.CreateSession();
    }

    [Fact, RunIf(TestOperatingSystems.Windows)]
    public void DisposingTheSessionBeforeTheContextIsSafe()
    {
        var context = AmsiContext.Create("MyApplication");
        var session = context.CreateSession();

        session.Dispose();
        context.Dispose();
    }

    [Fact, RunIf(TestOperatingSystems.Windows)]
    public void SeveralSessionsCanOutliveTheContext()
    {
        var context = AmsiContext.Create("MyApplication");
        var session1 = context.CreateSession();
        var session2 = context.CreateSession();

        context.Dispose();
        session1.Dispose();
        session2.Dispose();
    }

    [Fact, RunIf(TestOperatingSystems.Windows)]
    public void UsingADisposedContextThrows()
    {
        var context = AmsiContext.Create("MyApplication");
        context.Dispose();

        Assert.Throws<ObjectDisposedException>(() => context.IsMalware("0000", "EICAR"));
        Assert.Throws<ObjectDisposedException>(() => context.IsMalware([0, 0, 0, 0], "EICAR"));
        Assert.Throws<ObjectDisposedException>(context.CreateSession);
    }

    [Fact, RunIf(TestOperatingSystems.Windows)]
    public void UsingADisposedSessionThrows()
    {
        using var context = AmsiContext.Create("MyApplication");
        var session = context.CreateSession();
        session.Dispose();

        Assert.Throws<ObjectDisposedException>(() => session.IsMalware("0000", "EICAR"));
        Assert.Throws<ObjectDisposedException>(() => session.IsMalware([0, 0, 0, 0], "EICAR"));
    }

    [Fact, RunIf(TestOperatingSystems.Windows)]
    public void UsingASessionWhoseContextIsDisposedThrows()
    {
        var context = AmsiContext.Create("MyApplication");
        using var session = context.CreateSession();
        context.Dispose();

        Assert.Throws<ObjectDisposedException>(() => session.IsMalware("0000", "EICAR"));
    }

    [Fact, RunIf(TestOperatingSystems.Windows)]
    public void DisposingTwiceIsSafe()
    {
        var context = AmsiContext.Create("MyApplication");
        var session = context.CreateSession();

        session.Dispose();
        session.Dispose();
        context.Dispose();
        context.Dispose();
    }

    [Theory]
    [InlineData(AmsiResult.Clean, false)]
    [InlineData(AmsiResult.NotDetected, false)]
    [InlineData((AmsiResult)32767, false)]
    [InlineData(AmsiResult.BlockedByAdminStart, false)]
    [InlineData(AmsiResult.BlockedByAdminEnd, false)]
    [InlineData(AmsiResult.Detected, true)]
    [InlineData((AmsiResult)40000, true)]
    public void IsMalware_MatchesTheDocumentedThreshold(AmsiResult result, bool expected)
    {
        Assert.Equal(expected, result.IsMalware());
    }

    [Theory]
    [InlineData(AmsiResult.Clean, false)]
    [InlineData(AmsiResult.NotDetected, false)]
    [InlineData((AmsiResult)16383, false)]
    [InlineData(AmsiResult.BlockedByAdminStart, true)]
    [InlineData((AmsiResult)18000, true)]
    [InlineData(AmsiResult.BlockedByAdminEnd, true)]
    [InlineData((AmsiResult)20480, false)]
    [InlineData(AmsiResult.Detected, false)]
    public void IsBlockedByAdmin_CoversTheWholeAdminRange(AmsiResult result, bool expected)
    {
        Assert.Equal(expected, result.IsBlockedByAdmin());
    }

    [Theory]
    [InlineData(AmsiResult.Clean, false)]
    [InlineData(AmsiResult.NotDetected, false)]
    [InlineData(AmsiResult.BlockedByAdminStart, true)]
    [InlineData(AmsiResult.BlockedByAdminEnd, true)]
    [InlineData(AmsiResult.Detected, true)]
    public void ShouldBlock_CoversMalwareAndAdminPolicy(AmsiResult result, bool expected)
    {
        Assert.Equal(expected, result.ShouldBlock());
    }

    // Performs a real scan, so it needs a provider that is ready: hosted CI agents do not have one.
    [Fact, RunIf(TestOperatingSystems.Windows), SkipIf(ContinuousIntegration = ContinuousIntegrationEnvironments.GitHubActions)]
    public void ScanReportsTheRawProviderResult()
    {
        using var context = AmsiContext.Create("MyApplication");
        Assert.False(context.Scan("0000", "EICAR").ShouldBlock());

        using var session = context.CreateSession();
        Assert.False(session.Scan([0, 0, 0, 0], "EICAR").ShouldBlock());
    }

    // Performs a real scan, so it needs a provider that is ready: hosted CI agents do not have one.
    [Fact, RunIf(TestOperatingSystems.Windows), SkipIf(ContinuousIntegration = ContinuousIntegrationEnvironments.GitHubActions)]
    public void AmsiContext_ScanDocumentationExample()
    {
        #region AmsiContext_ShouldBlock
        using var context = AmsiContext.Create("MyApplication");

        bool CanRun(string script)
        {
            var result = context.Scan(script, "script.ps1");

            // ShouldBlock covers malware and the content that an administrator policy blocks
            return !result.ShouldBlock();
        }

        Assert.True(CanRun("Write-Host 'Hello'"));
        Assert.False(CanRun(@"X5O!P%@AP[4\PZX54(P^)7CC)7}$EICAR-STANDARD-ANTIVIRUS-TEST-FILE!$H+H*"));
        #endregion
    }

    [Fact, RunIf(TestOperatingSystems.Windows)]
    public void IsProviderRegistered_MatchesTheRegisteredProviders()
    {
        var providers = AmsiContext.GetRegisteredProviders();
        Assert.Equal(providers.Count > 0, AmsiContext.IsProviderRegistered());
        Assert.All(providers, provider => Assert.NotEqual(Guid.Empty, provider.Id));
        Assert.HasCount(providers.Count, providers.Select(provider => provider.Id).Distinct());
    }

    [Fact, RunIf(TestOperatingSystems.Windows)]
    public void GetRegisteredProviders_ReturnsAnEmptyListWhenTheProvidersKeyIsMissing()
    {
        using var root = CreateTemporaryRegistryRoot();

        Assert.Empty(AmsiContext.GetRegisteredProviders(root.Key));
    }

    [Fact, RunIf(TestOperatingSystems.Windows)]
    public void GetRegisteredProviders_ReturnsAnEmptyListWhenNoProviderIsRegistered()
    {
        using var root = CreateTemporaryRegistryRoot();
        root.Key.CreateSubKey(@"SOFTWARE\Microsoft\AMSI\Providers").Dispose();

        Assert.Empty(AmsiContext.GetRegisteredProviders(root.Key));
    }

    [Fact, RunIf(TestOperatingSystems.Windows)]
    public void GetRegisteredProviders_ReadsTheProvidersAndTheirComRegistration()
    {
        var registered = new Guid("11111111-2222-3333-4444-555555555555");
        var unregistered = new Guid("66666666-7777-8888-9999-AAAAAAAAAAAA");

        using var root = CreateTemporaryRegistryRoot();
        using (var providers = root.Key.CreateSubKey(@"SOFTWARE\Microsoft\AMSI\Providers"))
        {
            providers.CreateSubKey(registered.ToString("B")).Dispose();
            providers.CreateSubKey(unregistered.ToString("B")).Dispose();
            providers.CreateSubKey("not-a-guid").Dispose();
        }

        using (var classKey = root.Key.CreateSubKey(@"SOFTWARE\Classes\CLSID\" + registered.ToString("B")))
        {
            classKey.SetValue(name: null, "Sample provider");
            using var server = classKey.CreateSubKey("InprocServer32");
            server.SetValue(name: null, @"""C:\Program Files\Sample\provider.dll""");
        }

        var result = AmsiContext.GetRegisteredProviders(root.Key);

        Assert.HasCount(2, result);
        var provider = Assert.Single(result, provider => provider.Id == registered);
        Assert.Equal("Sample provider", provider.DisplayName);
        Assert.Equal(@"C:\Program Files\Sample\provider.dll", provider.ModulePath);
        Assert.Equal("Sample provider", provider.ToString());

        var withoutComRegistration = Assert.Single(result, provider => provider.Id == unregistered);
        Assert.Null(withoutComRegistration.DisplayName);
        Assert.Null(withoutComRegistration.ModulePath);
        Assert.Equal(unregistered.ToString("B"), withoutComRegistration.ToString());
    }

    // Microsoft Defender is the provider of the machines that run the scan tests
    [Fact, RunIf(TestOperatingSystems.Windows), SkipIf(ContinuousIntegration = ContinuousIntegrationEnvironments.GitHubActions)]
    public void GetRegisteredProviders_ReturnsMicrosoftDefender()
    {
        var defender = Assert.Single(AmsiContext.GetRegisteredProviders(), provider => provider.Id == new Guid("2781761E-28E0-4109-99FE-B9D127C57AFE"));
        Assert.NotNull(defender.DisplayName);
        Assert.NotNull(defender.ModulePath);
        Assert.True(File.Exists(defender.ModulePath));
    }

    [Fact, RunIf(TestOperatingSystems.Windows), SkipIf(ContinuousIntegration = ContinuousIntegrationEnvironments.GitHubActions)]
    public void AmsiContext_IsProviderRegisteredDocumentationExample()
    {
        #region AmsiContext_IsProviderRegistered
        // Without a provider, every content is reported as not detected
        if (!AmsiContext.IsProviderRegistered())
            throw new InvalidOperationException("No antimalware product is registered with AMSI");

        foreach (var provider in AmsiContext.GetRegisteredProviders())
        {
            // e.g. {2781761E-28E0-4109-99FE-B9D127C57AFE} Windows Defender IOfficeAntiVirus implementation
            Assert.NotEqual(Guid.Empty, provider.Id);
        }
        #endregion
    }

    [SupportedOSPlatform("windows")]
    private static TemporaryRegistryKey CreateTemporaryRegistryRoot()
    {
        var path = @"Software\Meziantou.Framework.Win32.AmsiTests-" + Guid.NewGuid().ToString("N");
        return new TemporaryRegistryKey(path, Registry.CurrentUser.CreateSubKey(path));
    }

    // Stands in for HKLM: the providers and the COM classes are read under it
    [SupportedOSPlatform("windows")]
    private sealed class TemporaryRegistryKey(string path, RegistryKey key) : IDisposable
    {
        public RegistryKey Key { get; } = key;

        public void Dispose()
        {
            Key.Dispose();
            Registry.CurrentUser.DeleteSubKeyTree(path, throwOnMissingSubKey: false);
        }
    }
}
