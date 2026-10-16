using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Testing;
using GlobalizationSensitivityAnalyzerType = Meziantou.Framework.Analyzers.GlobalizationSensitivity.GlobalizationSensitivityAnalyzer;

namespace Meziantou.Framework.Tests;

public sealed class GlobalizationSensitivityAnalyzerTests
{
    private static readonly ReferenceAssemblies Net11 = new("net11.0", new PackageIdentity("Microsoft.NETCore.App.Ref", "11.0.0-rc.2.26504.105"), Path.Combine("ref", "net11.0"));

    [Theory]
    [InlineData("""CultureInfo.GetCultureInfo("fr-FR")""")]
    [InlineData("""CultureInfo.GetCultureInfo(name)""")]
    [InlineData("""CultureInfo.CreateSpecificCulture("fr")""")]
    [InlineData("""CultureInfo.GetCultureInfoByIetfLanguageTag("fr-FR")""")]
    [InlineData("""CultureInfo.GetCultures(CultureTypes.AllCultures)""")]
    [InlineData("""new CultureInfo("fr-FR")""")]
    [InlineData("""new CultureInfo(1036)""")]
    [InlineData("""new RegionInfo("FR")""")]
    [InlineData("""RegionInfo.CurrentRegion""")]
    [InlineData("""CompareInfo.GetCompareInfo("fr-FR")""")]
    [InlineData("""StringComparer.InvariantCulture""")]
    [InlineData("""StringComparer.CurrentCultureIgnoreCase""")]
    [InlineData("""StringComparer.Create(CultureInfo.InvariantCulture, ignoreCase: true)""")]
    [InlineData("""StringComparison.InvariantCultureIgnoreCase""")]
    [InlineData("""StringComparison.CurrentCulture""")]
    [InlineData("""Comparer<string>.Default""")]
    [InlineData("""System.Collections.Comparer.Default""")]
    [InlineData("""System.Collections.Comparer.DefaultInvariant""")]
    [InlineData("""System.Collections.CaseInsensitiveComparer.DefaultInvariant""")]
    [InlineData("""CultureInfo.InvariantCulture.CompareInfo.Compare(name, "a", CompareOptions.IgnoreCase)""")]
    [InlineData("""CultureInfo.InvariantCulture.CompareInfo.IndexOf(name, "a")""")]
    [InlineData("""CultureInfo.InvariantCulture.CompareInfo.IsPrefix(name, "a", options)""")]
    [InlineData("""string.Compare(name, "a")""")]
    [InlineData("""string.Compare(name, "a", ignoreCase: true, CultureInfo.InvariantCulture)""")]
    [InlineData("""string.Compare(name, "a", CultureInfo.InvariantCulture, CompareOptions.None)""")]
    [InlineData("""name.CompareTo("a")""")]
    [InlineData("""name.StartsWith("a")""")]
    [InlineData("""name.EndsWith("a", ignoreCase: true, CultureInfo.InvariantCulture)""")]
    [InlineData("""name.IndexOf("a")""")]
    [InlineData("""name.LastIndexOf("a", 2)""")]
    [InlineData("""name.Replace("a", "b", ignoreCase: true, CultureInfo.InvariantCulture)""")]
    [InlineData("""name.Normalize()""")]
    [InlineData("""name.IsNormalized(NormalizationForm.FormD)""")]
    [InlineData("""name.AsSpan().IsNormalized()""")]
    [InlineData("""TimeZoneInfo.FindSystemTimeZoneById(name)""")]
    [InlineData("""TimeZoneInfo.TryConvertIanaIdToWindowsId(name, out _)""")]
    [InlineData("""TimeZoneInfo.GetSystemTimeZones()""")]
    [InlineData("""TimeZoneInfo.Utc.DisplayName""")]
    [InlineData("""new IdnMapping()""")]
    [InlineData("""new Uri("https://example.com").IdnHost""")]
    public Task ReportsIcuDependentApi(string expression)
    {
        return VerifyAsync(CreateSource($$"""{|MFGS0001:{{expression}}|}"""));
    }

    [Theory]
    [InlineData("""CultureInfo.InvariantCulture""")]
    [InlineData("""CultureInfo.CurrentCulture""")]
    [InlineData("""CultureInfo.GetCultureInfo("")""")]
    [InlineData("""new CultureInfo("")""")]
    [InlineData("""new CultureInfo(0x007F)""")]
    [InlineData("""StringComparer.Ordinal""")]
    [InlineData("""StringComparer.OrdinalIgnoreCase""")]
    [InlineData("""StringComparison.Ordinal""")]
    [InlineData("""Comparer<int>.Default""")]
    [InlineData("""CultureInfo.InvariantCulture.CompareInfo.Compare(name, "a", CompareOptions.Ordinal)""")]
    [InlineData("""CultureInfo.InvariantCulture.CompareInfo.IndexOf(name, "a", CompareOptions.OrdinalIgnoreCase)""")]
    [InlineData("""CultureInfo.InvariantCulture.CompareInfo.GetHashCode()""")]
    [InlineData("""string.Compare(name, "a", StringComparison.Ordinal)""")]
    [InlineData("""string.CompareOrdinal(name, "a")""")]
    [InlineData("""name.StartsWith('a')""")]
    [InlineData("""name.IndexOf('a')""")]
    [InlineData("""name.IndexOf("a", StringComparison.OrdinalIgnoreCase)""")]
    [InlineData("""name.Replace("a", "b")""")]
    [InlineData("""name.ToUpperInvariant()""")]
    [InlineData("""name.ToLower(CultureInfo.InvariantCulture)""")]
    [InlineData("""name.Contains("a")""")]
    [InlineData("""TimeZoneInfo.Utc.Id""")]
    [InlineData("""TimeZoneInfo.Local.GetUtcOffset(DateTime.UtcNow)""")]
    [InlineData("""new Uri("https://example.com").Host""")]
    [InlineData("""1.5.ToString(CultureInfo.InvariantCulture)""")]
    public Task DoesNotReportIcuIndependentApi(string expression)
    {
        return VerifyAsync(CreateSource(expression));
    }

    [Fact]
    public Task DoesNotReportStringComparisonInPatterns()
    {
        return VerifyAsync("""
            using System;

            static class Sample
            {
                static bool IsOrdinal(StringComparison comparison)
                {
                    switch (comparison)
                    {
                        case StringComparison.CurrentCulture:
                            return false;
                    }

                    return comparison is not (StringComparison.InvariantCulture or StringComparison.InvariantCultureIgnoreCase) && comparison switch
                    {
                        StringComparison.CurrentCultureIgnoreCase => false,
                        _ => true,
                    };
                }
            }
            """);
    }

    [Fact]
    public Task DoesNotReportSuppressedDiagnostic()
    {
        return VerifyAsync("""
            using System.Diagnostics.CodeAnalysis;
            using System.Globalization;

            static class Sample
            {
                [SuppressMessage("Globalization", "MFGS0001", Justification = "Only called with ASCII header names")]
                static int M(string name) => CultureInfo.InvariantCulture.CompareInfo.Compare(name, "a", CompareOptions.IgnoreCase);
            }
            """);
    }

    [Fact]
    public Task DoesNotReportWhenProjectIsGlobalizationSensitive()
    {
        return VerifyAsync(CreateSource("""CultureInfo.GetCultureInfo("fr-FR")"""), globalizationSensitive: true);
    }

    [Fact]
    public async Task ReportsTestProjectReferencingGlobalizationSensitiveAssembly()
    {
        var test = CreateTest("class Sample { }", isTestProject: true);
        test.TestState.AdditionalReferences.Add(await CreateLibraryReferenceAsync("Sensitive", isGlobalizationSensitive: true));
        test.TestState.AdditionalReferences.Add(await CreateLibraryReferenceAsync("NotSensitive", isGlobalizationSensitive: false));
        test.ExpectedDiagnostics.Add(new DiagnosticResult(GlobalizationSensitivityAnalyzerType.GlobalizationSensitiveReferenceRule).WithNoLocation().WithArguments("Sensitive"));
        await test.RunAsync(XunitCancellationToken);
    }

    [Fact]
    public async Task DoesNotReportTestProjectReferencingOnlyInsensitiveAssemblies()
    {
        var test = CreateTest("class Sample { }", isTestProject: true);
        test.TestState.AdditionalReferences.Add(await CreateLibraryReferenceAsync("NotSensitive", isGlobalizationSensitive: false));
        await test.RunAsync(XunitCancellationToken);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, true)]
    public async Task DoesNotReportGlobalizationSensitiveAssemblyReference(bool isTestProject, bool globalizationSensitive)
    {
        var test = CreateTest("class Sample { }", isTestProject, globalizationSensitive);
        test.TestState.AdditionalReferences.Add(await CreateLibraryReferenceAsync("Sensitive", isGlobalizationSensitive: true));
        await test.RunAsync(XunitCancellationToken);
    }

    private static string CreateSource(string expression)
    {
        return $$"""
            using System;
            using System.Collections.Generic;
            using System.Globalization;
            using System.Text;

            static class Sample
            {
                static object M(string name, CompareOptions options)
                {
                    return {{expression}};
                }
            }
            """;
    }

    private static Task VerifyAsync(string source, bool globalizationSensitive = false)
    {
        return CreateTest(source, isTestProject: false, globalizationSensitive).RunAsync(XunitCancellationToken);
    }

    private static CSharpAnalyzerTest<GlobalizationSensitivityAnalyzerType, DefaultVerifier> CreateTest(string source, bool isTestProject, bool globalizationSensitive = false)
    {
        var test = new CSharpAnalyzerTest<GlobalizationSensitivityAnalyzerType, DefaultVerifier>
        {
            TestCode = source,
            ReferenceAssemblies = Net11,
        };

        test.TestState.AnalyzerConfigFiles.Add(("/.globalconfig", $"""
            is_global = true
            build_property.IsTestProject = {(isTestProject ? "true" : "")}
            build_property.GlobalizationSensitive = {(globalizationSensitive ? "true" : "")}
            """));
        return test;
    }

    private static async Task<MetadataReference> CreateLibraryReferenceAsync(string assemblyName, bool isGlobalizationSensitive)
    {
        var source = isGlobalizationSensitive
            ? $"""[assembly: System.Reflection.AssemblyMetadata("{GlobalizationSensitivityAnalyzerType.MarkerMetadataKey}", "true")]"""
            : """[assembly: System.Reflection.AssemblyMetadata("Other", "true")]""";

        var frameworkReferences = await Net11.ResolveAsync(LanguageNames.CSharp, XunitCancellationToken);
        var compilation = CSharpCompilation.Create(
            assemblyName,
            [CSharpSyntaxTree.ParseText(source, cancellationToken: XunitCancellationToken)],
            frameworkReferences,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        using var stream = new MemoryStream();
        var result = compilation.Emit(stream, cancellationToken: XunitCancellationToken);
        if (!result.Success)
            throw new InvalidOperationException("The library does not compile: " + string.Join(Environment.NewLine, result.Diagnostics));

        return MetadataReference.CreateFromImage(stream.ToArray());
    }
}
