using FluentAssertions;
using TestUtilities;

namespace Meziantou.Framework.Tests;

public class ExecutableFinderTests
{
    [RunIfFact(FactOperatingSystem.Windows)]
    public void GetFullExecutablePathTests_Windows()
    {
        var result = ExecutableFinder.GetFullExecutablePath("calc");
        result.Should().BeEquivalentTo(@"C:\Windows\System32\calc.exe");
    }

    [RunIfFact(FactOperatingSystem.Linux | FactOperatingSystem.OSX)]
    public void GetFullExecutablePathTests_Linux()
    {
        var result = ExecutableFinder.GetFullExecutablePath("ls");
        result.Should().BeOneOf("/bin/ls", "/usr/bin/ls");
    }
}
