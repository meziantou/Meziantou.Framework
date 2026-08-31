using Xunit.Sdk;

namespace Meziantou.Framework.EmbeddedConstantsGenerator.Tests;

public sealed class EmbeddedConstantsGeneratorTests(EmbeddedConstantsGeneratorPackageFixture fixture) : IClassFixture<EmbeddedConstantsGeneratorPackageFixture>
{
    [Fact]
    public async Task GenerateOnBuild_TextKind_UsesDefaultNamespaceAndGeneratesStringOnly()
    {
        await using var temporaryDirectory = TemporaryDirectory.Create();
        var projectDirectory = temporaryDirectory.CreateDirectory("text");
        CreateGlobalJson(projectDirectory, fixture.DotnetSdkVersion);
        CreateNuGetConfig(projectDirectory, fixture.PackagesDirectory);

        temporaryDirectory.CreateTextFile("text/Sample.csproj", $$"""
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net10.0</TargetFramework>
                <RootNamespace>MyApp.DefaultNamespace</RootNamespace>
                <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
                <TreatsWarningsAsErrors>true</TreatsWarningsAsErrors>
              </PropertyGroup>

              <ItemGroup>
                <PackageReference Include="{{EmbeddedConstantsGeneratorPackageFixture.PackageName}}" Version="{{fixture.PackageVersion}}" PrivateAssets="all" />
                <EmbeddedConstant Include="Assets/hello.txt" Kind="Text" />
              </ItemGroup>

              <Target Name="CaptureEmbeddedConstantsBinlogItems" AfterTargets="GenerateEmbeddedConstants">
                <WriteLinesToFile File="$(IntermediateOutputPath)embed-items.txt" Lines="@(EmbedInBinlog)" Overwrite="true" />
                <WriteLinesToFile File="$(IntermediateOutputPath)uptodate-items.txt" Lines="@(UpToDateCheckInput)" Overwrite="true" />
              </Target>
            </Project>
            """);
        temporaryDirectory.CreateTextFile("text/Consumer.cs", """
            namespace Demo;

            public static class Consumer
            {
                public static string Value => MyApp.DefaultNamespace.EmbeddedConstants.HelloText;
            }
            """);
        temporaryDirectory.CreateTextFile("text/Assets/hello.txt", "Hello");

        await RunDotNetCommand(projectDirectory, ["restore", "--disable-build-servers"], expectedExitCode: 0);
        await RunDotNetCommand(projectDirectory, ["build", "--no-restore", "--disable-build-servers", "-nologo", "/bl:build.binlog"], expectedExitCode: 0);

        var generatedFilePath = GetGeneratedFilePath(projectDirectory);
        var generatedSource = await File.ReadAllTextAsync(generatedFilePath, XunitCancellationToken);
        Assert.Contains("namespace MyApp.DefaultNamespace;", generatedSource);
        Assert.Contains("public const string HelloText = \"Hello\";", generatedSource);
        Assert.DoesNotContain("HelloBytes", generatedSource);

        var embedItemsPath = Assert.Single(Directory.GetFiles(projectDirectory / "obj", "embed-items.txt", SearchOption.AllDirectories));
        var embedItems = await File.ReadAllTextAsync(embedItemsPath, XunitCancellationToken);
        Assert.Contains(generatedFilePath, embedItems, ignoreCase: true);

        // The Visual Studio fast up-to-date check skips the build unless the embedded files are declared as inputs
        var upToDateItemsPath = Assert.Single(Directory.GetFiles(projectDirectory / "obj", "uptodate-items.txt", SearchOption.AllDirectories));
        var upToDateItems = await File.ReadAllTextAsync(upToDateItemsPath, XunitCancellationToken);
        Assert.Contains("hello.txt", upToDateItems);

        await RunDotNetCommand(projectDirectory, ["clean", "--disable-build-servers", "-nologo"], expectedExitCode: 0);

        Assert.False(File.Exists(generatedFilePath));
    }

    [Fact]
    public async Task GenerateOnBuild_BinaryKind_GeneratesBytesOnly()
    {
        await using var temporaryDirectory = TemporaryDirectory.Create();
        var projectDirectory = temporaryDirectory.CreateDirectory("binary");
        CreateGlobalJson(projectDirectory, fixture.DotnetSdkVersion);
        CreateNuGetConfig(projectDirectory, fixture.PackagesDirectory);

        temporaryDirectory.CreateTextFile("binary/Sample.csproj", $$"""
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net10.0</TargetFramework>
                <EmbeddedConstantsNamespace>Generated</EmbeddedConstantsNamespace>
              </PropertyGroup>

              <ItemGroup>
                <PackageReference Include="{{EmbeddedConstantsGeneratorPackageFixture.PackageName}}" Version="{{fixture.PackageVersion}}" PrivateAssets="all" />
                <EmbeddedConstant Include="Assets/logo.bin" Kind="Binary" />
              </ItemGroup>
            </Project>
            """);
        temporaryDirectory.CreateTextFile("binary/Consumer.cs", """
            namespace Demo;

            public static class Consumer
            {
                public static int Length => Generated.EmbeddedConstants.LogoBytes.Length;
            }
            """);
        // More than 16 bytes so the byte array spans several lines
        CreateBinaryFile(projectDirectory / "Assets" / "logo.bin", [0x89, 0x50, 0x4E, 0x47, .. Enumerable.Range(0, 16).Select(i => (byte)i)]);

        await RunDotNetCommand(projectDirectory, ["restore", "--disable-build-servers"], expectedExitCode: 0);
        await RunDotNetCommand(projectDirectory, ["build", "--no-restore", "--disable-build-servers", "-nologo"], expectedExitCode: 0);

        var generatedSource = await File.ReadAllTextAsync(GetGeneratedFilePath(projectDirectory), XunitCancellationToken);
        Assert.DoesNotContain("LogoText", generatedSource);
        Assert.Contains("public static global::System.ReadOnlySpan<byte> LogoBytes => new byte[]", generatedSource);
        Assert.Contains("0x89, 0x50, 0x4E, 0x47", generatedSource);
        AssertNoTrailingWhitespace(generatedSource);
    }

    [Fact]
    public async Task GenerateOnBuild_BothKind_GeneratesStringAndBytes()
    {
        await using var temporaryDirectory = TemporaryDirectory.Create();
        var projectDirectory = temporaryDirectory.CreateDirectory("both");
        CreateGlobalJson(projectDirectory, fixture.DotnetSdkVersion);
        CreateNuGetConfig(projectDirectory, fixture.PackagesDirectory);

        temporaryDirectory.CreateTextFile("both/Sample.csproj", $$"""
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net10.0</TargetFramework>
                <EmbeddedConstantsNamespace>Generated</EmbeddedConstantsNamespace>
                <EmbeddedConstantsClassName>EmbeddedFiles</EmbeddedConstantsClassName>
              </PropertyGroup>

              <ItemGroup>
                <PackageReference Include="{{EmbeddedConstantsGeneratorPackageFixture.PackageName}}" Version="{{fixture.PackageVersion}}" PrivateAssets="all" />
                <EmbeddedConstant Include="Assets/config.json" Kind="Both" Name="ConfigJson" />
              </ItemGroup>
            </Project>
            """);
        temporaryDirectory.CreateTextFile("both/Consumer.cs", """
            namespace Demo;

            public static class Consumer
            {
                public static string Text => Generated.EmbeddedFiles.ConfigJsonText;
                public static int Length => Generated.EmbeddedFiles.ConfigJsonBytes.Length;
            }
            """);
        temporaryDirectory.CreateTextFile("both/Assets/config.json", "{}");

        await RunDotNetCommand(projectDirectory, ["restore", "--disable-build-servers"], expectedExitCode: 0);
        await RunDotNetCommand(projectDirectory, ["build", "--no-restore", "--disable-build-servers", "-nologo"], expectedExitCode: 0);

        var generatedSource = await File.ReadAllTextAsync(GetGeneratedFilePath(projectDirectory), XunitCancellationToken);
        Assert.Contains("public const string ConfigJsonText = \"{}\";", generatedSource);
        Assert.Contains("public static global::System.ReadOnlySpan<byte> ConfigJsonBytes => new byte[]", generatedSource);

        // The two members of the first entry are separated like every other pair of members
        Assert.Contains(
            "public const string ConfigJsonText = \"{}\";\n\n    public static global::System.ReadOnlySpan<byte> ConfigJsonBytes",
            generatedSource.ReplaceLineEndings("\n"));
        AssertNoTrailingWhitespace(generatedSource);
    }

    [Theory]
    [InlineData("Binary")]
    [InlineData("Both")]
    public async Task GenerateOnBuild_FileStartsWithByteOrderMark_ByteMemberKeepsIt(string kind)
    {
        await using var temporaryDirectory = TemporaryDirectory.Create();
        var projectDirectory = temporaryDirectory.CreateDirectory("bom-" + kind);
        CreateGlobalJson(projectDirectory, fixture.DotnetSdkVersion);
        CreateNuGetConfig(projectDirectory, fixture.PackagesDirectory);

        temporaryDirectory.CreateTextFile($"bom-{kind}/Sample.csproj", CreateProjectFile(fixture, $"""
                <EmbeddedConstant Include="Assets/config.json" Kind="{kind}" />
            """));

        // A UTF-8 byte order mark followed by {}
        CreateBinaryFile(projectDirectory / "Assets" / "config.json", [0xEF, 0xBB, 0xBF, (byte)'{', (byte)'}']);

        await RunDotNetCommand(projectDirectory, ["restore", "--disable-build-servers"], expectedExitCode: 0);
        await RunDotNetCommand(projectDirectory, ["build", "--no-restore", "--disable-build-servers", "-nologo"], expectedExitCode: 0);

        var generatedSource = await File.ReadAllTextAsync(GetGeneratedFilePath(projectDirectory), XunitCancellationToken);

        // The byte member is the file, byte for byte, whichever Kind asked for it
        Assert.Contains("0xEF, 0xBB, 0xBF, 0x7B, 0x7D", generatedSource);

        if (kind is "Both")
        {
            // Only the text member drops the byte order mark
            Assert.Contains("public const string ConfigText = \"{}\";", generatedSource);
        }
    }

    [Fact]
    public async Task GenerateOnBuild_UnsupportedKind_FailsBuild()
    {
        await using var temporaryDirectory = TemporaryDirectory.Create();
        var projectDirectory = temporaryDirectory.CreateDirectory("unsupported-kind");
        CreateGlobalJson(projectDirectory, fixture.DotnetSdkVersion);
        CreateNuGetConfig(projectDirectory, fixture.PackagesDirectory);

        temporaryDirectory.CreateTextFile("unsupported-kind/Sample.csproj", CreateProjectFile(fixture, """
                <EmbeddedConstant Include="Assets/sample.txt" Kind="Other" />
            """));
        temporaryDirectory.CreateTextFile("unsupported-kind/Assets/sample.txt", "Hello");

        await RunDotNetCommand(projectDirectory, ["restore", "--disable-build-servers"], expectedExitCode: 0);
        var result = await RunDotNetCommand(projectDirectory, ["build", "--no-restore", "--disable-build-servers", "-nologo"], expectedExitCode: 1);

        Assert.Contains("MFECG0004", string.Join('\n', result.Output));
    }

    [Fact]
    public async Task GenerateOnBuild_MissingKind_FailsBuild()
    {
        await using var temporaryDirectory = TemporaryDirectory.Create();
        var projectDirectory = temporaryDirectory.CreateDirectory("missing-kind");
        CreateGlobalJson(projectDirectory, fixture.DotnetSdkVersion);
        CreateNuGetConfig(projectDirectory, fixture.PackagesDirectory);

        temporaryDirectory.CreateTextFile("missing-kind/Sample.csproj", CreateProjectFile(fixture, """
                <EmbeddedConstant Include="Assets/sample.txt" />
            """));
        temporaryDirectory.CreateTextFile("missing-kind/Assets/sample.txt", "Hello");

        await RunDotNetCommand(projectDirectory, ["restore", "--disable-build-servers"], expectedExitCode: 0);
        var result = await RunDotNetCommand(projectDirectory, ["build", "--no-restore", "--disable-build-servers", "-nologo"], expectedExitCode: 1);

        Assert.Contains("MFECG0003", string.Join('\n', result.Output));
    }

    [Fact]
    public async Task GenerateOnBuild_DuplicateNames_FailsBuild()
    {
        await using var temporaryDirectory = TemporaryDirectory.Create();
        var projectDirectory = temporaryDirectory.CreateDirectory("duplicate");
        CreateGlobalJson(projectDirectory, fixture.DotnetSdkVersion);
        CreateNuGetConfig(projectDirectory, fixture.PackagesDirectory);

        temporaryDirectory.CreateTextFile("duplicate/Sample.csproj", CreateProjectFile(fixture, """
                <EmbeddedConstant Include="Assets/first.txt" Kind="Both" Name="Sample" />
                <EmbeddedConstant Include="Assets/second.txt" Kind="Both" Name="Sample" />
            """));
        temporaryDirectory.CreateTextFile("duplicate/Assets/first.txt", "First");
        temporaryDirectory.CreateTextFile("duplicate/Assets/second.txt", "Second");

        await RunDotNetCommand(projectDirectory, ["restore", "--disable-build-servers"], expectedExitCode: 0);
        var result = await RunDotNetCommand(projectDirectory, ["build", "--no-restore", "--disable-build-servers", "-nologo"], expectedExitCode: 1);

        Assert.Contains("MFECG0005", string.Join('\n', result.Output));
    }

    [Fact]
    public async Task GenerateOnBuild_OutputPathCannotBeWritten_ReportsDiagnosticInsteadOfUnhandledException()
    {
        await using var temporaryDirectory = TemporaryDirectory.Create();
        var projectDirectory = temporaryDirectory.CreateDirectory("unwritable-output-path");
        CreateGlobalJson(projectDirectory, fixture.DotnetSdkVersion);
        CreateNuGetConfig(projectDirectory, fixture.PackagesDirectory);

        temporaryDirectory.CreateTextFile("unwritable-output-path/Sample.csproj", $$"""
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net10.0</TargetFramework>
                <EmbeddedConstantsNamespace>Generated</EmbeddedConstantsNamespace>
                <EmbeddedConstantsOutputPath>Generated/EmbeddedConstants.g.cs</EmbeddedConstantsOutputPath>
              </PropertyGroup>

              <ItemGroup>
                <PackageReference Include="{{EmbeddedConstantsGeneratorPackageFixture.PackageName}}" Version="{{fixture.PackageVersion}}" PrivateAssets="all" />
                <EmbeddedConstant Include="Assets/sample.txt" Kind="Text" />
              </ItemGroup>
            </Project>
            """);
        temporaryDirectory.CreateTextFile("unwritable-output-path/Assets/sample.txt", "Hello");

        // The generated file path is an existing directory, so writing it throws inside the task
        Directory.CreateDirectory(projectDirectory / "Generated" / "EmbeddedConstants.g.cs");

        await RunDotNetCommand(projectDirectory, ["restore", "--disable-build-servers"], expectedExitCode: 0);
        var result = await RunDotNetCommand(projectDirectory, ["build", "--no-restore", "--disable-build-servers", "-nologo"], expectedExitCode: 1);

        var output = string.Join('\n', result.Output);
        Assert.Contains("MFECG0011", output);
        Assert.DoesNotContain("MSB4018", output);
    }

    private static string CreateProjectFile(EmbeddedConstantsGeneratorPackageFixture fixture, string embeddedConstants)
    {
        return $$"""
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net10.0</TargetFramework>
              </PropertyGroup>

              <ItemGroup>
                <PackageReference Include="{{EmbeddedConstantsGeneratorPackageFixture.PackageName}}" Version="{{fixture.PackageVersion}}" PrivateAssets="all" />
            {{embeddedConstants}}
              </ItemGroup>
            </Project>
            """;
    }

    private static void AssertNoTrailingWhitespace(string generatedSource)
    {
        var lines = generatedSource.ReplaceLineEndings("\n").Split('\n');
        Assert.DoesNotContain(lines, line => line != line.TrimEnd());
    }

    private static FullPath GetGeneratedFilePath(FullPath projectDirectory)
    {
        return FullPath.FromPath(Assert.Single(Directory.GetFiles(projectDirectory / "obj", "EmbeddedConstants.g.cs", SearchOption.AllDirectories)));
    }

    private static void CreateBinaryFile(FullPath path, byte[] content)
    {
        Directory.CreateDirectory(path.Parent);
        File.WriteAllBytes(path, content);
    }

    private static void CreateGlobalJson(FullPath projectDirectory, string dotnetSdkVersion)
    {
        File.WriteAllText(projectDirectory / "global.json", $$"""
            {
              "sdk": {
                "version": "{{dotnetSdkVersion}}",
                "rollForward": "latestFeature"
              }
            }
            """);
    }

    private static void CreateNuGetConfig(FullPath projectDirectory, FullPath packagesDirectory)
    {
        var packagesCachePath = packagesDirectory / "global-packages";
        File.WriteAllText(projectDirectory / "NuGet.config", $$"""
            <?xml version="1.0" encoding="utf-8"?>
            <configuration>
              <config>
                <add key="globalPackagesFolder" value="{{packagesCachePath}}" />
              </config>
              <packageSources>
                <clear />
                <add key="local" value="{{packagesDirectory}}" />
                <add key="nuget.org" value="https://api.nuget.org/v3/index.json" />
              </packageSources>
              <packageSourceMapping>
                <packageSource key="local">
                  <package pattern="{{EmbeddedConstantsGeneratorPackageFixture.PackageName}}*" />
                </packageSource>
                <packageSource key="nuget.org">
                  <package pattern="*" />
                </packageSource>
              </packageSourceMapping>
            </configuration>
            """);
    }

    private static async Task<BufferedProcessResult> RunDotNetCommand(FullPath workingDirectory, IReadOnlyList<string> arguments, int expectedExitCode)
    {
        var result = await ProcessWrapper.Create("dotnet")
            .WithArguments(arguments)
            .WithWorkingDirectory(workingDirectory)
            .WithEnvironmentVariables(env => env.Set("DOTNET_SKIP_FIRST_TIME_EXPERIENCE", "1"))
            .WithValidation(ProcessValidationMode.None)
            .ExecuteBufferedAsync(XunitCancellationToken);

        if (result.ExitCode != expectedExitCode)
        {
            var output = string.Join('\n', result.Output);
            throw new XunitException($"Command failed: dotnet {string.Join(' ', arguments)}\nExpected exit code: {expectedExitCode}\nActual exit code: {result.ExitCode}\nOutput:\n{output}");
        }

        return result;
    }
}
