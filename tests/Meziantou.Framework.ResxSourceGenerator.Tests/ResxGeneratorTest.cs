using System.Collections.Immutable;
using System.Xml.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Text;
using TestUtilities;

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

    private static async Task<Compilation> CreateCompilation()
    {
        var netcoreRef = await NuGetHelpers.GetNuGetReferences("Microsoft.NETCore.App.Ref", "8.0.0", "ref/net8.0/");
        var desktopRef = await NuGetHelpers.GetNuGetReferences("Microsoft.WindowsDesktop.App.Ref", "8.0.0", "ref/net8.0/");
        var references = netcoreRef.Concat(desktopRef)
            .Select(loc => MetadataReference.CreateFromFile(loc))
            .ToArray();

        return CSharpCompilation.Create("compilation",
            [CSharpSyntaxTree.ParseText("")],
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
    }

    private static async Task<GenerationResult> GenerateFiles((string ResxPath, string ResxContent)[] files, OptionProvider optionProvider, bool mustCompile = true)
    {
        var compilation = await CreateCompilation();
        var additionalTexts = files.Select(file => (AdditionalText)new TestAdditionalText(file.ResxPath, file.ResxContent)).ToArray();

        var generator = new ResxGenerator().AsSourceGenerator();
        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            generators: [generator],
            additionalTexts: additionalTexts,
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

    private static async Task<ImmutableArray<Diagnostic>> AnalyzeFiles((string ResxPath, string ResxContent)[] files, OptionProvider optionProvider)
    {
        var compilation = await CreateCompilation();
        var additionalTexts = files.Select(file => (AdditionalText)new TestAdditionalText(file.ResxPath, file.ResxContent)).ToImmutableArray();
        var analyzerOptions = new AnalyzerOptions(additionalTexts, optionProvider);
        var analyzers = ImmutableArray.Create<DiagnosticAnalyzer>(new ResxGeneratorAnalyzer());
        var compilationWithAnalyzers = compilation.WithAnalyzers(analyzers, new CompilationWithAnalyzersOptions(analyzerOptions, onAnalyzerException: null, concurrentAnalysis: true, logAnalyzerExecutionTime: false, reportSuppressedDiagnostics: false));
        return await compilationWithAnalyzers.GetAnalyzerDiagnosticsAsync();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("internal")]
    [InlineData("dummy")]
    public async Task GenerateInternalClasses(string? visibility)
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
        Assert.DoesNotContain("FormatSample", fileContent, ignoreCase: true);
        Assert.Contains("HelloWorld", fileContent);
        Assert.Contains("FormatHelloWorld(object? arg0)", fileContent);
        Assert.Contains("public static global::System.Drawing.Bitmap? @Image1", fileContent);
    }

    [Theory]
    [InlineData("He\"llo")]
    [InlineData(@"a\tb")]
    [InlineData(@"Path\To")]
    [InlineData("Line1\nLine2")]
    public async Task ResourceNamesWithSpecialCharactersAreEscaped(string name)
    {
        var element = new XElement("root",
            new XElement("data", new XAttribute("name", name), new XElement("value", "Value {0}")));

        var result = await GenerateFiles([("test.resx", element.ToString())], new OptionProvider
        {
            Namespace = "test",
            ResourceName = "test",
        });

        // The literal must round-trip to the exact key of the resx entry, otherwise the lookup silently returns null
        var fileContent = result.GeneratedFileRoot.ToFullString();
        var literal = SymbolDisplay.FormatLiteral(name, quote: true);
        Assert.Contains("GetString(" + literal + ")", fileContent);
        Assert.Contains(" = " + literal + ";", fileContent);
    }

    [Fact]
    public async Task ResourceFileNameWithSpecialCharactersIsEscaped()
    {
        var element = new XElement("root", new XElement("data", new XAttribute("name", "Sample"), new XElement("value", "Value")));
        var result = await GenerateFiles([("test.resx", element.ToString())], new OptionProvider
        {
            Namespace = "test",
            ResourceName = @"My\Resource""Name",
        });

        var fileContent = result.GeneratedFileRoot.ToFullString();
        Assert.Contains("new global::System.Resources.ResourceManager(" + SymbolDisplay.FormatLiteral(@"My\Resource""Name", quote: true), fileContent);
    }

    [Fact]
    public async Task InlineTypedValueUsesTheTypeAttribute()
    {
        // A typed value that is not a file reference carries its type in the type attribute, not in the value
        var element = new XElement("root",
            new XElement("data",
                new XAttribute("name", "MyColor"),
                new XAttribute("type", "System.Drawing.Color, System.Drawing"),
                new XElement("value", "Red")));

        var result = await GenerateFiles([("test.resx", element.ToString())], new OptionProvider
        {
            Namespace = "test",
            ResourceName = "test",
        });

        var fileContent = result.GeneratedFileRoot.ToFullString();
        Assert.Contains("public static global::System.Drawing.Color? @MyColor", fileContent);
        Assert.DoesNotContain("global::?", fileContent);
    }

    [Fact]
    public async Task ResourceWithUnknownTypeIsSkippedInsteadOfBreakingTheCompilation()
    {
        var element = new XElement("root",
            new XElement("data", new XAttribute("name", "Sample"), new XElement("value", "Value")),
            new XElement("data", new XAttribute("name", "Mystery"), new XAttribute("type", ""), new XElement("value", "?")));

        var result = await GenerateFiles([("test.resx", element.ToString())], new OptionProvider
        {
            Namespace = "test",
            ResourceName = "test",
        });

        var fileContent = result.GeneratedFileRoot.ToFullString();
        Assert.DoesNotContain("global::?", fileContent);
        Assert.Contains("@Sample", fileContent);
    }

    [Fact]
    public async Task GeneratedCodeQualifiesEveryFrameworkTypeReference()
    {
        // A consumer can declare a type or namespace named System, so nothing in the generated code may rely on it
        var element = new XElement("root", new XElement("data", new XAttribute("name", "Sample"), new XElement("value", "Value")));
        var result = await GenerateFiles([("test.resx", element.ToString())], new OptionProvider
        {
            Namespace = "test",
            ResourceName = "test",
        });

        var fileContent = result.GeneratedFileRoot.ToFullString();
        for (var index = fileContent.IndexOf("System.", StringComparison.Ordinal); index >= 0; index = fileContent.IndexOf("System.", index + 1, StringComparison.Ordinal))
        {
            var qualified = index >= 8 && fileContent.AsSpan(index - 8, 8).SequenceEqual("global::".AsSpan());
            Assert.True(qualified, "Unqualified reference: " + fileContent[Math.Max(0, index - 40)..Math.Min(fileContent.Length, index + 40)]);
        }
    }

    [Fact]
    public async Task GenerateProperties_WithFormatParameterMetadata()
    {
        XNamespace generatorNamespace = "https://meziantou.net/meziantou.framework/resxgenerator";
        var element = new XElement("root",
            new XAttribute(XNamespace.Xmlns + "mfrg", generatorNamespace),
            new XElement("data",
                new XAttribute("name", "HelloWorld"),
                new XElement("value", "Hello {0} from {1}!"),
                new XElement(generatorNamespace + "parameter", new XAttribute("name", "name"), new XAttribute("comment", "Name to greet.")),
                new XElement(generatorNamespace + "parameter", new XAttribute("name", "country"), new XAttribute("typename", "global::System.String"), new XAttribute("comment", "Country name."))));

        var result = await GenerateFiles([("test.resx", element.ToString())], new OptionProvider
        {
            Namespace = "test",
            ResourceName = "test",
        });

        var fileContent = result.GeneratedFileRoot.ToFullString();
        Assert.Contains("FormatHelloWorld(global::System.Globalization.CultureInfo? provider, object? name, global::System.String country)", fileContent);
        Assert.Contains("FormatHelloWorld(object? name, global::System.String country)", fileContent);
        Assert.Contains("<param name=\"name\">Name to greet.</param>", fileContent);
        Assert.Contains("<param name=\"country\">Country name.</param>", fileContent);
        Assert.DoesNotContain("object? arg0", fileContent);
    }

    [Fact]
    public async Task GenerateProperties_WithMissingAndExtraFormatParameterMetadata()
    {
        XNamespace generatorNamespace = "https://meziantou.net/meziantou.framework/resxgenerator";
        var element = new XElement("root",
            new XAttribute(XNamespace.Xmlns + "mfrg", generatorNamespace),
            new XElement("data",
                new XAttribute("name", "HelloWorld"),
                new XElement("value", "Hello {0} from {1}!"),
                new XElement(generatorNamespace + "parameter", new XAttribute("name", "name"), new XAttribute("comment", "Name to greet.")),
                new XElement(generatorNamespace + "parameter", new XAttribute("name", ""), new XAttribute("comment", "Fallback parameter.")),
                new XElement(generatorNamespace + "parameter", new XAttribute("name", "unused"), new XAttribute("comment", "Ignored parameter."))));

        var result = await GenerateFiles([("test.resx", element.ToString())], new OptionProvider
        {
            Namespace = "test",
            ResourceName = "test",
        });

        var fileContent = result.GeneratedFileRoot.ToFullString();
        Assert.Contains("FormatHelloWorld(object? name, object? arg1)", fileContent);
        Assert.Contains("<param name=\"name\">Name to greet.</param>", fileContent);
        Assert.Contains("<param name=\"arg1\">Fallback parameter.</param>", fileContent);
        Assert.DoesNotContain("unused", fileContent);
        Assert.DoesNotContain("Ignored parameter.", fileContent);
    }

    [Fact]
    public async Task GenerateProperties_WithFallbackFormatParameterNameCollision()
    {
        XNamespace generatorNamespace = "https://meziantou.net/meziantou.framework/resxgenerator";
        var element = new XElement("root",
            new XAttribute(XNamespace.Xmlns + "mfrg", generatorNamespace),
            new XElement("data",
                new XAttribute("name", "HelloWorld"),
                new XElement("value", "Hello {0} from {1}!"),
                new XElement(generatorNamespace + "parameter", new XAttribute("name", "arg1")),
                new XElement(generatorNamespace + "parameter", new XAttribute("name", ""))));

        var result = await GenerateFiles([("test.resx", element.ToString())], new OptionProvider
        {
            Namespace = "test",
            ResourceName = "test",
        });

        var fileContent = result.GeneratedFileRoot.ToFullString();
        Assert.Contains("FormatHelloWorld(object? arg1, object? arg1_)", fileContent);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task GenerateProperties_UsesFormatParameterMetadataFromAnyDuplicateEntry(bool parameterMetadataInFirstFile)
    {
        XNamespace generatorNamespace = "https://meziantou.net/meziantou.framework/resxgenerator";
        var elementWithoutParameterMetadata = new XElement("root",
            new XElement("data", new XAttribute("name", "HelloWorld"), new XElement("value", "Hello {0}!")));
        var elementWithParameterMetadata = new XElement("root",
            new XAttribute(XNamespace.Xmlns + "mfrg", generatorNamespace),
            new XElement("data",
                new XAttribute("name", "HelloWorld"),
                new XElement("value", "Hello {0}!"),
                new XElement(generatorNamespace + "parameter", new XAttribute("name", "name"), new XAttribute("typename", "global::System.String"), new XAttribute("comment", "Name to greet."))));

        var firstElement = parameterMetadataInFirstFile ? elementWithParameterMetadata : elementWithoutParameterMetadata;
        var secondElement = parameterMetadataInFirstFile ? elementWithoutParameterMetadata : elementWithParameterMetadata;

        var result = await GenerateFiles(
            [
                ("test.en.resx", firstElement.ToString()),
                ("test.resx", secondElement.ToString()),
            ], new OptionProvider
            {
                Namespace = "test",
                ResourceName = "test",
            });

        var fileContent = result.GeneratedFileRoot.ToFullString();
        Assert.Contains("FormatHelloWorld(global::System.String name)", fileContent);
        Assert.Contains("<param name=\"name\">Name to greet.</param>", fileContent);
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

        Assert.Collection(result.GeneratedTrees.OrderBy(t => t.FilePath, StringComparer.Ordinal),
            tree =>
            {
                var fileContent = tree.GetRoot(XunitCancellationToken).ToFullString();
                Assert.Equal("test.NewResource.resx.g.cs", Path.GetFileName(tree.FilePath));
                Assert.Contains("BBB", fileContent);
            },
            tree =>
            {
                var fileContent = tree.GetRoot(XunitCancellationToken).ToFullString();
                Assert.Equal("test.resx.g.cs", Path.GetFileName(tree.FilePath));
                Assert.Contains("Sample", fileContent);
                Assert.Contains("HelloWorld", fileContent);
                Assert.Contains("AAA", fileContent);
            });
    }

    [Fact]
    public async Task ResxFilesWithSameFileName()
    {
        var element1 = new XElement("root", new XElement("data", new XAttribute("name", "Sample"), new XElement("value", "from Folder1")));
        var element2 = new XElement("root", new XElement("data", new XAttribute("name", "Sample"), new XElement("value", "from Folder2")));

        var result = await GenerateFiles(
            [
                (FullPath.GetTempPath() / "proj" / "Folder1" / "Messages.resx", element1.ToString()),
                (FullPath.GetTempPath() / "proj" / "Folder2" / "Messages.resx", element2.ToString()),
            ], new OptionProvider
            {
                ProjectDir = FullPath.GetTempPath() / "proj",
                RootNamespace = "Test",
            });

        Assert.Collection(result.GeneratedTrees.OrderBy(t => t.FilePath, StringComparer.Ordinal),
            tree =>
            {
                Assert.Equal("Folder1.Messages.resx.g.cs", Path.GetFileName(tree.FilePath));
                Assert.Equal("Test.Folder1", tree.GetRoot(XunitCancellationToken).GetNamespace());
            },
            tree =>
            {
                Assert.Equal("Folder2.Messages.resx.g.cs", Path.GetFileName(tree.FilePath));
                Assert.Equal("Test.Folder2", tree.GetRoot(XunitCancellationToken).GetNamespace());
            });
    }

    [Theory]
    [InlineData("fr")]
    [InlineData("fil")]
    [InlineData("fr-FR")]
    [InlineData("zh-Hans")]
    [InlineData("sr-Latn-RS")]
    [InlineData("es-419")]
    public async Task SatelliteResxFilesAreGroupedWithTheNeutralFile(string culture)
    {
        var element = new XElement("root", new XElement("data", new XAttribute("name", "Sample"), new XElement("value", "Value")));

        var result = await GenerateFiles(
            [
                (FullPath.GetTempPath() / "proj" / "Messages.resx", element.ToString()),
                (FullPath.GetTempPath() / "proj" / $"Messages.{culture}.resx", element.ToString()),
            ], new OptionProvider
            {
                ProjectDir = FullPath.GetTempPath() / "proj",
                RootNamespace = "Test",
            });

        Assert.Equal("Messages.resx.g.cs", result.GeneratedFileName);
    }

    [Theory]
    [InlineData("Backup")]
    [InlineData("v2")]
    [InlineData("Design")]
    public async Task NonCultureSuffixesAreTheirOwnResource(string suffix)
    {
        var element = new XElement("root", new XElement("data", new XAttribute("name", "Sample"), new XElement("value", "Value")));

        var result = await GenerateFiles(
            [
                (FullPath.GetTempPath() / "proj" / "Messages.resx", element.ToString()),
                (FullPath.GetTempPath() / "proj" / $"Messages.{suffix}.resx", element.ToString()),
            ], new OptionProvider
            {
                ProjectDir = FullPath.GetTempPath() / "proj",
                RootNamespace = "Test",
            });

        var fileNames = result.GeneratedTrees.Select(tree => Path.GetFileName(tree.FilePath)).Order(StringComparer.Ordinal);
        Assert.Equal(new[] { $"Messages.{suffix}.resx.g.cs", "Messages.resx.g.cs" }.Order(StringComparer.Ordinal), fileNames);
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
        var files = new[] { ("test.resx", "invalid xml") };
        var options = new OptionProvider
        {
            ResourceName = "resource",
            Namespace = "test",
        };

        var (result, _) = await GenerateFiles(files, options, mustCompile: false);
        Assert.Empty(result.Diagnostics);
        Assert.Empty(result.GeneratedTrees);

        var diagnostics = await AnalyzeFiles(files, options);
        Assert.Collection(diagnostics, diag => Assert.Equal("MFRG0001", diag.Id));
    }

    [Fact]
    public async Task InconsistentMetadata_Warning()
    {
        var element = new XElement("root", new XElement("data", new XAttribute("name", "Sample"), new XElement("value", "Value")));
        var files = new[]
        {
            (ResxPath: "test.resx", ResxContent: element.ToString()),
            (ResxPath: "test.fr.resx", ResxContent: element.ToString()),
        };
        var options = new OptionProvider
        {
            PerFileMetadata = new Dictionary<string, Dictionary<string, string>>(StringComparer.Ordinal)
            {
                ["test.resx"] = new(StringComparer.Ordinal) { ["Namespace"] = "A" },
                ["test.fr.resx"] = new(StringComparer.Ordinal) { ["Namespace"] = "B" },
            },
        };

        var (result, _) = await GenerateFiles(files, options, mustCompile: false);
        Assert.Empty(result.Diagnostics);
        Assert.Empty(result.GeneratedTrees);

        var diagnostics = await AnalyzeFiles(files, options);
        Assert.Collection(diagnostics, diag => Assert.Equal("MFRG0004", diag.Id));
    }

    [Fact]
    public async Task EmptyMetadataIsNotInconsistent()
    {
        // MSBuild reports an unset metadata as an empty value, so only the neutral resx file carries the resource name
        var element = new XElement("root", new XElement("data", new XAttribute("name", "Sample"), new XElement("value", "Value")));
        var files = new[]
        {
            (ResxPath: "test.fr.resx", ResxContent: element.ToString()),
            (ResxPath: "test.resx", ResxContent: element.ToString()),
        };
        var options = new OptionProvider
        {
            Namespace = "test",
            PerFileMetadata = new Dictionary<string, Dictionary<string, string>>(StringComparer.Ordinal)
            {
                ["test.resx"] = new(StringComparer.Ordinal) { ["DefaultResourceName"] = "Sample.Test" },
                ["test.fr.resx"] = new(StringComparer.Ordinal) { ["DefaultResourceName"] = "" },
            },
        };

        var result = await GenerateFiles(files, options);
        Assert.Contains("Sample.Test", result.GeneratedFileRoot.ToFullString());

        var diagnostics = await AnalyzeFiles(files, options);
        Assert.Empty(diagnostics);
    }

    private sealed class OptionProvider : AnalyzerConfigOptionsProvider
    {
        public string? ProjectDir { get; set; }
        public string? RootNamespace { get; set; }
        public string? Namespace { get; set; }
        public string? ClassName { get; set; }
        public string? DefaultResourcesNamespace { get; set; }
        public string? ResourceName { get; set; }
        public string? DefaultResourcesVisibility { get; set; }
        public string? Visibility { get; set; }
        public string? GenerateResourcesType { get; set; }
        public string? GenerateKeyNamesType { get; set; }
        public Dictionary<string, Dictionary<string, string>> PerFileMetadata { get; set; } = new(StringComparer.Ordinal);

        public override AnalyzerConfigOptions GlobalOptions => new Options(this);

        public override AnalyzerConfigOptions GetOptions(SyntaxTree tree) => new Options(this);

        public override AnalyzerConfigOptions GetOptions(AdditionalText textFile) => new Options(this, textFile.Path);

        private sealed class Options : AnalyzerConfigOptions
        {
            private readonly OptionProvider _optionProvider;
            private readonly string? _path;

            public Options(OptionProvider optionProvider, string? path = null)
            {
                _optionProvider = optionProvider;
                _path = path;
            }

            public override bool TryGetValue(string key, [NotNullWhen(true)] out string? value)
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

                if (_path is not null && _optionProvider.PerFileMetadata.TryGetValue(_path, out var metadata) && metadata.TryGetValue(key, out value))
                {
                    return true;
                }

                var prop = typeof(OptionProvider).GetProperty(key);
                if (prop != null)
                {
                    var propValue = prop.GetValue(_optionProvider, null) as string;
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
        public TestAdditionalText(string path, string text, Encoding? encoding = null)
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
