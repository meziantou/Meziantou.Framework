using Meziantou.Xunit;

namespace Meziantou.Framework.Win32.AmsiTests;

[Collection("AmsiContextTests")]
public class AmsiContextTests
{
    [Fact, SkipIf(ContinuousIntegration = ContinuousIntegrationEnvironments.GitHubActions)]
    public void AmsiShouldDetectMalware_Buffer()
    {
        using var application = AmsiContext.Create("MyApplication");
        Assert.True(application.IsMalware(Encoding.ASCII.GetBytes(@"X5O!P%@AP[4\PZX54(P^)7CC)7}$EICAR-STANDARD-ANTIVIRUS-TEST-FILE!$H+H*"), "EICAR"));
        Assert.False(application.IsMalware(new byte[] { 0, 0, 0, 0 }, "EICAR"));
    }

    [Fact, SkipIf(ContinuousIntegration = ContinuousIntegrationEnvironments.GitHubActions)]
    public void AmsiShouldDetectMalware_String()
    {
        using var application = AmsiContext.Create("MyApplication");
        Assert.True(application.IsMalware(@"X5O!P%@AP[4\PZX54(P^)7CC)7}$EICAR-STANDARD-ANTIVIRUS-TEST-FILE!$H+H*", "EICAR"));
        Assert.False(application.IsMalware("0000", "EICAR"));
    }

    [Fact, SkipIf(ContinuousIntegration = ContinuousIntegrationEnvironments.GitHubActions)]
    public void AmsiSessionShouldDetectMalware_Buffer()
    {
        using var application = AmsiContext.Create("MyApplication");
        using var session = application.CreateSession();
        Assert.True(session.IsMalware(Encoding.ASCII.GetBytes(@"X5O!P%@AP[4\PZX54(P^)7CC)7}$EICAR-STANDARD-ANTIVIRUS-TEST-FILE!$H+H*"), "EICAR"));
        Assert.False(session.IsMalware([0, 0, 0, 0], "EICAR"));
    }

    [Fact, SkipIf(ContinuousIntegration = ContinuousIntegrationEnvironments.GitHubActions)]
    public void AmsiSessionShouldDetectMalware_String()
    {
        using var application = AmsiContext.Create("MyApplication");
        using var session = application.CreateSession();
        Assert.True(session.IsMalware(@"X5O!P%@AP[4\PZX54(P^)7CC)7}$EICAR-STANDARD-ANTIVIRUS-TEST-FILE!$H+H*", "EICAR"));
        Assert.False(session.IsMalware("0000", "EICAR"));
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

    [Fact, RunIf(TestOperatingSystems.Windows)]
    public void ScanReportsTheRawProviderResult()
    {
        using var context = AmsiContext.Create("MyApplication");
        Assert.False(context.Scan("0000", "EICAR").ShouldBlock());

        using var session = context.CreateSession();
        Assert.False(session.Scan([0, 0, 0, 0], "EICAR").ShouldBlock());
    }
}
