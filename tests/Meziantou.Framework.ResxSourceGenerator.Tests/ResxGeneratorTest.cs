using System.Text;
using System.Xml.Linq;
using FluentAssertions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Text;
using TestUtilities;
using Xunit;

namespace Meziantou.Framework.ResxSourceGenerator.Tests;

public sealed class ResxGeneratorTest
{
    private sealed record GenerationResult(GeneratorDriverRunResult Result, byte[] Assembly)
    {
        public IEnumerable<SyntaxTree> GeneratedTrees => Result.GeneratedTrees;
        public SyntaxTree SyntaxTree => Result.GeneratedTrees.Single();
        public string GeneratedFilePath => SyntaxTree.FilePath;
        public string GeneratedFileName => Path.GetFileName(SyntaxTree.FilePath);
        public SyntaxNode GeneratedFileRoot => SyntaxTree.GetRoot();
    }

    private static async Task<GenerationResult> GenerateFiles((string ResxPath, string ResxContent)[] files, OptionProvider optionProvider, bool mustCompile = true)
    {
        var netcoreRef = await NuGetHelpers.GetNuGetReferences("Microsoft.NETCore.App.Ref", "8.0.0", "ref/net8.0/");
        var desktopRef = await NuGetHelpers.GetNuGetReferences("Microsoft.WindowsDesktop.App.Ref", "8.0.0", "ref/net8.0/");
        var references = netcoreRef.Concat(desktopRef)
            .Select(loc => MetadataReference.CreateFromFile(loc))
            .ToArray();

        var compilation = CSharpCompilation.Create("compilation",
            [CSharpSyntaxTree.ParseText("")],
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var generator = new ResxGenerator().AsSourceGenerator();
        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            generators: [generator],
            additionalTexts: files.Select(file => (AdditionalText)new TestAdditionalText(file.ResxPath, file.ResxContent)).ToArray(),
            optionsProvider: optionProvider);

        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out var outputCompilation, out var diagnostics);
        var runResult = driver.GetRunResult();

        using var ms = new MemoryStream();
        var result = outputCompilation.Emit(ms);
        if (mustCompile)
        {
            var diags = string.Join('\n', result.Diagnostics);
            var generated = (await runResult.GeneratedTrees[0].GetRootAsync()).ToFullString();
            Assert.True(result.Success);
            Assert.Empty(result.Diagnostics);
        }

        return new(runResult, ms.ToArray());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("internal")]
    [InlineData("dummy")]
    public async Task GenerateInternalClasses(string visibility)
    {
        var element = new XElement("root", new XElement("data", new XAttribute("name", "Sample"), new XElement("value", "Value")));
        var result = await GenerateFiles([("test.resx", element.ToString())], new OptionProvider
        {
            Visibility = visibility,
        });
        Assert.True(result.GeneratedFileRoot.AreTypesInternal());
    }

    [Theory]
    [InlineData("public")]
    [InlineData("Public")]
    public async Task GeneratePublicClasses(string visibility)
    {
        var element = new XElement("root", new XElement("data", new XAttribute("name", "Sample"), new XElement("value", "Value")));
        var result = await GenerateFiles([("test.resx", element.ToString())], new OptionProvider
        {
            Visibility = visibility,
        });
        Assert.True(result.GeneratedFileRoot.AreTypesPublic());
    }

    [Fact]
    public async Task GenerateProperties()
    {
        var element = new XElement("root",
            new XElement("data", new XAttribute("name", "Sample"), new XElement("value", "Value")),
            new XElement("data", new XAttribute("name", "HelloWorld"), new XElement("value", "Hello {0}!")),
            new XElement("data", new XAttribute("name", "Image1"), new XAttribute("type", "System.Resources.ResXFileRef, System.Windows.Forms"), new XElement("value", @"Resources\Image1.png;System.Drawing.Bitmap, System.Drawing, Version=4.0.0.0, Culture=neutral, PublicKeyToken=b03f5f7f11d50a3a"))
            );

        var result = await GenerateFiles([("test.resx", element.ToString())], new OptionProvider
        {
            Namespace = "test",
            ResourceName = "test",
        });
        Assert.Equal("test.resx.g.cs", Path.GetFileName(result.GeneratedFilePath));
        var fileContent = result.GeneratedFileRoot.ToFullString();
        Assert.Contains("Sample", fileContent);
        fileContent.Should().NotContain("FormatSample");
        Assert.Contains("HelloWorld\n", fileContent);
        Assert.Contains("FormatHelloWorld(object? arg0)", fileContent);
        Assert.Contains("public static global::System.Drawing.Bitmap? @Image1", fileContent);
    }

    [Fact]
    public async Task GeneratePropertiesFromMultipleResx()
    {
        var element1 = new XElement("root",
            new XElement("data", new XAttribute("name", "Sample"), new XElement("value", "Value")),
            new XElement("data", new XAttribute("name", "HelloWorld"), new XElement("value", "Hello {0}!"))
            );

        var element2 = new XElement("root",
            new XElement("data", new XAttribute("name", "Sample"), new XElement("value", "Value")),
            new XElement("data", new XAttribute("name", "HelloWorld2"), new XElement("value", "Hello {0}!"))
            );

        var element3 = new XElement("root",
            new XElement("data", new XAttribute("name", "AAA"), new XElement("value", "Value"))
            );

        var element4 = new XElement("root",
            new XElement("data", new XAttribute("name", "BBB"), new XElement("value", "Value"))
            );

        var result = await GenerateFiles(
            [
                (FullPath.GetTempPath() / "test.resx", element1.ToString()),
                (FullPath.GetTempPath() / "test.en.resx", element2.ToString()),
                (FullPath.GetTempPath() / "test.fr-FR.resx", element3.ToString()),
                (FullPath.GetTempPath() / "test.NewResource.fr.resx", element4.ToString()),
            ], new OptionProvider
            {
                ProjectDir = FullPath.GetTempPath(),
                RootNamespace = "Test",
            });

        result.GeneratedTrees.OrderBy(t => t.FilePath, StringComparer.Ordinal).Should().SatisfyRespectively(tree =>
            {
                var fileContent = tree.GetRoot(XunitCancellationToken).ToFullString();
                Assert.Equal("test.NewResource.resx.g.cs", Path.GetFileName(tree.FilePath));
                Assert.Contains("BBB", fileContent);
            }, tree =>
            {
                var fileContent = tree.GetRoot(XunitCancellationToken).ToFullString();
                Assert.Equal("test.resx.g.cs", Path.GetFileName(tree.FilePath));
                Assert.Contains("Sample", fileContent);
                Assert.Contains("HelloWorld", fileContent);
                Assert.Contains("AAA", fileContent);
            });
    }

    [Fact]
    public async Task ComputeNamespace_RootDir()
    {
        var result = await GenerateFiles([(FullPath.GetTempPath() / "dir" / "proj" / "test.resx", new XElement("root").ToString())], new OptionProvider
        {
            ProjectDir = FullPath.GetTempPath() / "dir" / "proj",
            RootNamespace = "proj",
        });
        Assert.Equal("proj", result.GeneratedFileRoot.GetNamespace());
    }

    [Fact]
    public async Task ComputeNamespace_SubFolder()
    {
        var result = await GenerateFiles([(FullPath.GetTempPath() / "dir" / "proj" / "A" / "test.resx", new XElement("root").ToString())], new OptionProvider
        {
            ProjectDir = FullPath.GetTempPath() / "dir" / "proj",
            RootNamespace = "proj",
        });
        Assert.Equal("proj.A", result.GeneratedFileRoot.GetNamespace());
    }

    [Fact]
    public async Task WrongResx_Warning()
    {
        var (result, _) = await GenerateFiles([("test.resx", "invalid xml")], new OptionProvider
        {
            ResourceName = "resource",
            Namespace = "test",
        }, mustCompile: false);

        result.Diagnostics.Should().SatisfyRespectively(diag => Assert.Equal("MFRG0001", diag.Id));
    }

    private sealed class OptionProvider : AnalyzerConfigOptionsProvider
    {
        public string ProjectDir { get; set; }
        public string RootNamespace { get; set; }
        public string Namespace { get; set; }
        public string ClassName { get; set; }
        public string DefaultResourcesNamespace { get; set; }
        public string ResourceName { get; set; }
        public string DefaultResourcesVisibility { get; set; }
        public string Visibility { get; set; }
        public string GenerateResourcesType { get; set; }
        public string GenerateKeyNamesType { get; set; }

        public override AnalyzerConfigOptions GlobalOptions => new Options(this);

        public override AnalyzerConfigOptions GetOptions(SyntaxTree tree) => new Options(this);

        public override AnalyzerConfigOptions GetOptions(AdditionalText textFile) => new Options(this);

        private sealed class Options : AnalyzerConfigOptions
        {
            private readonly OptionProvider _optionProvider;

            public Options(OptionProvider optionProvider) => _optionProvider = optionProvider;

            public override bool TryGetValue(string key, [NotNullWhen(true)] out string value)
            {
                const string BuildMetadata = "build_metadata.AdditionalFiles.";
                const string BuildProperties = "build_property.";
                if (key.StartsWith(BuildMetadata, StringComparison.Ordinal))
                {
                    key = key[BuildMetadata.Length..];
                }
                else if (key.StartsWith(BuildProperties, StringComparison.Ordinal))
                {
                    key = key[BuildProperties.Length..];
                }
                else
                {
                    value = null;
                    return false;
                }

                var prop = typeof(OptionProvider).GetProperty(key);
                if (prop != null)
                {
                    var propValue = (string?)prop.GetValue(_optionProvider, null);
                    if (propValue is not null)
                    {
                        value = propValue;
                        return true;
                    }
                }

                value = null;
                return false;
            }
        }
    }

    private sealed class TestAdditionalText : AdditionalText
    {
        private readonly SourceText _text;

        public TestAdditionalText(string path, SourceText text)
        {
            Path = path;
            _text = text;
        }
        public TestAdditionalText(string path, string text, Encoding encoding = null)
            : this(path, SourceText.From(text, encoding))
        {
        }

        public override string Path { get; }

        public override SourceText GetText(CancellationToken cancellationToken = default) => _text;
    }
}

file static class Extensions
{
    public static string GetNamespace(this SyntaxNode node)
    {
        return node.DescendantNodesAndSelf()
            .OfType<NamespaceDeclarationSyntax>()
            .Single()
            .Name.WithoutTrivia().ToFullString();
    }

    public static bool AreTypesPublic(this SyntaxNode node)
    {
        return node.DescendantNodesAndSelf()
            .OfType<TypeDeclarationSyntax>()
            .All(type => type.Modifiers.Any(modifier => modifier.IsKind(SyntaxKind.PublicKeyword)));
    }

    public static bool AreTypesInternal(this SyntaxNode node)
    {
        return node.DescendantNodesAndSelf()
            .OfType<TypeDeclarationSyntax>()
            .All(type => type.Modifiers.Any(modifier => modifier.IsKind(SyntaxKind.InternalKeyword)));
    }
}