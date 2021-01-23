using System;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Xml.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Text;
using Xunit;

namespace Meziantou.Framework.ResxSourceGenerator.Tests
{
    public class ResxGeneratorTest
    {
        private static GeneratorDriverRunResult GenerateFiles((string ResxPath, string ResxContent)[] files, OptionProvider optionProvider)
        {
            var compilation = CSharpCompilation.Create("compilation",
                new[] { CSharpSyntaxTree.ParseText("") },
                new[] { MetadataReference.CreateFromFile(typeof(Binder).GetTypeInfo().Assembly.Location) },
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

            var generator = new ResxGenerator();
            GeneratorDriver driver = CSharpGeneratorDriver.Create(
                generators: new ISourceGenerator[] { generator },
                additionalTexts: files.Select(file => (AdditionalText)new TestAdditionalText(file.ResxPath, file.ResxContent)).ToArray(),
                optionsProvider: optionProvider);

            driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out var outputCompilation, out var diagnostics);
            return driver.GetRunResult();
        }

        [Fact]
        public void GenerateProperties()
        {
            var element = new XElement("root",
                new XElement("data", new XAttribute("name", "Sample"), new XElement("value", "Value")),
                new XElement("data", new XAttribute("name", "HelloWorld"), new XElement("value", "Hello {0}!"))
                );

            var result = GenerateFiles(new[] { ("test.resx", element.ToString()) }, new OptionProvider
            {
                Namespace = "test",
                ResourceName = "test",
            });

            Assert.Empty(result.Diagnostics);
            Assert.Single(result.GeneratedTrees);
            Assert.Equal("test.resx.cs", Path.GetFileName(result.GeneratedTrees[0].FilePath));
            var fileContent = result.GeneratedTrees[0].GetRoot().ToFullString();
            Assert.Contains("Sample", fileContent, StringComparison.Ordinal);
            Assert.DoesNotContain("FormatSample", fileContent, StringComparison.Ordinal);

            Assert.Contains("HelloWorld\n", fileContent, StringComparison.Ordinal);
            Assert.Contains("FormatHelloWorld(object arg0)", fileContent, StringComparison.Ordinal);
        }

        [Fact]
        public void GeneratePropertiesFromMultipleResx()
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

            var result = GenerateFiles(new (string, string)[]
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

            Assert.Collection(result.GeneratedTrees.OrderBy(t => t.FilePath),
                tree =>
                {
                    var fileContent = tree.GetRoot().ToFullString();
                    Assert.Equal("test.NewResource.resx.cs", Path.GetFileName(tree.FilePath));
                    Assert.Contains("BBB", fileContent, StringComparison.Ordinal);
                },
                tree =>
                {
                    var fileContent = tree.GetRoot().ToFullString();
                    Assert.Equal("test.resx.cs", Path.GetFileName(tree.FilePath));
                    Assert.Contains("Sample", fileContent, StringComparison.Ordinal);
                    Assert.Contains("HelloWorld", fileContent, StringComparison.Ordinal);
                    Assert.Contains("AAA", fileContent, StringComparison.Ordinal);
                });
        }

        [Fact]
        public void ComputeNamespace_RootDir()
        {
            var result = GenerateFiles(new (string, string)[] { (FullPath.GetTempPath() / "dir" / "proj" / "test.resx", new XElement("root").ToString()) }, new OptionProvider
            {
                ProjectDir = FullPath.GetTempPath() / "dir" / "proj",
                RootNamespace = "proj",
            });

            Assert.Empty(result.Diagnostics);
            var fileContent = result.GeneratedTrees[0].GetRoot().ToFullString();
            Assert.Contains("namespace proj" + Environment.NewLine, fileContent, StringComparison.Ordinal);
        }

        [Fact]
        public void ComputeNamespace_SubFolder()
        {
            var result = GenerateFiles(new (string, string)[] { (FullPath.GetTempPath() / "dir" / "proj" / "A" / "test.resx", new XElement("root").ToString()) }, new OptionProvider
            {
                ProjectDir = FullPath.GetTempPath() / "dir" / "proj",
                RootNamespace = "proj",
            });

            var fileContent = result.GeneratedTrees[0].GetRoot().ToFullString();
            Assert.Contains("namespace proj.A" + Environment.NewLine, fileContent, StringComparison.Ordinal);
        }

        [Fact]
        public void WrongResx_Warning()
        {
            var result = GenerateFiles(new[] { ("test.resx", "invalid xml") }, new OptionProvider
            {
                ResourceName = "resource",
                Namespace = "test",
            });

            Assert.Collection(result.Diagnostics, diag => Assert.Equal("MFRG0001", diag.Id));
            Assert.Empty(result.GeneratedTrees);
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
}
