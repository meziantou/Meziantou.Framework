using TestUtilities;
using Xunit;

namespace Meziantou.Framework.Tests;

public class ExecutableFinderTests
{
    [Fact, RunIf(FactOperatingSystem.Windows)]
    public void GetFullExecutablePathTests_Windows()
    {
        var result = ExecutableFinder.GetFullExecutablePath("calc");
        Assert.Equal(@"C:\Windows\System32\calc.exe", result, ignoreCase: true);
    }

    [Fact, RunIf(FactOperatingSystem.Windows)]
    public void GetFullExecutablePathTests_Windows_CurrentFolder()
    {
        var fileNameWithoutExtension = $"meziantou.{Guid.NewGuid():N}";
        var path = Path.GetFullPath(fileNameWithoutExtension + ".exe");
        File.WriteAllBytes(path, []);
        var result = ExecutableFinder.GetFullExecutablePath(fileNameWithoutExtension, Path.GetDirectoryName(path));
        Assert.Equal(path, result, ignoreCase: true);
    }

    [Fact, RunIf(FactOperatingSystem.Linux | FactOperatingSystem.OSX)]
    public void GetFullExecutablePathTests_Linux()
    {
        var result = ExecutableFinder.GetFullExecutablePath("ls");
        Assert.True(result is "/bin/ls" or "/usr/bin/ls");
    }
}
