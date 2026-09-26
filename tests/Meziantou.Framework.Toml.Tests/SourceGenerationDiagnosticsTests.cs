using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json.Serialization;
using Meziantou.Framework.Toml.Serialization;
using Meziantou.Framework.Toml.SourceGeneration;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Meziantou.Framework.Toml.Tests;

public sealed class SourceGenerationDiagnosticsTests
{
    [Fact]
    public void Generator_ResolvesKnownOptionBranchesAtBuildTime()
    {
        var source = """
            #nullable enable
            using System.Collections.Generic;
            using System.Text.Json.Serialization;
            using Meziantou.Framework.Toml;
            using Meziantou.Framework.Toml.Serialization;

            [TomlSourceGenerationOptions(
                PropertyNameCaseInsensitive = true,
                DefaultIgnoreCondition = TomlIgnoreCondition.Never,
                DuplicateKeyHandling = TomlDuplicateKeyHandling.LastWins,
                MappingOrder = TomlMappingOrderPolicy.Alphabetical,
                DictionaryKeyPolicy = TomlKnownNamingPolicy.CamelCase,
                PreferredObjectCreationHandling = TomlObjectCreationHandling.Populate)]
            [TomlSerializable(typeof(Person))]
            [TomlSerializable(typeof(CtorPerson))]
            internal partial class Ctx : TomlSerializerContext { }

            public sealed class Person
            {
                public string? Name { get; set; }
                public int Age { get; set; }
                public Child Child { get; } = new();
                [TomlExtensionData]
                public Dictionary<string, string>? Extra { get; set; }
            }

            public sealed class CtorPerson
            {
                [JsonConstructor]
                public CtorPerson(string? name, int age)
                {
                    Name = name;
                    Age = age;
                }

                public string? Name { get; }
                public int Age { get; }
            }

            public sealed class Child
            {
                public string? Value { get; set; }
            }
            """;

        var generatedSource = RunGeneratorTest(source).GeneratedSources.Single();

        AssertNoDynamicKnownOptionBranches(generatedSource);
    }

    [Fact]
    public void Generator_ResolvesDefaultOptionBranchesAtBuildTime()
    {
        var source = """
            #nullable enable
            using System.Collections.Generic;
            using System.Text.Json.Serialization;
            using Meziantou.Framework.Toml.Serialization;

            [TomlSerializable(typeof(Person))]
            [TomlSerializable(typeof(CtorPerson))]
            internal partial class Ctx : TomlSerializerContext { }

            public sealed class Person
            {
                public string? Name { get; set; }
                public int Age { get; set; }
                public Child Child { get; } = new();
                [TomlExtensionData]
                public Dictionary<string, string>? Extra { get; set; }
            }

            public sealed class CtorPerson
            {
                [JsonConstructor]
                public CtorPerson(string? name, int age)
                {
                    Name = name;
                    Age = age;
                }

                public string? Name { get; }
                public int Age { get; }
            }

            public sealed class Child
            {
                public string? Value { get; set; }
            }
            """;

        var generatedSource = RunGeneratorTest(source).GeneratedSources.Single();

        AssertNoDynamicKnownOptionBranches(generatedSource);
    }

    private static void AssertNoDynamicKnownOptionBranches(string generatedSource)
    {
        Assert.DoesNotContain("Options.PropertyNameCaseInsensitive", generatedSource);
        Assert.DoesNotContain("Options.DefaultIgnoreCondition", generatedSource);
        Assert.DoesNotContain("Options.DuplicateKeyHandling", generatedSource);
        Assert.DoesNotContain("Options.MappingOrder", generatedSource);
        Assert.DoesNotContain("Options.DictionaryKeyPolicy", generatedSource);
        Assert.DoesNotContain("Options.PreferredObjectCreationHandling", generatedSource);
        Assert.DoesNotContain("if (Options.", generatedSource);
        Assert.DoesNotContain("switch (Options.", generatedSource);
    }

    [Fact]
    public void Generator_ReportsInvalidIndentSize()
    {
        var source = """
            #nullable enable
            using Meziantou.Framework.Toml.Serialization;

            [TomlSourceGenerationOptions(IndentSize = 0)]
            [TomlSerializable(typeof(Person))]
            internal partial class Ctx : TomlSerializerContext { }

            public sealed class Person { public string Name { get; set; } = ""; }
            """;

        var diagnostics = RunGenerator(source);
        Assert.True(diagnostics.Any(d => d.Id == "MFTOML005"));
    }

    [Theory]
    [InlineData("Always")]
    [InlineData("WhenWriting")]
    [InlineData("WhenReading")]
    public void Generator_ReportsUnsupportedDefaultIgnoreCondition(string condition)
    {
        var source = """
            using Meziantou.Framework.Toml;
            using Meziantou.Framework.Toml.Serialization;

            [TomlSourceGenerationOptions(DefaultIgnoreCondition = TomlIgnoreCondition.CONDITION)]
            [TomlSerializable(typeof(Person))]
            internal partial class Ctx : TomlSerializerContext { }

            public sealed class Person { public string Name { get; set; } = ""; }
            """.Replace("CONDITION", condition, StringComparison.Ordinal);

        var diagnostics = RunGenerator(source);

        var diagnostic = Assert.Single(diagnostics, d => d.Id == "MFTOML005");
        Assert.Contains("DefaultIgnoreCondition", diagnostic.GetMessage(CultureInfo.InvariantCulture), StringComparison.Ordinal);
    }

    [Fact]
    public void Generator_ReportsInvalidEnumOption()
    {
        var source = """
            #nullable enable
            using Meziantou.Framework.Toml;
            using Meziantou.Framework.Toml.Serialization;

            [TomlSourceGenerationOptions(NewLine = (TomlNewLineKind)42)]
            [TomlSerializable(typeof(Person))]
            internal partial class Ctx : TomlSerializerContext { }

            public sealed class Person { public string Name { get; set; } = ""; }
            """;

        var diagnostics = RunGenerator(source);
        Assert.True(diagnostics.Any(d => d.Id == "MFTOML005"));
    }

    [Fact]
    public void Generator_ReportsInvalidMaxDepth()
    {
        var source = """
            #nullable enable
            using Meziantou.Framework.Toml.Serialization;

            [TomlSourceGenerationOptions(MaxDepth = -1)]
            [TomlSerializable(typeof(Person))]
            internal partial class Ctx : TomlSerializerContext { }

            public sealed class Person { public string Name { get; set; } = ""; }
            """;

        var diagnostics = RunGenerator(source);
        Assert.True(diagnostics.Any(d => d.Id == "MFTOML005"));
    }

    [Fact]
    public void Generator_ReportsInvalidConverterType()
    {
        var source = """
            #nullable enable
            using System;
            using Meziantou.Framework.Toml.Serialization;

            public sealed class NotAConverter { public NotAConverter() { } }

            [TomlSourceGenerationOptions(Converters = new [] { typeof(NotAConverter) })]
            [TomlSerializable(typeof(Person))]
            internal partial class Ctx : TomlSerializerContext { }

            public sealed class Person { public string Name { get; set; } = ""; }
            """;

        var diagnostics = RunGenerator(source);
        Assert.True(diagnostics.Any(d => d.Id == "MFTOML002"));
    }

    [Fact]
    public void Generator_WarnsForOptionsConverterFactory()
    {
        var source = """
            #nullable enable
            using System;
            using Meziantou.Framework.Toml;
            using Meziantou.Framework.Toml.Serialization;

            public sealed class Factory : TomlConverterFactory
            {
                public override bool CanConvert(Type typeToConvert) => false;
                public override TomlConverter CreateConverter(Type typeToConvert, TomlSerializerOptions options) => throw new NotSupportedException();
            }

            [TomlSourceGenerationOptions(Converters = new [] { typeof(Factory) })]
            [TomlSerializable(typeof(Person))]
            internal partial class Ctx : TomlSerializerContext { }

            public sealed class Person { public string Name { get; set; } = ""; }
            """;

        var diagnostics = RunGenerator(source);
        Assert.Single(diagnostics, d => d.Id == "MFTOML012" && d.Severity == DiagnosticSeverity.Warning);
    }

    [Theory]
    [InlineData("[TomlConverter(typeof(NotAConverter))]")]
    [InlineData("[System.Text.Json.Serialization.JsonConverter(typeof(NotAConverter))]")]
    [InlineData("[TomlConverter(typeof(Holder.PrivateConverter))]")]
    public void Generator_ReportsInvalidMemberConverter(string attribute)
    {
        var source = """
            #nullable enable
            using Meziantou.Framework.Toml.Serialization;

            public sealed class NotAConverter { }

            public sealed class Holder
            {
                private sealed class PrivateConverter : TomlConverter<string>
                {
                    public override string? Read(TomlReader reader) => reader.GetString();
                    public override void Write(TomlWriter writer, string value) => writer.WriteStringValue(value);
                }

                public static string Name => nameof(PrivateConverter);
            }

            [TomlSerializable(typeof(Person))]
            internal partial class Ctx : TomlSerializerContext { }

            public sealed class Person
            {
                ATTRIBUTE
                public string Name { get; set; } = "";

                public int Age { get; set; }
            }
            """.Replace("ATTRIBUTE", attribute, StringComparison.Ordinal);

        var diagnostics = RunGenerator(source);
        Assert.Contains(diagnostics, d => d.Id == "MFTOML002" && d.Severity == DiagnosticSeverity.Error);
    }

    [Fact]
    public void Generator_TypesWithTheSameName_Compile()
    {
        var source = """
            #nullable enable
            using System.Collections.Generic;
            using Meziantou.Framework.Toml.Serialization;

            namespace A
            {
                public sealed class Item { public int X { get; set; } }
            }

            namespace B
            {
                public sealed class Item { public string? Y { get; set; } }
            }

            namespace C
            {
                public sealed class Item { public int Z { get; set; } }
                public sealed class Holder { public Item? Value { get; set; } }
            }

            namespace D
            {
                public sealed class Item { public int W { get; set; } }
                public sealed class Holder2 { public Item? Value { get; set; } }
            }

            public sealed class Options { public int Value { get; set; } }
            public sealed class Default { public int Value { get; set; } }
            public sealed class Ctx { public int Value { get; set; } }

            [TomlSerializable(typeof(A.Item))]
            [TomlSerializable(typeof(B.Item))]
            [TomlSerializable(typeof(C.Holder))]
            [TomlSerializable(typeof(D.Holder2))]
            [TomlSerializable(typeof(List<int>[]))]
            [TomlSerializable(typeof(List<int[]>))]
            [TomlSerializable(typeof(Options))]
            [TomlSerializable(typeof(Default))]
            internal partial class Ctx { }

            [TomlSerializable(typeof(A.Item), TypeInfoPropertyName = "FirstItem")]
            [TomlSerializable(typeof(B.Item))]
            internal partial class RenamedCtx : TomlSerializerContext { }

            internal partial class Ctx : TomlSerializerContext { }
            """;

        var result = RunGeneratorTest(source.Replace("public sealed class Ctx { public int Value { get; set; } }", "", StringComparison.Ordinal));

        Assert.Empty(result.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));
        var generated = string.Join("\n", result.GeneratedSources);
        Assert.Contains("public global::Meziantou.Framework.Toml.TomlTypeInfo<global::A.Item> Item", generated, StringComparison.Ordinal);
        Assert.Contains("public global::Meziantou.Framework.Toml.TomlTypeInfo<global::B.Item> B_Item", generated, StringComparison.Ordinal);
        Assert.Contains("public global::Meziantou.Framework.Toml.TomlTypeInfo<global::A.Item> FirstItem", generated, StringComparison.Ordinal);
        Assert.Contains("public global::Meziantou.Framework.Toml.TomlTypeInfo<global::B.Item> Item", generated, StringComparison.Ordinal);
        Assert.Contains("public global::Meziantou.Framework.Toml.TomlTypeInfo<global::Options> Options2", generated, StringComparison.Ordinal);
        Assert.Contains("public global::Meziantou.Framework.Toml.TomlTypeInfo<global::Default> Default2", generated, StringComparison.Ordinal);
    }

    [Fact]
    public void Generator_ContextsWithTheSameNameInDifferentNamespaces_Compile()
    {
        var source = """
            #nullable enable
            using Meziantou.Framework.Toml.Serialization;

            public sealed class Person { public string? Name { get; set; } }

            namespace A
            {
                [TomlSerializable(typeof(Person))]
                internal partial class Ctx : TomlSerializerContext { }
            }

            namespace B
            {
                [TomlSerializable(typeof(Person))]
                internal partial class Ctx : TomlSerializerContext { }
            }
            """;

        var result = RunGeneratorTest(source);

        Assert.Empty(result.Diagnostics.Where(d => d.Severity >= DiagnosticSeverity.Warning));
        Assert.HasCount(2, result.GeneratedSources);
    }

    [Fact]
    public void Generator_NestedContext_Compiles()
    {
        var source = """
            #nullable enable
            using Meziantou.Framework.Toml.Serialization;

            public sealed class Person { public string? Name { get; set; } }

            public static partial class Outer<T>
            {
                internal partial record struct Middle
                {
                    [TomlSerializable(typeof(Person))]
                    internal partial class Ctx : TomlSerializerContext { }
                }
            }
            """;

        var result = RunGeneratorTest(source);

        Assert.Empty(result.Diagnostics.Where(d => d.Severity >= DiagnosticSeverity.Warning));
    }

    [Fact]
    public void Generator_ContextInNonPartialType_ReportsDiagnostic()
    {
        var source = """
            #nullable enable
            using Meziantou.Framework.Toml.Serialization;

            public sealed class Person { public string? Name { get; set; } }

            public static class Outer
            {
                [TomlSerializable(typeof(Person))]
                internal partial class Ctx : TomlSerializerContext { }
            }
            """;

        var diagnostics = RunGenerator(source);

        Assert.Contains(diagnostics, d => d.Id == "MFTOML001");
    }

    [Fact]
    public void Generator_KeywordNames_Compile()
    {
        var source = """
            #nullable enable
            using System.Collections.Generic;
            using Meziantou.Framework.Toml.Serialization;

            public sealed class @event
            {
                public string? @class { get; set; }

                public int @int { get; set; }

                [TomlInclude]
                public int @namespace;

                [TomlExtensionData]
                public Dictionary<string, object>? @object { get; set; }
            }

            public sealed record @record(string @class, int @base);

            [TomlSerializable(typeof(@event))]
            [TomlSerializable(typeof(@record))]
            internal partial class Ctx : TomlSerializerContext { }
            """;

        var result = RunGeneratorTest(source);

        Assert.Empty(result.Diagnostics.Where(d => d.Severity >= DiagnosticSeverity.Warning));
    }

    [Theory]
    [InlineData("public required int Count { get; set; }")]
    [InlineData("public int Count { get; init; }")]
    [InlineData("public int Count { get; set; }")]
    [InlineData("")]
    public void Generator_ExtensionDataWithConstructorOrInitializer_CompilesWithoutWarnings(string member)
    {
        var source = """
            #nullable enable
            using System.Collections.Generic;
            using Meziantou.Framework.Toml.Serialization;

            public sealed class WithInitializer
            {
                MEMBER

                [TomlExtensionData]
                public Dictionary<string, object>? Extra { get; set; }
            }

            public sealed class WithConstructor
            {
                public WithConstructor(string name) => Name = name;

                public string Name { get; }

                [TomlExtensionData]
                public Dictionary<string, object>? Extra { get; set; }
            }

            [TomlSerializable(typeof(WithInitializer))]
            [TomlSerializable(typeof(WithConstructor))]
            internal partial class Ctx : TomlSerializerContext { }
            """.Replace("MEMBER", member, StringComparison.Ordinal);

        var result = RunGeneratorTest(source);

        Assert.Empty(result.Diagnostics.Where(d => d.Severity >= DiagnosticSeverity.Warning));
    }

    [Fact]
    public void Generator_PropertyNamesWithSpecialCharacters_Compile()
    {
        var source = """
            #nullable enable
            using Meziantou.Framework.Toml.Serialization;

            public sealed class Person
            {
                [TomlRequired, TomlPropertyName("{name}")]
                public string? A { get; set; }

                [TomlRequired, TomlPropertyName("line\nbreak")]
                public string? B { get; set; }

                [TomlRequired, TomlPropertyName("line\u2028separator")]
                public string? C { get; set; }

                [TomlRequired, TomlPropertyName("quote\"back\\slash")]
                public string? D { get; set; }
            }

            public sealed record Ctor([property: TomlPropertyName("{key}")] string Value);

            [TomlSerializable(typeof(Person))]
            [TomlSerializable(typeof(Ctor))]
            internal partial class Ctx : TomlSerializerContext { }
            """;

        var result = RunGeneratorTest(source);

        Assert.Empty(result.Diagnostics.Where(d => d.Severity >= DiagnosticSeverity.Warning));
    }

    [Fact]
    public void Generator_TypeWithoutMembers_CompilesWithoutWarnings()
    {
        var source = """
            #nullable enable
            using Meziantou.Framework.Toml.Serialization;

            public sealed class Empty { }

            public sealed class EmptyWithConstructor
            {
                public EmptyWithConstructor(int value) { }
            }

            [TomlSerializable(typeof(Empty))]
            [TomlSerializable(typeof(EmptyWithConstructor))]
            internal partial class Ctx : TomlSerializerContext { }
            """;

        var result = RunGeneratorTest(source);

        Assert.Empty(result.Diagnostics.Where(d => d.Severity >= DiagnosticSeverity.Warning));
    }

    [Fact]
    public void Generator_UserTypesNamedLikeFrameworkTypes_Compile()
    {
        var source = """
            #nullable enable
            using Meziantou.Framework.Toml.Serialization;

            namespace App
            {
                public sealed class Type { }
                public sealed class Exception { }
                public sealed class StringComparison { }
                public sealed class TomlException { }
                public sealed class TomlTypeInfo { }
                public sealed class TomlReader { }
                public sealed class TomlWriter { }
                public sealed class TomlSerializerOptions { }
                public sealed class TomlTokenType { }
                public sealed class List<T> { }

                public sealed class Person
                {
                    [TomlRequired]
                    public string? Name { get; set; }

                    public System.Collections.Generic.List<int>? Values { get; set; }

                    [TomlExtensionData]
                    public System.Collections.Generic.Dictionary<string, object>? Extra { get; set; }
                }

                public sealed record Ctor(string Name, int Age);

                [TomlSourceGenerationOptions(PropertyNameCaseInsensitive = true)]
                [TomlSerializable(typeof(Person))]
                [TomlSerializable(typeof(Ctor))]
                internal partial class Ctx : TomlSerializerContext { }
            }
            """;

        var result = RunGeneratorTest(source);

        Assert.Empty(result.Diagnostics.Where(d => d.Severity >= DiagnosticSeverity.Warning));
    }

    [Fact]
    public void Generator_PrivateAnnotatedConstructor_ReportsDiagnostic()
    {
        var source = """
            #nullable enable
            using Meziantou.Framework.Toml.Serialization;

            public sealed class Person
            {
                [TomlConstructor]
                private Person(string name) => Name = name;

                public Person() { }

                public string? Name { get; set; }
            }

            [TomlSerializable(typeof(Person))]
            internal partial class Ctx : TomlSerializerContext { }
            """;

        var diagnostics = RunGenerator(source);

        Assert.Contains(diagnostics, d => d.Id == "MFTOML013" && d.Severity == DiagnosticSeverity.Error);
    }

    [Fact]
    public void Generator_WarnsForJsonSerializableUsage()
    {
        var source = """
            #nullable enable
            using System.Text.Json.Serialization;
            using Meziantou.Framework.Toml.Serialization;

            [JsonSerializable(typeof(Person))]
            internal partial class Ctx : TomlSerializerContext { }

            public sealed class Person { public string Name { get; set; } = ""; }
            """;

        var diagnostics = RunGenerator(source);
        Assert.True(diagnostics.Any(d => d.Id == "MFTOML008"));
    }

    [Fact]
    public void Generator_ReportsInvalidTypeInfoPropertyName()
    {
        var source = """
            #nullable enable
            using Meziantou.Framework.Toml.Serialization;

            [TomlSerializable(typeof(Person), TypeInfoPropertyName = "not-valid")]
            internal partial class Ctx : TomlSerializerContext { }

            public sealed class Person { public string Name { get; set; } = ""; }
            """;

        var diagnostics = RunGenerator(source);
        Assert.True(diagnostics.Any(d => d.Id == "MFTOML005" && d.GetMessage().Contains("TypeInfoPropertyName", StringComparison.Ordinal)));
    }

    [Fact]
    public void Generator_PreservesNullableReferenceLocals()
    {
        var source = """
            #nullable enable
            using System.Text.Json.Serialization;
            using Meziantou.Framework.Toml.Serialization;

            public sealed class InitOptions
            {
                public string? NullableMock { get; init; }
                public string NonNullableMock { get; init; } = string.Empty;
            }

            public sealed class CtorOptions
            {
                [JsonConstructor]
                public CtorOptions(string? nullableMock, string nonNullableMock)
                {
                    NullableMock = nullableMock;
                    NonNullableMock = nonNullableMock;
                }

                public string? NullableMock { get; }
                public string NonNullableMock { get; }
            }

            [TomlSerializable(typeof(InitOptions))]
            [TomlSerializable(typeof(CtorOptions))]
            internal partial class Ctx : TomlSerializerContext { }
            """;

        var result = RunGeneratorTest(source);
        var generatedSource = string.Join(Environment.NewLine, result.GeneratedSources);

        Assert.False(result.Diagnostics.Any(d => d.Id is "CS8600" or "CS8601"));
        Assert.Contains("string? __memberValue0 = default;", generatedSource);
        Assert.Contains("string __memberValue1 = default!;", generatedSource);
        Assert.Contains("string? __arg0 = default;", generatedSource);
        Assert.Contains("string __arg1 = default!;", generatedSource);
    }

    [Fact]
    public void Generator_EmitsGeneratedCodeCoverageExclusionAttributes()
    {
        var source = """
            #nullable enable
            using Meziantou.Framework.Toml.Serialization;

            public sealed class MetadataPayload
            {
                public string Value { get; set; } = string.Empty;
            }

            [TomlSerializable(typeof(MetadataPayload))]
            internal partial class MetadataTomlContext : TomlSerializerContext { }
            """;

        var generatedSource = RunGeneratorTest(source).GeneratedSources.Single();
        var generatedCodeAttribute = "[global::System.CodeDom.Compiler.GeneratedCode(\"Meziantou.Framework.Toml.SourceGenerator\", \"" +
            (typeof(TomlSerializerContextGenerator).Assembly.GetName().Version?.ToString() ?? "0.0.0.0") +
            "\")]";
        const string ExcludeFromCoverageAttribute = "[global::System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage]";

        Assert.StartsWith("// <auto-generated/>", generatedSource);
        Assert.Contains(generatedCodeAttribute + Environment.NewLine +
            ExcludeFromCoverageAttribute + Environment.NewLine +
            "partial class MetadataTomlContext", generatedSource);
        Assert.Contains("    " + generatedCodeAttribute + Environment.NewLine +
            "    " + ExcludeFromCoverageAttribute + Environment.NewLine +
            "    private sealed class __TomlTypeInfo_MetadataPayload", generatedSource);
    }

    [Fact]
    public void Generator_DoesNotReferenceInternalRuntimeHelpers_FromConsumerAssembly()
    {
        var source = """
            #nullable enable
            using System.Text.Json.Serialization;
            using Meziantou.Framework.Toml.Serialization;

            public sealed class Root
            {
                [JsonPropertyName("child")]
                public Child Child { get; } = new();
            }

            public sealed class Child
            {
                public string Name { get; set; } = "";
            }

            [TomlSourceGenerationOptions(PreferredObjectCreationHandling = TomlObjectCreationHandling.Populate)]
            [TomlSerializable(typeof(Root))]
            internal partial class Ctx : TomlSerializerContext { }
            """;

        var result = RunGeneratorTest(source);
        var generatedSource = string.Join(Environment.NewLine, result.GeneratedSources);

        Assert.False(result.Diagnostics.Any(d => d.Id == "CS0122"));
        Assert.DoesNotContain("TomlTableHeaderExtensionHelper", generatedSource);
    }

    [Fact]
    public void Generator_DoesNotReferenceInternalSingleOrArrayHelper_FromConsumerAssembly()
    {
        var source = """
            #nullable enable
            using System.Collections.Generic;
            using Meziantou.Framework.Toml.Serialization;

            public sealed class ProviderConfig
            {
                [TomlSingleOrArray]
                public required IReadOnlyList<string> ApiKey { get; init; }
            }

            [TomlSerializable(typeof(ProviderConfig))]
            internal partial class Ctx : TomlSerializerContext { }
            """;

        var result = RunGeneratorTest(source);
        var generatedSource = string.Join(Environment.NewLine, result.GeneratedSources);

        Assert.False(result.Diagnostics.Any(d => d.Id == "CS0122"));
        Assert.DoesNotContain("TomlSingleOrArrayCollectionHelper", generatedSource);
    }

    [Fact]
    public void Generator_ReportsInvalidDerivedTypeMapping_WhenDerivedTypeIsNotAssignable()
    {
        var source = """
            #nullable enable
            using Meziantou.Framework.Toml.Serialization;

            [TomlSerializable(typeof(Animal))]
            [TomlDerivedTypeMapping(typeof(Animal), typeof(NotAnimal), "cat")]
            internal partial class Ctx : TomlSerializerContext { }

            [TomlPolymorphic(TypeDiscriminatorPropertyName = "kind")]
            public abstract class Animal { }
            public sealed class NotAnimal { }
            """;

        var diagnostics = RunGenerator(source);

        Assert.True(diagnostics.Any(d => d.Id == "MFTOML009"));
    }

    [Fact]
    public void Generator_ReportsInvalidDerivedTypeMapping_WhenDiscriminatorIsEmpty()
    {
        var source = """
            #nullable enable
            using Meziantou.Framework.Toml.Serialization;

            [TomlSerializable(typeof(Animal))]
            [TomlDerivedTypeMapping(typeof(Animal), typeof(Cat), "")]
            internal partial class Ctx : TomlSerializerContext { }

            [TomlPolymorphic(TypeDiscriminatorPropertyName = "kind")]
            public abstract class Animal { }
            public sealed class Cat : Animal { }
            """;

        var diagnostics = RunGenerator(source);

        Assert.True(diagnostics.Any(d => d.Id == "MFTOML009"));
    }

    [Fact]
    public void Generator_WarnsWhenDerivedTypeMappingBaseHasNoPolymorphicConfiguration()
    {
        var source = """
            #nullable enable
            using Meziantou.Framework.Toml.Serialization;

            [TomlSerializable(typeof(Animal))]
            [TomlDerivedTypeMapping(typeof(Animal), typeof(Cat), "cat")]
            internal partial class Ctx : TomlSerializerContext { }

            public abstract class Animal { public string Name { get; set; } = ""; }
            public sealed class Cat : Animal { public int Lives { get; set; } }
            """;

        var diagnostics = RunGenerator(source);

        Assert.True(diagnostics.Any(d => d.Id == "MFTOML010"));
    }

    [Fact]
    public void Generator_UnrelatedEdit_KeepsTheGeneratedOutput()
    {
        var source = """
            using Meziantou.Framework.Toml.Serialization;

            [TomlSerializable(typeof(Person))]
            internal partial class Ctx : TomlSerializerContext { }

            public sealed class Person { public string Name { get; set; } = ""; }
            """;
        var parseOptions = new CSharpParseOptions(LanguageVersion.Latest);
        var compilation = CSharpCompilation.Create(
            assemblyName: "Meziantou.Framework.Toml.SourceGeneration.Tests.Input",
            syntaxTrees: [CSharpSyntaxTree.ParseText(source, parseOptions)],
            references: CreateReferences(),
            options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable));
        GeneratorDriver driver = CSharpGeneratorDriver.Create([new TomlSerializerContextGenerator().AsSourceGenerator()], parseOptions: parseOptions, driverOptions: new GeneratorDriverOptions(IncrementalGeneratorOutputKind.None, trackIncrementalGeneratorSteps: true));
        driver = driver.RunGenerators(compilation);

        driver = driver.RunGenerators(compilation.AddSyntaxTrees(CSharpSyntaxTree.ParseText("public sealed class Other { }", parseOptions)));

        var result = Assert.Single(driver.GetRunResult().Results);
        Assert.Single(result.GeneratedSources);
        var outputs = result.TrackedSteps["TomlContextOutputs"].SelectMany(static step => step.Outputs);
        Assert.All(outputs, output => Assert.True(output.Reason is IncrementalStepRunReason.Cached or IncrementalStepRunReason.Unchanged, output.Reason.ToString()));
    }

    [Theory]
    [InlineData("MFTOML014", "[TomlSerializable(typeof(Person))] internal partial class Ctx<T> : TomlSerializerContext { } public sealed class Person { public string Name { get; set; } = \"\"; }")]
    [InlineData("MFTOML015", "[TomlSerializable(typeof(Person))] internal partial class Ctx : TomlSerializerContext { } file sealed class Person { public string Name { get; set; } = \"\"; }")]
    [InlineData("MFTOML015", "[TomlSerializable(typeof(Outer))] internal partial class Ctx : TomlSerializerContext { } public class Outer { private sealed class Inner { public int X { get; set; } } [TomlInclude] private Inner? Value { get; set; } }")]
    [InlineData("MFTOML015", "[TomlSerializable(typeof(Outer))] internal partial class Ctx : TomlSerializerContext { } public class Outer { private sealed class Inner { public int X { get; set; } } [TomlInclude] private System.Collections.Generic.List<Inner>? Values { get; set; } }")]
    [InlineData("MFTOML015", "public partial class Outer { private sealed class Person { public string Name { get; set; } = \"\"; } [TomlSerializable(typeof(Person))] internal partial class Ctx : TomlSerializerContext { } }")]
    [InlineData("MFTOML015", "public partial class Outer { private sealed class Person { public string Name { get; set; } = \"\"; } [TomlSerializable(typeof(Person[]))] internal partial class Ctx : TomlSerializerContext { } }")]
    [InlineData("MFTOML016", "[TomlSerializable(typeof(Person))] internal partial class Ctx : TomlSerializerContext { } public sealed class Person { public System.Action? Callback { get; set; } }")]
    [InlineData("MFTOML016", "[TomlSerializable(typeof(Person))] internal partial class Ctx : TomlSerializerContext { } public sealed class Person { public string Name { get; set; } = \"\"; public System.ReadOnlySpan<char> Span => System.MemoryExtensions.AsSpan(Name); }")]
    [InlineData("MFTOML017", "[TomlSerializable(typeof(Root))] internal partial class Ctx : TomlSerializerContext { } public abstract class Root { public int X { get; set; } }")]
    [InlineData("MFTOML017", "[TomlSerializable(typeof(IRoot))] internal partial class Ctx : TomlSerializerContext { } public interface IRoot { int X { get; set; } }")]
    [InlineData("MFTOML017", "[TomlSerializable(typeof(D))] internal partial class Ctx : TomlSerializerContext { } public delegate void D();")]
    [InlineData("MFTOML017", "[TomlSerializable(typeof(int[,]))] internal partial class Ctx : TomlSerializerContext { }")]
    [InlineData("MFTOML017", "[TomlSerializable(typeof(System.Span<int>))] internal partial class Ctx : TomlSerializerContext { }")]
    [InlineData("MFTOML017", "[TomlSerializable(typeof(System.Collections.Generic.List<>))] internal partial class Ctx : TomlSerializerContext { }")]
    [InlineData("MFTOML003", "[TomlSerializable(typeof(Root))] internal partial class Ctx : TomlSerializerContext { } public sealed class Root { public object[,] X { get; set; } = new object[0, 0]; }")]
    [InlineData("MFTOML004", "[TomlSerializable(typeof(Root))] internal partial class Ctx : TomlSerializerContext { } public sealed class Root { public System.Collections.Generic.Dictionary<int, string> X { get; set; } = new(); }")]
    public void Generator_UnsupportedContextOrType_ReportsOnlyADiagnostic(string id, string declarations)
    {
        var source = "#nullable enable\nusing Meziantou.Framework.Toml.Serialization;\n" + declarations;

        var diagnostics = RunGenerator(source);

        // The context is not generated, so the compiler also reports its missing members (CS0534), like for MFTOML001
        Assert.Single(diagnostics, d => d.Id == id);
        Assert.All(diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error && d.Id != id), d => Assert.Equal("CS0534", d.Id));
    }

    [Fact]
    public void Generator_PrivateTypeNextToANestedContext_Compiles()
    {
        var source = """
            #nullable enable
            using Meziantou.Framework.Toml.Serialization;

            public partial class Outer
            {
                private sealed class Person { public string Name { get; set; } = ""; }

                [TomlSerializable(typeof(Person))]
                private partial class Ctx : TomlSerializerContext { }
            }
            """;

        var diagnostics = RunGenerator(source);

        Assert.DoesNotContain(diagnostics, d => d.Severity == DiagnosticSeverity.Error);
    }

    [Fact]
    public void Generator_IgnoredDelegateMember_Compiles()
    {
        var source = """
            #nullable enable
            using Meziantou.Framework.Toml.Serialization;

            [TomlSerializable(typeof(Person))]
            internal partial class Ctx : TomlSerializerContext { }

            public sealed class Person
            {
                public string Name { get; set; } = "";

                [TomlIgnore]
                public System.Action? Callback { get; set; }
            }
            """;

        var diagnostics = RunGenerator(source);

        Assert.DoesNotContain(diagnostics, d => d.Severity == DiagnosticSeverity.Error);
    }

    [Fact]
    public void Generator_InternalMembersOfAnotherAssembly_UseAccessors()
    {
        var librarySource = """
            using Meziantou.Framework.Toml.Serialization;

            public class Person
            {
                [TomlInclude]
                public int Age { get; protected internal set; }

                [TomlInclude]
                internal string Name { get; set; } = "";

                [TomlInclude]
                internal int Id;
            }
            """;
        var library = CSharpCompilation.Create(
            assemblyName: "Meziantou.Framework.Toml.SourceGeneration.Tests.Library",
            syntaxTrees: [CSharpSyntaxTree.ParseText(librarySource, new CSharpParseOptions(LanguageVersion.Latest))],
            references: CreateReferences(),
            options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable));
        using var image = new MemoryStream();
        var emitResult = library.Emit(image);
        Assert.True(emitResult.Success, string.Join(Environment.NewLine, emitResult.Diagnostics));

        var source = """
            using Meziantou.Framework.Toml.Serialization;

            [TomlSerializable(typeof(Person))]
            internal partial class Ctx : TomlSerializerContext { }
            """;

        var diagnostics = RunGeneratorTest(source, MetadataReference.CreateFromImage(image.ToArray())).Diagnostics;

        Assert.DoesNotContain(diagnostics, d => d.Severity == DiagnosticSeverity.Error);
    }

    [Fact]
    public void Generator_MemberDiagnostic_IsReportedOnce()
    {
        var source = """
            using Meziantou.Framework.Toml;
            using Meziantou.Framework.Toml.Serialization;

            [TomlSerializable(typeof(Person))]
            internal partial class Ctx : TomlSerializerContext { }

            public sealed class Person
            {
                [TomlStringStyle(TomlStringStyle.Literal)]
                public int Value { get; set; }
            }
            """;

        var diagnostics = RunGenerator(source);

        Assert.Single(diagnostics, d => d.Id == "MFTOML011");
    }

    [Theory]
    [InlineData("[TomlPropertyName(\"\")] public int Value { get; set; }")]
    [InlineData("[TomlPropertyName(\"\")] [TomlInclude] public int Value;")]
    public void Generator_EmptyPropertyName_ReportsDiagnostic(string member)
    {
        var source = "using Meziantou.Framework.Toml.Serialization;\n[TomlSerializable(typeof(Person))]\ninternal partial class Ctx : TomlSerializerContext { }\npublic sealed class Person { " + member + " }";

        var diagnostics = RunGenerator(source);

        var diagnostic = Assert.Single(diagnostics, d => d.Severity == DiagnosticSeverity.Error);
        Assert.Equal("MFTOML011", diagnostic.Id);
        Assert.Contains("[TomlPropertyName] cannot be empty", diagnostic.GetMessage(CultureInfo.InvariantCulture), StringComparison.Ordinal);
        Assert.Equal(3, diagnostic.Location.GetLineSpan().StartLinePosition.Line);
    }

    // The offending declaration is on the line marked with /*here*/
    [Theory]
    [InlineData("MFTOML003", "public sealed class Person {\n public int X { get; set; }\n /*here*/ public System.IDisposable? Handle { get; set; }\n}")]
    [InlineData("MFTOML004", "public sealed class Person {\n public int X { get; set; }\n /*here*/ public System.Collections.Generic.Dictionary<int, string> Values { get; set; } = new();\n}")]
    [InlineData("MFTOML006", "public sealed class Person {\n public int X { get; set; }\n /*here*/ [TomlExtensionData] public int Extra { get; set; }\n}")]
    [InlineData("MFTOML007", "[TomlPolymorphic]\n[TomlDerivedType(typeof(Derived), \"a\")]\n/*here*/ [TomlDerivedType(typeof(Other), \"a\")]\npublic class Person { } public sealed class Derived : Person { } public sealed class Other : Person { }")]
    public void Generator_MemberDiagnostic_IsReportedOnTheMember(string id, string declarations)
    {
        var source = "#nullable enable\nusing Meziantou.Framework.Toml.Serialization;\n[TomlSerializable(typeof(Person))]\ninternal partial class Ctx : TomlSerializerContext { }\n" + declarations;
        var expectedLine = Array.FindIndex(source.Split('\n'), line => line.Contains("/*here*/", StringComparison.Ordinal));

        var diagnostics = RunGenerator(source);

        var diagnostic = Assert.Single(diagnostics, d => d.Id == id);

        // A location in the syntax tree, so '#pragma warning disable' applies to it
        Assert.Equal(LocationKind.SourceFile, diagnostic.Location.Kind);
        Assert.Equal(expectedLine, diagnostic.Location.GetLineSpan().StartLinePosition.Line);
    }

    [Fact]
    public void Generator_DisallowNullOnNullableMembers_Compiles()
    {
        var source = """
            #nullable enable
            using System.Diagnostics.CodeAnalysis;
            using Meziantou.Framework.Toml.Serialization;

            [TomlSerializable(typeof(M))]
            [TomlSerializable(typeof(C))]
            [TomlSerializable(typeof(G<string?>))]
            internal partial class Ctx : TomlSerializerContext { }

            public class M { [DisallowNull] public string? A { get; set; } [DisallowNull] public int? B { get; set; } }
            public class C { public C([DisallowNull] string? a) { A = a; } public string? A { get; } }
            public class G<T> where T : class { public T? A { get; set; } }
            """;

        var diagnostics = RunGenerator(source);

        Assert.DoesNotContain(diagnostics, d => d.Severity == DiagnosticSeverity.Error);
    }

    [Theory]
    [InlineData("[TomlInclude] protected int P { get; set; }")]
    [InlineData("[TomlInclude] public int P { get; protected set; }")]
    [InlineData("[TomlInclude] protected int F;")]
    [InlineData("[TomlInclude] protected internal int P { get; set; }")]
    public void Generator_ProtectedMembersWithAContextNestedInADerivedType_UseAccessors(string member)
    {
        var source = """
            using Meziantou.Framework.Toml.Serialization;

            public class Model { public int Q { get; set; } MEMBER }

            public partial class Holder : Model
            {
                [TomlSerializable(typeof(Model))]
                internal partial class Ctx : TomlSerializerContext { }
            }
            """.Replace("MEMBER", member, StringComparison.Ordinal);

        var diagnostics = RunGenerator(source);

        Assert.DoesNotContain(diagnostics, d => d.Severity == DiagnosticSeverity.Error);
    }

    [Fact]
    public void Generator_ProtectedConstructorWithAContextNestedInADerivedType_ReportsDiagnostic()
    {
        var source = """
            using Meziantou.Framework.Toml.Serialization;

            public class Model { [TomlConstructor] protected Model(int q) { Q = q; } public int Q { get; } }

            public partial class Holder : Model
            {
                public Holder() : base(0) { }

                [TomlSerializable(typeof(Model))]
                internal partial class Ctx : TomlSerializerContext { }
            }
            """;

        var diagnostics = RunGenerator(source);

        Assert.Single(diagnostics, d => d.Severity == DiagnosticSeverity.Error && d.Id == "MFTOML013");
        Assert.DoesNotContain(diagnostics, d => d.Id.StartsWith("CS", StringComparison.Ordinal));
    }

    [Fact]
    public void Generator_InternalConverterOfAnotherAssembly_ReportsDiagnostic()
    {
        var librarySource = """
            using System;
            using Meziantou.Framework.Toml;
            using Meziantou.Framework.Toml.Serialization;

            internal sealed class InternalConverter : TomlConverter<string>
            {
                public override string? Read(TomlReader reader) => reader.GetString();
                public override void Write(TomlWriter writer, string value) => writer.WriteStringValue(value);
            }

            public class Model
            {
                [TomlConverter(typeof(InternalConverter))]
                public string? S { get; set; }
            }
            """;
        var library = CSharpCompilation.Create(
            assemblyName: "Meziantou.Framework.Toml.SourceGeneration.Tests.ConverterLibrary",
            syntaxTrees: [CSharpSyntaxTree.ParseText(librarySource, new CSharpParseOptions(LanguageVersion.Latest))],
            references: CreateReferences(),
            options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable));
        using var image = new MemoryStream();
        var emitResult = library.Emit(image);
        Assert.True(emitResult.Success, string.Join(Environment.NewLine, emitResult.Diagnostics));

        var source = """
            using Meziantou.Framework.Toml.Serialization;

            [TomlSerializable(typeof(Model))]
            internal partial class Ctx : TomlSerializerContext { }
            """;

        var diagnostics = RunGeneratorTest(source, MetadataReference.CreateFromImage(image.ToArray())).Diagnostics;

        var error = Assert.Single(diagnostics, d => d.Severity == DiagnosticSeverity.Error);
        Assert.Equal("MFTOML002", error.Id);
    }

    [Fact]
    public void Generator_PrivateConverterNextToANestedContext_Compiles()
    {
        var source = """
            #nullable enable
            using Meziantou.Framework.Toml;
            using Meziantou.Framework.Toml.Serialization;

            public partial class Outer
            {
                private sealed class UpperConverter : TomlConverter<string>
                {
                    public override string? Read(TomlReader reader) => reader.GetString();
                    public override void Write(TomlWriter writer, string value) => writer.WriteStringValue(value.ToUpperInvariant());
                }

                private sealed class Model
                {
                    [TomlConverter(typeof(UpperConverter))]
                    public string? S { get; set; }
                }

                [TomlSerializable(typeof(Model))]
                private partial class Ctx : TomlSerializerContext { }
            }
            """;

        var diagnostics = RunGenerator(source);

        Assert.DoesNotContain(diagnostics, d => d.Severity == DiagnosticSeverity.Error);
    }

    [Fact]
    public void Generator_GenericRootWithNullableTypeArgument_CompilesWithoutWarnings()
    {
        var source = """
            #nullable enable
            using System.Collections.Generic;
            using Meziantou.Framework.Toml.Serialization;

            [TomlSerializable(typeof(G<string?>))]
            [TomlSerializable(typeof(Dictionary<string, List<string?>>))]
            internal partial class Ctx : TomlSerializerContext { }

            public class G<T>
            {
                public T Value { get; set; } = default!;
                public List<T> Items { get; set; } = [];
                public T? Maybe { get; set; }
                public Dictionary<string, T> Map { get; set; } = [];
            }
            """;

        var diagnostics = RunGenerator(source);

        Assert.DoesNotContain(diagnostics, d => d.Severity >= DiagnosticSeverity.Warning);
    }

    private static ImmutableArray<Diagnostic> RunGenerator(string source)
        => RunGeneratorTest(source).Diagnostics;

    private static GeneratorTestResult RunGeneratorTest(string source, MetadataReference? additionalReference = null)
    {
        var parseOptions = new CSharpParseOptions(LanguageVersion.Latest);
        var syntaxTree = CSharpSyntaxTree.ParseText(source, parseOptions);
        var references = CreateReferences();
        if (additionalReference is not null)
        {
            references.Add(additionalReference);
        }

        var compilation = CSharpCompilation.Create(
            assemblyName: "Meziantou.Framework.Toml.SourceGeneration.Tests.Input",
            syntaxTrees: new[] { syntaxTree },
            references: references,
            options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable));

        IIncrementalGenerator generator = new TomlSerializerContextGenerator();
        GeneratorDriver driver = CSharpGeneratorDriver.Create(generator);
        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out var outputCompilation, out var generatorDiagnostics);

        var inputSyntaxTrees = compilation.SyntaxTrees.ToHashSet();
        var generatedSources = driver.GetRunResult()
            .Results
            .SelectMany(static result => result.GeneratedSources)
            .Select(static generated => generated.SourceText.ToString())
            .ToImmutableArray();
        var outputDiagnostics = outputCompilation.GetDiagnostics();
        var generatedCodeWarnings = outputDiagnostics
            .Where(d => d.Severity == DiagnosticSeverity.Warning &&
                        d.Location.SourceTree is { } tree &&
                        !inputSyntaxTrees.Contains(tree))
            .ToImmutableArray();

        if (!generatedCodeWarnings.IsDefaultOrEmpty)
        {
            Assert.Fail(
                "Generated code produced compiler warnings:" + Environment.NewLine +
                string.Join(Environment.NewLine, generatedCodeWarnings.Select(static d => d.ToString())));
        }

        return new GeneratorTestResult(generatorDiagnostics.Concat(outputDiagnostics).ToImmutableArray(), generatedSources);
    }

    private readonly record struct GeneratorTestResult(ImmutableArray<Diagnostic> Diagnostics, ImmutableArray<string> GeneratedSources);

    private static List<MetadataReference> CreateReferences()
    {
        var unique = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var references = new List<MetadataReference>();

        void Add(string path)
        {
            if (string.IsNullOrEmpty(path) || !File.Exists(path))
            {
                return;
            }

            if (unique.Add(path))
            {
                references.Add(MetadataReference.CreateFromFile(path));
            }
        }

        // Prefer the trusted platform assemblies list to get a coherent reference set.
        var tpa = AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") as string;
        if (tpa is { Length: > 0 } tpaValue)
        {
            foreach (var path in tpaValue.Split(Path.PathSeparator))
            {
                Add(path);
            }
        }
        else
        {
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                if (assembly.IsDynamic)
                {
                    continue;
                }

                Add(assembly.Location);
            }
        }

        Add(typeof(object).Assembly.Location);
        Add(typeof(Enumerable).Assembly.Location);
        Add(typeof(List<>).Assembly.Location);
        Add(typeof(JsonSerializableAttribute).Assembly.Location);
        Add(typeof(TomlSerializerContext).Assembly.Location);

        return references;
    }
}
