using System.Text;
using FluentAssertions;
using TestUtilities;
using Xunit;

namespace Meziantou.Framework.Win32.AmsiTests;

[Collection("AmsiContextTests")]
public class AmsiContextTests
{
    [Fact, SkipOnGitHubActionsAttribute]
    public void AmsiShouldDetectMalware_Buffer()
    {
        using var application = AmsiContext.Create("MyApplication");
        // https://en.wikipedia.org/wiki/EICAR_test_file
        application.IsMalware(Encoding.ASCII.GetBytes(@"X5O!P%@AP[4\PZX54(P^)7CC)7}$EICAR-STANDARD-ANTIVIRUS-TEST-FILE!$H+H*"), "EICAR").Should().BeTrue();
        application.IsMalware(new byte[] { 0, 0, 0, 0 }, "EICAR").Should().BeFalse();
    }

    [Fact, SkipOnGitHubActionsAttribute]
    public void AmsiShouldDetectMalware_String()
    {
        using var application = AmsiContext.Create("MyApplication");
        // https://en.wikipedia.org/wiki/EICAR_test_file
        application.IsMalware(@"X5O!P%@AP[4\PZX54(P^)7CC)7}$EICAR-STANDARD-ANTIVIRUS-TEST-FILE!$H+H*", "EICAR").Should().BeTrue();
        application.IsMalware("0000", "EICAR").Should().BeFalse();
    }

    [Fact, SkipOnGitHubActionsAttribute]
    public void AmsiSessionShouldDetectMalware_Buffer()
    {
        using var application = AmsiContext.Create("MyApplication");
        using var session = application.CreateSession();

        // https://en.wikipedia.org/wiki/EICAR_test_file
        session.IsMalware(Encoding.ASCII.GetBytes(@"X5O!P%@AP[4\PZX54(P^)7CC)7}$EICAR-STANDARD-ANTIVIRUS-TEST-FILE!$H+H*"), "EICAR").Should().BeTrue();
        session.IsMalware(new byte[] { 0, 0, 0, 0 }, "EICAR").Should().BeFalse();
    }

    [Fact, SkipOnGitHubActionsAttribute]
    public void AmsiSessionShouldDetectMalware_String()
    {
        using var application = AmsiContext.Create("MyApplication");
        using var session = application.CreateSession();

        // https://en.wikipedia.org/wiki/EICAR_test_file
        session.IsMalware(@"X5O!P%@AP[4\PZX54(P^)7CC)7}$EICAR-STANDARD-ANTIVIRUS-TEST-FILE!$H+H*", "EICAR").Should().BeTrue();
        session.IsMalware("0000", "EICAR").Should().BeFalse();
    }
}
