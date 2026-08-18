using System.IO.Compression;
using Xunit.Sdk;

namespace Meziantou.Framework.Roslyn.Tests;

public sealed class RoslynPackageTests(RoslynPackageFixture fixture) : IClassFixture<RoslynPackageFixture>
{
    [Fact]
    public void Package_ContainsSourceAndBuildTransitiveTargets()
    {
        using var package = ZipFile.OpenRead(fixture.PackagePath);

        Assert.Contains("GetBestTypeByMetadataName", ReadEntryText(AssertEntry(package, "contentFiles/cs/any/Meziantou.Framework.Roslyn/CompilationExtensions.cs")));
        Assert.Contains("ReportDiagnostic", ReadEntryText(AssertEntry(package, "contentFiles/cs/any/Meziantou.Framework.Roslyn/ContextExtensions.cs")));
        Assert.Contains("SyntaxNodeAnalysisContext", ReadEntryText(AssertEntry(package, "contentFiles/cs/any/Meziantou.Framework.Roslyn/ContextExtensions.g.cs")));
        Assert.Contains("ContextExtensions", ReadEntryText(AssertEntry(package, "contentFiles/cs/any/Meziantou.Framework.Roslyn/ContextExtensions.tt")));
        Assert.Contains("DiagnosticReporter", ReadEntryText(AssertEntry(package, "contentFiles/cs/any/Meziantou.Framework.Roslyn/DiagnosticReporter.cs")));
        Assert.Contains("IsInterfaceImplementation", ReadEntryText(AssertEntry(package, "contentFiles/cs/any/Meziantou.Framework.Roslyn/MethodSymbolExtensions.cs")));
        Assert.Contains("IsNamespace", ReadEntryText(AssertEntry(package, "contentFiles/cs/any/Meziantou.Framework.Roslyn/NamespaceSymbolExtensions.cs")));
        Assert.Contains("UnwrapImplicitConversions", ReadEntryText(AssertEntry(package, "contentFiles/cs/any/Meziantou.Framework.Roslyn/OperationExtensions.cs")));
        Assert.Contains("TryFindNode", ReadEntryText(AssertEntry(package, "contentFiles/cs/any/Meziantou.Framework.Roslyn/SuppressorHelpers.cs")));
        Assert.Contains("IsVisibleOutsideOfAssembly", ReadEntryText(AssertEntry(package, "contentFiles/cs/any/Meziantou.Framework.Roslyn/SymbolExtensions.cs")));
        Assert.Contains("GetFirstAttribute", ReadEntryText(AssertEntry(package, "contentFiles/cs/any/Meziantou.Framework.Roslyn/SymbolAttributeExtensions.cs")));
        Assert.Contains("GetUnderlyingNullableTypeOrSelf", ReadEntryText(AssertEntry(package, "contentFiles/cs/any/Meziantou.Framework.Roslyn/TypeSymbolExtensions.cs")));
        AssertEntry(package, "buildTransitive/Meziantou.Framework.Roslyn.targets");
        Assert.DoesNotContain(package.Entries, entry => entry.FullName.StartsWith("lib/", StringComparison.Ordinal));
        Assert.DoesNotContain(package.Entries, entry => entry.FullName.StartsWith("_manifest/", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Package_AddsSourceAndConstants()
    {
        await using var temporaryDirectory = TemporaryDirectory.Create();
        var projectDirectory = temporaryDirectory.CreateDirectory("consumer");
        CreateGlobalJson(projectDirectory, fixture.DotnetSdkVersion);
        CreateNuGetConfig(projectDirectory, fixture.PackagesDirectory);

        temporaryDirectory.CreateTextFile("consumer/Sample.csproj", $$"""
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net10.0</TargetFramework>
                <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
                <TreatsWarningsAsErrors>true</TreatsWarningsAsErrors>
                <NoWarn>nullable</NoWarn>
              </PropertyGroup>

              <ItemGroup>
                <PackageReference Include="{{RoslynPackageFixture.PackageName}}" Version="{{fixture.PackageVersion}}" PrivateAssets="all" />
                <PackageReference Include="Microsoft.CodeAnalysis.CSharp" Version="5.6.0" PrivateAssets="all" />
              </ItemGroup>
            </Project>
            """);
        temporaryDirectory.CreateTextFile("consumer/Consumer.cs", CreateConsumerSourceWithConstantChecks(GetRoslyn56Constants()));

        await RunDotNetCommand(projectDirectory, ["restore", "--disable-build-servers"], expectedExitCode: 0);
        await RunDotNetCommand(projectDirectory, ["build", "--no-restore", "--disable-build-servers", "-nologo"], expectedExitCode: 0);

        var projectAssets = await File.ReadAllTextAsync(projectDirectory / "obj" / "project.assets.json", XunitCancellationToken);
        foreach (var sourceFileName in SourceFileNames)
        {
            Assert.Contains($"contentFiles/cs/any/Meziantou.Framework.Roslyn/{sourceFileName}", projectAssets);
        }
    }

    [Fact]
    public async Task Package_AddsConstantsWithCentralPackageManagement()
    {
        await using var temporaryDirectory = TemporaryDirectory.Create();
        var projectDirectory = temporaryDirectory.CreateDirectory("consumer-cpm");
        CreateGlobalJson(projectDirectory, fixture.DotnetSdkVersion);
        CreateNuGetConfig(projectDirectory, fixture.PackagesDirectory);

        temporaryDirectory.CreateTextFile("consumer-cpm/Directory.Packages.props", $$"""
            <Project>
              <PropertyGroup>
                <ManagePackageVersionsCentrally>true</ManagePackageVersionsCentrally>
              </PropertyGroup>

              <ItemGroup>
                <PackageVersion Include="{{RoslynPackageFixture.PackageName}}" Version="{{fixture.PackageVersion}}" />
                <PackageVersion Include="Microsoft.CodeAnalysis.CSharp" Version="5.6.0" />
              </ItemGroup>
            </Project>
            """);
        temporaryDirectory.CreateTextFile("consumer-cpm/Sample.csproj", $$"""
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net10.0</TargetFramework>
              </PropertyGroup>

              <ItemGroup>
                <PackageReference Include="{{RoslynPackageFixture.PackageName}}" PrivateAssets="all" />
                <PackageReference Include="Microsoft.CodeAnalysis.CSharp" PrivateAssets="all" />
              </ItemGroup>
            </Project>
            """);
        temporaryDirectory.CreateTextFile("consumer-cpm/Consumer.cs", CreateConstantChecksSource(GetRoslyn56Constants()));

        await RunDotNetCommand(projectDirectory, ["restore", "--disable-build-servers"], expectedExitCode: 0);
        await RunDotNetCommand(projectDirectory, ["build", "--no-restore", "--disable-build-servers", "-nologo"], expectedExitCode: 0);
    }

    private static ZipArchiveEntry AssertEntry(ZipArchive archive, string entryName)
    {
        var entry = archive.GetEntry(entryName);
        Assert.NotNull(entry);

        return entry;
    }

    private static string ReadEntryText(ZipArchiveEntry entry)
    {
        using var stream = entry.Open();
        using var reader = new StreamReader(stream);

        return reader.ReadToEnd();
    }

    private static void ExtractEntry(ZipArchiveEntry entry, FullPath destination)
    {
        using var stream = entry.Open();
        using var file = File.Create(destination);
        stream.CopyTo(file);
    }

    private static string CreateConsumerSourceWithConstantChecks(string[] expectedConstants)
    {
        return $$"""
            using System.Threading;
            using Microsoft.CodeAnalysis;
            using Microsoft.CodeAnalysis.CSharp;
            using Microsoft.CodeAnalysis.Operations;
            using Meziantou.Framework.Roslyn;

            {{CreateConstantChecks(expectedConstants)}}

            namespace Demo;

            internal static class Consumer
            {
                public static INamedTypeSymbol? GetBestType(Compilation compilation, string fullyQualifiedMetadataName) => compilation.GetBestTypeByMetadataName(fullyQualifiedMetadataName);
                public static bool IsNet9OrGreater(Compilation compilation) => compilation.IsNet9OrGreater();
                public static Location? GetLocation(ISymbol symbol) => symbol.GetFirstSourceLocation();
                public static bool HasAttribute(ISymbol symbol, ITypeSymbol attributeType) => symbol.HasAttribute(attributeType);
                public static bool IsVisible(ISymbol? symbol) => symbol.IsVisibleOutsideOfAssembly();
                public static bool IsInterfaceImplementation(IMethodSymbol symbol) => symbol.IsInterfaceImplementation();
                public static bool IsNamespace(INamespaceSymbol namespaceSymbol) => namespaceSymbol.IsNamespace(["System"]);
                public static IOperation Unwrap(IOperation operation) => operation.UnwrapImplicitConversions();
                public static IOperation UnwrapOperations(IOperation operation) => operation.UnwrapConversions();
                public static LanguageVersion GetOperationLanguageVersion(IOperation operation) => operation.GetCSharpLanguageVersion();
                public static SyntaxNode? TryFindNode(Diagnostic diagnostic, CancellationToken cancellationToken) => diagnostic.TryFindNode(cancellationToken);
                public static int? GetNodeLine(SyntaxNode node, CancellationToken cancellationToken) => node.GetLine(cancellationToken);
                public static bool IsCSharp12(LanguageVersion languageVersion) => languageVersion.IsCSharp12OrAbove();
                public static ITypeSymbol? GetFlowType(IOperation operation, CancellationToken cancellationToken) => LocalDataFlowAnalysis.GetActualType(operation, cancellationToken);
                public static string ReporterName => typeof(DiagnosticReporter).Name;
                public static DiagnosticInvocationReportOptions InvocationReportOptions => DiagnosticInvocationReportOptions.ReportOnMember | DiagnosticInvocationReportOptions.ReportOnArguments;
            }
            """;
    }

    private static string CreateConstantChecksSource(string[] expectedConstants)
    {
        return $$"""
            {{CreateConstantChecks(expectedConstants)}}

            namespace Demo;

            internal static class Consumer;
            """;
    }

    private static string CreateConstantChecks(string[] expectedConstants)
    {
        return string.Join('\n', expectedConstants.Select(constant => $"#if !{constant}\n#error Expected {constant}.\n#endif"));
    }

    private static string[] GetRoslyn56Constants()
    {
        return
        [
            .. GetStableRoslynConstants("5.6.0"),
            "CSHARP9_OR_GREATER",
            "CSHARP10_OR_GREATER",
            "CSHARP11_OR_GREATER",
            "CSHARP12_OR_GREATER",
            "CSHARP13_OR_GREATER",
            "CSHARP14_OR_GREATER",
            "CSHARP15_OR_GREATER",
        ];
    }

    private static string[] GetStableRoslynConstants(string maximumVersion)
    {
        return StableRoslynVersions
            .TakeWhile(version => string.Compare(version, maximumVersion, StringComparison.Ordinal) <= 0)
            .Select(GetRoslynConstantName)
            .ToArray();
    }

    private static string GetRoslynConstantName(string version)
    {
        if (version.EndsWith(".0", StringComparison.Ordinal))
        {
            version = version[..^2];
        }

        return "ROSLYN_" + version.Replace('.', '_') + "_OR_GREATER";
    }

    private static readonly string[] StableRoslynVersions =
    [
        "1.0.0",
        "1.0.1",
        "1.1.0",
        "1.1.1",
        "1.2.0",
        "1.2.1",
        "1.2.2",
        "1.3.0",
        "1.3.1",
        "1.3.2",
        "2.0.0",
        "2.1.0",
        "2.2.0",
        "2.3.0",
        "2.3.1",
        "2.3.2",
        "2.4.0",
        "2.6.0",
        "2.6.1",
        "2.7.0",
        "2.8.0",
        "2.8.2",
        "2.9.0",
        "2.10.0",
        "3.0.0",
        "3.1.0",
        "3.2.0",
        "3.2.1",
        "3.3.0",
        "3.3.1",
        "3.4.0",
        "3.5.0",
        "3.6.0",
        "3.7.0",
        "3.8.0",
        "3.9.0",
        "3.10.0",
        "3.11.0",
        "4.0.0",
        "4.0.1",
        "4.1.0",
        "4.2.0",
        "4.3.0",
        "4.3.1",
        "4.4.0",
        "4.5.0",
        "4.6.0",
        "4.7.0",
        "4.8.0",
        "4.9.2",
        "4.10.0",
        "4.11.0",
        "4.12.0",
        "4.13.0",
        "4.14.0",
        "5.0.0",
        "5.3.0",
        "5.6.0",
    ];

    private static readonly string[] SourceFileNames =
    [
        "CompilationExtensions.cs",
        "ContextExtensions.cs",
        "ContextExtensions.g.cs",
        "DiagnosticFieldReportOptions.cs",
        "DiagnosticInvocationReportOptions.cs",
        "DiagnosticMethodReportOptions.cs",
        "DiagnosticParameterReportOptions.cs",
        "DiagnosticPropertyReportOptions.cs",
        "DiagnosticReporter.cs",
        "LanguageVersionExtensions.cs",
        "LocalDataFlowAnalysis.cs",
        "LocationExtensions.cs",
        "MethodSymbolExtensions.cs",
        "NamespaceSymbolExtensions.cs",
        "OperationExtensions.cs",
        "SuppressorHelpers.cs",
        "SymbolAttributeExtensions.cs",
        "SymbolExtensions.cs",
        "TypeSymbolExtensions.cs",
    ];

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
                  <package pattern="{{RoslynPackageFixture.PackageName}}*" />
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
