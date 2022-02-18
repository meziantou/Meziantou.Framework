using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using FluentAssertions;
using FluentAssertions.Execution;
using TestUtilities;
using Xunit;
using Xunit.Abstractions;
using Xunit.Sdk;

namespace Meziantou.Framework.CommandLineTests
{
    public class CommandLineBuilderTests
    {
        private readonly ITestOutputHelper _testOutputHelper;

        public CommandLineBuilderTests(ITestOutputHelper testOutputHelper)
        {
            _testOutputHelper = testOutputHelper;
        }

        public static TheoryData<string, string> GetArguments()
        {
            var result = new TheoryData<string, string>();
            Add(@"a");
            Add(@"arg 1");
            Add(@"\some\path with\spaces");
            Add(@"a\\b");
            Add(@"a\\\\b");
            Add(@"""a");
            Add(@"a|b");
            Add(@"ab|");
            Add(@"|ab");
            Add(@"^ab");
            Add(@"a^b");
            Add(@"ab^");
            Add(@"malicious argument"" & whoami");
            Add(@"""malicious-argument\^""^&whoami""");
            return result;

            void Add(string value) => result.Add(value, value);
        }

        [Theory]
        [MemberData(nameof(GetArguments))]
        public void WindowsQuotedArgument_Test(string value, string expected)
        {
            var args = CommandLineBuilder.WindowsQuotedArgument(value);
            var path = GetArgumentPrinterPath();

            ValidateArguments("dotnet", "\"" + path + "\" " + args, new[] { expected });
        }

        [RunIfTheory(FactOperatingSystem.Windows)]
        [MemberData(nameof(GetArguments))]
        public void WindowsCmdArgument_Test(string value, string expected)
        {
            var args = CommandLineBuilder.WindowsCmdArgument(value);
            var batPath = FullPath.Combine(Path.GetTempPath(), Guid.NewGuid() + ".cmd");

            var path = GetArgumentPrinterPath();
            var fileContent = "dotnet \"" + path + "\" " + args;
            File.WriteAllText(batPath, fileContent);

            var cmdArguments = "/Q /C \"" + batPath + "\"";

            _testOutputHelper.WriteLine($"Executing 'cmd.exe' '{cmdArguments}' with batch content:\n{fileContent}");
            ValidateArguments("cmd.exe", cmdArguments, new[] { expected });
        }

        private FullPath GetArgumentPrinterPath()
        {
            var fileName = "ArgumentsPrinter.dll";
            var testedPaths = new List<FullPath>();

            var configurations = new[] { "Debug", "Release" };
            foreach (var configuration in configurations)
            {
                var path = FullPath.Combine(Environment.CurrentDirectory, "..", "..", "..", "..", "ArgumentsPrinter", "bin", configuration, "net7.0", fileName);
                if (File.Exists(path))
                {
                    _testOutputHelper.WriteLine($"Use ArgumentsPrinter located at '{path}'");
                    return path;
                }

                testedPaths.Add(path);
            }

            var existingFiles = new List<string>();
            foreach (var testedPath in testedPaths)
            {
                var path = testedPath.Parent;
                if (Directory.Exists(path))
                {
                    existingFiles.AddRange(Directory.GetFiles(path, "*", SearchOption.AllDirectories));
                }
            }

            existingFiles.Sort(StringComparer.Ordinal);
            throw new XunitException($"File not found:\n{string.Join("\n", testedPaths)}\n. List of existing files:\n{string.Join("\n", existingFiles)}\nHave you built the ArgumentsPrinter project?");
        }

        private void ValidateArguments(string fileName, string arguments, string[] expectedArguments)
        {
            var psi = new ProcessStartInfo
            {
                FileName = fileName,
                Arguments = arguments,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
            };
            // https://github.com/Microsoft/vstest/issues/1263
            psi.EnvironmentVariables["COR_ENABLE_PROFILING"] = "0";

            _testOutputHelper.WriteLine($"Executing '{fileName}' '{arguments}'");
            using var process = Process.Start(psi);
            process.WaitForExit();

            var errors = process.StandardError.ReadToEnd();
            errors.Should().BeNullOrEmpty();

            var actualArguments = process.StandardOutput.ReadToEnd().Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            _testOutputHelper.WriteLine("----------");
            foreach (var arg in actualArguments)
            {
                _testOutputHelper.WriteLine(arg);
            }

            using (new AssertionScope())
            {
                process.ExitCode.Should().Be(0);
                actualArguments.Should().BeEquivalentTo(expectedArguments);
            }
        }
    }
}
