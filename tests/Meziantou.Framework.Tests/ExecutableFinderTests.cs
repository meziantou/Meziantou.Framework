using Meziantou.Xunit;

namespace Meziantou.Framework.Tests;

public class ExecutableFinderTests
{
    [Fact, RunIf(TestOperatingSystems.Windows)]
    public void GetFullExecutablePathTests_Windows()
    {
        var result = ExecutableFinder.GetFullExecutablePath("calc");
        Assert.Equal(@"C:\Windows\System32\calc.exe", result, ignoreCase: true);
    }

    [Fact, RunIf(TestOperatingSystems.Windows)]
    public void GetFullExecutablePathTests_Windows_CurrentFolder()
    {
        var fileNameWithoutExtension = $"meziantou.{Guid.NewGuid():N}";
        var path = Path.GetFullPath(fileNameWithoutExtension + ".exe");
        File.WriteAllBytes(path, []);
        var result = ExecutableFinder.GetFullExecutablePath(fileNameWithoutExtension, Path.GetDirectoryName(path));
        Assert.Equal(path, result, ignoreCase: true);
    }

    [Fact, RunIf(TestOperatingSystems.Windows)]
    public void GetFullExecutablePathTests_Windows_PrefersPathExtOverExtensionlessFile()
    {
        var dir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var fileName = $"meziantou.{Guid.NewGuid():N}";
            File.WriteAllBytes(Path.Combine(dir, fileName), []);
            var cmdPath = Path.Combine(dir, fileName + ".cmd");
            File.WriteAllBytes(cmdPath, []);

            var result = ExecutableFinder.GetFullExecutablePath(fileName, dir);

            Assert.Equal(cmdPath, result, ignoreCase: true);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact, RunIf(TestOperatingSystems.Windows)]
    public void GetFullExecutablePathTests_Windows_ExtensionlessFileOnly_ReturnsNull()
    {
        var dir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var fileName = $"meziantou.{Guid.NewGuid():N}";
            File.WriteAllBytes(Path.Combine(dir, fileName), []);

            var result = ExecutableFinder.GetFullExecutablePath(fileName, dir);

            Assert.Null(result);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact, RunIf(TestOperatingSystems.Windows)]
    public void GetFullExecutablePathTests_Windows_NameWithExtension_ReturnsExactFile()
    {
        var dir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var fileName = $"meziantou.{Guid.NewGuid():N}";
            File.WriteAllBytes(Path.Combine(dir, fileName), []);
            var cmdPath = Path.Combine(dir, fileName + ".cmd");
            File.WriteAllBytes(cmdPath, []);

            var result = ExecutableFinder.GetFullExecutablePath(fileName + ".cmd", dir);

            Assert.Equal(cmdPath, result, ignoreCase: true);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact, RunIf(TestOperatingSystems.Windows)]
    public void GetFullExecutablePathTests_Windows_Npm()
    {
        var npmPath = ExecutableFinder.GetFullExecutablePath("npm");
        global::Xunit.Assert.SkipWhen(npmPath is null, "npm is not installed.");

        Assert.True(Path.HasExtension(npmPath), $"'{npmPath}' must have an extension");
    }

    [Fact, RunIf(TestOperatingSystems.Linux | TestOperatingSystems.MacOS)]
    public void GetFullExecutablePathTests_Linux()
    {
        var result = ExecutableFinder.GetFullExecutablePath("ls");
        Assert.True(result is "/bin/ls" or "/usr/bin/ls");
    }

    [Fact, RunIf(TestOperatingSystems.Linux | TestOperatingSystems.MacOS)]
    public void GetFullExecutablePathTests_Unix_WithoutExecuteBit_ReturnsNull()
    {
        var dir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var fileName = $"meziantou.{Guid.NewGuid():N}";
            var filePath = Path.Combine(dir, fileName);
            File.WriteAllBytes(filePath, []);
            File.SetUnixFileMode(filePath, UnixFileMode.UserRead | UnixFileMode.UserWrite);

            var result = ExecutableFinder.GetFullExecutablePath(fileName, dir);

            Assert.Null(result);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact, RunIf(TestOperatingSystems.Linux | TestOperatingSystems.MacOS)]
    public void GetFullExecutablePathTests_Unix_WithExecuteBit_ReturnsPath()
    {
        var dir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var fileName = $"meziantou.{Guid.NewGuid():N}";
            var filePath = Path.Combine(dir, fileName);
            File.WriteAllBytes(filePath, []);
            File.SetUnixFileMode(filePath, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);

            var result = ExecutableFinder.GetFullExecutablePath(fileName, dir);

            Assert.Equal(filePath, result);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }
}
