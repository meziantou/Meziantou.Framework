using System.ComponentModel;
using System.Diagnostics;
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
            var fileName = $"meziantou_{Guid.NewGuid():N}";
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
            var fileName = $"meziantou_{Guid.NewGuid():N}";
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
            var fileName = $"meziantou_{Guid.NewGuid():N}";
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

    // Validates the assumption ExecutableFinder relies on: Windows cannot start an extensionless file, such as the 'npm' shell
    // script that the Node.js installer ships for Git Bash.
    [Fact, RunIf(TestOperatingSystems.Windows)]
    public void ProcessStart_Windows_ExtensionlessFile_Fails()
    {
        var dir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var filePath = Path.Combine(dir, $"meziantou_{Guid.NewGuid():N}");
            File.WriteAllText(filePath, "#!/bin/sh\necho hello\n");

            var psi = new ProcessStartInfo(filePath)
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                CreateNoWindow = true,
            };

            var exception = Assert.Throws<Win32Exception>(() => Process.Start(psi)?.Dispose());

            // ERROR_BAD_EXE_FORMAT: "The specified executable is not a valid application for this OS platform"
            Assert.Equal(193, exception.NativeErrorCode);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Theory, RunIf(TestOperatingSystems.Windows)]
    [InlineData(".cmd")]
    [InlineData(".bat")]
    public async Task ProcessStart_Windows_ResolvedFileWithExtension_Succeeds(string extension)
    {
        var dir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var fileName = $"meziantou_{Guid.NewGuid():N}";
            File.WriteAllText(Path.Combine(dir, fileName), "#!/bin/sh\necho hello\n");
            File.WriteAllText(Path.Combine(dir, fileName + extension), "@echo hello\r\n");

            var resolvedPath = ExecutableFinder.GetFullExecutablePath(fileName, dir);
            Assert.NotNull(resolvedPath);

            var psi = new ProcessStartInfo(resolvedPath)
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                CreateNoWindow = true,
            };

            using var process = Process.Start(psi);
            Assert.NotNull(process);
            var output = await process.StandardOutput.ReadToEndAsync(TestContext.Current.CancellationToken);
            await process.WaitForExitAsync(TestContext.Current.CancellationToken);

            Assert.Equal(0, process.ExitCode);
            Assert.Equal("hello", output.Trim());
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
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
