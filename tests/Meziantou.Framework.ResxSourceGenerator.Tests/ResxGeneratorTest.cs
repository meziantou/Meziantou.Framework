using System.Text;
using System.Xml.Linq;
using FluentAssertions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Text;
using TestUtilities;
using Xunit;

namespace Meziantou.Framework.ResxSourceGenerator.Tests;

public class ResxGeneratorTest
{
    private static async Task<(GeneratorDriverRunResult Result, byte[] Assembly)> GenerateFiles((string ResxPath, string ResxContent)[] files, OptionProvider optionProvider, bool mustCompile = true)
    {
        var netcoreRef = await NuGetHelpers.GetNuGetReferences("Microsoft.NETCore.App.Ref", "6.0.0", "ref/net6.0/");
        var desktopRef = await NuGetHelpers.GetNuGetReferences("Microsoft.WindowsDesktop.App.Ref", "6.0.0", "ref/net6.0/");
        var references = netcoreRef.Concat(desktopRef)
            .Select(loc => MetadataReference.CreateFromFile(loc))
            .ToArray();

        var compilation = CSharpCompilation.Create("compilation",
            new[] { CSharpSyntaxTree.ParseText("") },
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var generator = new ResxGenerator().AsSourceGenerator();
        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            generators: new ISourceGenerator[] { generator },
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
            result.Success.Should().BeTrue("Project cannot build:\n" + diags + "\n\n\n" + generated);
            result.Diagnostics.Should().BeEmpty();
        }

        return (runResult, ms.ToArray());
    }

    [Fact]
    public async Task GenerateProperties()
    {
        var element = new XElement("root",
            new XElement("data", new XAttribute("name", "Sample"), new XElement("value", "Value")),
            new XElement("data", new XAttribute("name", "HelloWorld"), new XElement("value", "Hello {0}!")),
            new XElement("data", new XAttribute("name", "Image1"), new XAttribute("type", "System.Resources.ResXFileRef, System.Windows.Forms"), new XElement("value", @"Resources\Image1.png;System.Drawing.Bitmap, System.Drawing, Version=4.0.0.0, Culture=neutral, PublicKeyToken=b03f5f7f11d50a3a"))
            );

        var (result, _) = await GenerateFiles(new[] { ("test.resx", element.ToString()) }, new OptionProvider
        {
            Namespace = "test",
            ResourceName = "test",
        });

        result.Diagnostics.Should().BeEmpty();
        result.GeneratedTrees.Should().ContainSingle();
        Path.GetFileName(result.GeneratedTrees[0].FilePath).Should().Be("test.resx.g.cs");
        var fileContent = (await result.GeneratedTrees[0].GetRootAsync()).ToFullString();
        fileContent.Should().Contain("Sample");
        fileContent.Should().NotContain("FormatSample");

        fileContent.Should().Contain("HelloWorld\n");
        fileContent.Should().Contain("FormatHelloWorld(object? arg0)");
        fileContent.Should().Contain("public static global::System.Drawing.Bitmap? @Image1");
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

        var (result, assembly) = await GenerateFiles(new (string, string)[]
            {
                (FullPath.GetTempPath() / "test.resx", element1.ToString()),
                (FullPath.GetTempPath() / "test.en.resx", element2.ToString()),
                (FullPath.GetTempPath() / "test.fr-FR.resx", element3.ToString()),
                (FullPath.GetTempPath() / "test.NewResource.fr.resx", element4.ToString()),
            }, new OptionProvider
            {
                ProjectDir = FullPath.GetTempPath(),
                RootNamespace = "Test",
            });

        result.GeneratedTrees.OrderBy(t => t.FilePath, StringComparer.Ordinal).Should().SatisfyRespectively(tree =>
            {
                var fileContent = tree.GetRoot().ToFullString();
                Path.GetFileName(tree.FilePath).Should().Be("test.NewResource.resx.g.cs");
                fileContent.Should().Contain("BBB");
            }, tree =>
            {
                var fileContent = tree.GetRoot().ToFullString();
                Path.GetFileName(tree.FilePath).Should().Be("test.resx.g.cs");
                fileContent.Should().Contain("Sample");
                fileContent.Should().Contain("HelloWorld");
                fileContent.Should().Contain("AAA");
            });
    }

    [Fact]
    public async Task ComputeNamespace_RootDir()
    {
        var (result, _) = await GenerateFiles(new (string, string)[] { (FullPath.GetTempPath() / "dir" / "proj" / "test.resx", new XElement("root").ToString()) }, new OptionProvider
        {
            ProjectDir = FullPath.GetTempPath() / "dir" / "proj",
            RootNamespace = "proj",
        });

        result.Diagnostics.Should().BeEmpty();
        var fileContent = (await result.GeneratedTrees[0].GetRootAsync()).ToFullString();
        fileContent.Should().Contain("namespace proj" + Environment.NewLine);
    }

    [Fact]
    public async Task ComputeNamespace_SubFolder()
    {
        var (result, _) = await GenerateFiles(new (string, string)[] { (FullPath.GetTempPath() / "dir" / "proj" / "A" / "test.resx", new XElement("root").ToString()) }, new OptionProvider
        {
            ProjectDir = FullPath.GetTempPath() / "dir" / "proj",
            RootNamespace = "proj",
        });

        var fileContent = (await result.GeneratedTrees[0].GetRootAsync()).ToFullString();
        fileContent.Should().Contain("namespace proj.A" + Environment.NewLine);
    }

    [Fact]
    public async Task WrongResx_Warning()
    {
        var (result, _) = await GenerateFiles(new[] { ("test.resx", "invalid xml") }, new OptionProvider
        {
            ResourceName = "resource",
            Namespace = "test",
        }, mustCompile: false);

        result.Diagnostics.Should().SatisfyRespectively(diag => diag.Id.Should().Be("MFRG0001"));
    }

    private sealed class OptionProvider : AnalyzerConfigOptionsProvider
    {
        public string ProjectDir { get; set; }
        public string RootNamespace { get; set; }
        public string Namespace { get; set; }
        public string ClassName { get; set; }
        public string ResourceName { get; set; }

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

                switch (key)
                {
                    case "RootNamespace":
                        if (_optionProvider.RootNamespace != null)
                        {
                            value = _optionProvider.RootNamespace;
                            return true;
                        }
                        break;

                    case "ProjectDir":
                        if (_optionProvider.ProjectDir != null)
                        {
                            value = _optionProvider.ProjectDir;
                            return true;
                        }
                        break;

                    case "Namespace":
                        if (_optionProvider.Namespace != null)
                        {
                            value = _optionProvider.Namespace;
                            return true;
                        }
                        break;

                    case "ResourceName":
                        if (_optionProvider.ResourceName != null)
                        {
                            value = _optionProvider.ResourceName;
                            return true;
                        }
                        break;

                    case "ClassName":
                        if (_optionProvider.ClassName != null)
                        {
                            value = _optionProvider.ClassName;
                            return true;
                        }
                        break;

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
