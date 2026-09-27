using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Globalization;
using System.IO;
using System.Linq;
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
                [TomlConstructor]
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
                [TomlConstructor]
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

    [Theory]
    [InlineData("PropertyNamingPolicy = (TomlKnownNamingPolicy)99", true)]
    [InlineData("DictionaryKeyPolicy = (TomlKnownNamingPolicy)99", true)]
    [InlineData("PropertyNamingPolicy = TomlKnownNamingPolicy.Unspecified", false)]
    [InlineData("RootValueKeyName = \"\\uD800\"", true)]
    [InlineData("RootValueKeyName = \"\"", true)]
    [InlineData("RootValueKeyName = \" \"", false)]
    public void Generator_ValidatesOptionsLikeTheRuntime(string option, bool isError)
    {
        var diagnostics = RunGenerator($$"""
            #nullable enable
            using Meziantou.Framework.Toml;
            using Meziantou.Framework.Toml.Serialization;

            [TomlSourceGenerationOptions({{option}})]
            [TomlSerializable(typeof(Person))]
            internal partial class Ctx : TomlSerializerContext { }

            public sealed class Person { public string Name { get; set; } = ""; }
            """);

        Assert.Equal(isError, diagnostics.Any(d => d.Id == "MFTOML005"));
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

    // Like the reflection resolver, which rejects the whole type, a TomlConverter<T> must convert the type of its member
    [Fact]
    public void Generator_ReportsAConverterOfAnotherType()
    {
        var source = """
            #nullable enable
            using System.Collections.Generic;
            using Meziantou.Framework.Toml.Serialization;

            public sealed class Int32Converter : TomlConverter<int>
            {
                public override int Read(TomlReader reader) => 0;
                public override void Write(TomlWriter writer, int value) { }
            }

            public sealed class Model
            {
                [TomlConverter(typeof(Int32Converter))]
                public List<int>? Values { get; set; }

                [TomlConverter(typeof(Int32Converter))]
                public int? Nullable { get; set; }

                [TomlConverter(typeof(Int32Converter))]
                public int Value { get; set; }
            }

            [TomlSerializable(typeof(Model))]
            internal partial class Ctx : TomlSerializerContext { }
            """;

        var diagnostics = RunGenerator(source);

        var diagnostic = Assert.Single(diagnostics, d => d.Id == "MFTOML002");
        Assert.Contains("not 'System.Collections.Generic.List<int>?'", diagnostic.GetMessage(System.Globalization.CultureInfo.InvariantCulture), StringComparison.Ordinal);
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

    [Theory]
    [InlineData("[TomlSerializable(typeof(Person), TypeInfoPropertyName = \"not-valid\")]", "must be a valid C# identifier")]
    [InlineData("[TomlSerializable(typeof(Person), TypeInfoPropertyName = \"class\")]", "must be a valid C# identifier")]
    [InlineData("[TomlSerializable(typeof(Person), TypeInfoPropertyName = \"X1\")][TomlSerializable(typeof(Person), TypeInfoPropertyName = \"Y1\")]", "are both used for 'Person'")]
    [InlineData("[TomlSerializable(typeof(Person), TypeInfoPropertyName = \"Options\")]", "conflicts with a member of the context")]
    [InlineData("[TomlSerializable(typeof(Person), TypeInfoPropertyName = \"Default\")]", "conflicts with a member of the context")]
    [InlineData("[TomlSerializable(typeof(Person), TypeInfoPropertyName = \"Helper\")]", "conflicts with a member of the context")]
    [InlineData("[TomlSerializable(typeof(Person), TypeInfoPropertyName = \"Item\")][TomlSerializable(typeof(Address), TypeInfoPropertyName = \"Item\")]", "is used for both 'Person' and 'Address'")]
    [InlineData("[TomlSerializable(typeof(Person), TypeInfoPropertyName = \"Foo\")][TomlSerializable(typeof(Address), TypeInfoPropertyName = \"_Foo\")]", "generated for another TypeInfoPropertyName")]
    [InlineData("[TomlSerializable(typeof(Person), TypeInfoPropertyName = \"CreateFoo\")][TomlSerializable(typeof(Address), TypeInfoPropertyName = \"Foo\")]", "generated for another TypeInfoPropertyName")]
    public void Generator_ReportsInvalidTypeInfoPropertyName(string attributes, string message)
    {
        var source = $$"""
            #nullable enable
            using Meziantou.Framework.Toml.Serialization;

            {{attributes}}
            internal partial class Ctx : TomlSerializerContext
            {
                public static void Helper() { }
            }

            public sealed class Person { public string Name { get; set; } = ""; }
            public sealed class Address { public string City { get; set; } = ""; }
            """;

        var diagnostics = RunGenerator(source);

        var diagnostic = Assert.Single(diagnostics, d => d.Id == "MFTOML005");
        Assert.Contains(message, diagnostic.GetMessage(CultureInfo.InvariantCulture));
        Assert.All(diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error && d.Id != "MFTOML005"), d => Assert.Equal("CS0534", d.Id));
    }

    // A base member accessed through a cast, because a derived member has its name, keeps the nullable type arguments of the
    // base type
    [Fact]
    public void Generator_CastToABaseTypeWithANullableTypeArgument_CompilesWithoutWarnings()
    {
        var source = """
            #nullable enable
            using Meziantou.Framework.Toml.Serialization;

            public class GenBase<T>
            {
                public T? X { get; set; }
            }

            public sealed class GenDerived : GenBase<string?>
            {
                public static new int X { get; set; }
            }

            [TomlSerializable(typeof(GenDerived))]
            internal partial class Ctx : TomlSerializerContext { }
            """;

        var result = RunGeneratorTest(source);

        Assert.Empty(result.Diagnostics.Where(d => d.Severity >= DiagnosticSeverity.Warning));
    }

    [Fact]
    public void Generator_PreservesNullableReferenceLocals()
    {
        var source = """
            #nullable enable
            using Meziantou.Framework.Toml.Serialization;

            // A generic type cannot use accessors, so its init-only members are set in an object initializer from locals
            public sealed class InitOptions<T>
            {
                public string? NullableMock { get; init; }
                public string NonNullableMock { get; init; } = string.Empty;
            }

            public sealed class CtorOptions
            {
                [TomlConstructor]
                public CtorOptions(string? nullableMock, string nonNullableMock)
                {
                    NullableMock = nullableMock;
                    NonNullableMock = nonNullableMock;
                }

                public string? NullableMock { get; }
                public string NonNullableMock { get; }
            }

            [TomlSerializable(typeof(InitOptions<int>))]
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
            using Meziantou.Framework.Toml.Serialization;

            public sealed class Root
            {
                [TomlPropertyName("child")]
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
    [InlineData("MFTOML015", "internal sealed class Person { public string Name { get; set; } = \"\"; } public partial class Outer { [TomlSerializable(typeof(Person))] protected partial class Ctx : TomlSerializerContext { } }")]
    [InlineData("MFTOML016", "[TomlSerializable(typeof(Person))] internal partial class Ctx : TomlSerializerContext { } public sealed class Person { public System.Action? Callback { get; set; } }")]
    [InlineData("MFTOML016", "[TomlSerializable(typeof(Person))] internal partial class Ctx : TomlSerializerContext { } public sealed class Person { public string Name { get; set; } = \"\"; public System.ReadOnlySpan<char> Span => System.MemoryExtensions.AsSpan(Name); }")]
    [InlineData("MFTOML017", "[TomlSerializable(typeof(Root))] internal partial class Ctx : TomlSerializerContext { } public abstract class Root { public int X { get; set; } }")]
    [InlineData("MFTOML017", "[TomlSerializable(typeof(IRoot))] internal partial class Ctx : TomlSerializerContext { } public interface IRoot { int X { get; set; } }")]
    [InlineData("MFTOML017", "[TomlSerializable(typeof(D))] internal partial class Ctx : TomlSerializerContext { } public delegate void D();")]
    [InlineData("MFTOML017", "[TomlSerializable(typeof(int[,]))] internal partial class Ctx : TomlSerializerContext { }")]
    [InlineData("MFTOML017", "[TomlSerializable(typeof(System.Span<int>))] internal partial class Ctx : TomlSerializerContext { }")]
    [InlineData("MFTOML017", "[TomlSerializable(typeof(System.Collections.Generic.List<>))] internal partial class Ctx : TomlSerializerContext { }")]
    [InlineData("MFTOML003", "[TomlSerializable(typeof(Root))] internal partial class Ctx : TomlSerializerContext { } public sealed class Root { public object[,] X { get; set; } = new object[0, 0]; }")]
    [InlineData("MFTOML003", "[TomlSerializable(typeof(Root))] internal partial class Ctx : TomlSerializerContext { } public sealed class Root { public System.Collections.Generic.Queue<int> X { get; set; } = new(); }")]
    [InlineData("MFTOML003", "[TomlSerializable(typeof(Root))] internal partial class Ctx : TomlSerializerContext { } public sealed class Root { public System.Collections.Concurrent.ConcurrentQueue<int> X { get; set; } = new(); }")]
    [InlineData("MFTOML017", "[TomlSerializable(typeof(System.Collections.Generic.Stack<int>))] internal partial class Ctx : TomlSerializerContext { }")]
    [InlineData("MFTOML003", "[TomlSerializable(typeof(Root))] internal partial class Ctx : TomlSerializerContext { } public sealed class Root { public (int A, string B) Pair { get; set; } }")]
    [InlineData("MFTOML017", "[TomlSerializable(typeof((int, string)))] internal partial class Ctx : TomlSerializerContext { }")]
    [InlineData("MFTOML004", "[TomlSerializable(typeof(Root))] internal partial class Ctx : TomlSerializerContext { } public sealed class Root { public System.Collections.Generic.Dictionary<int, string> X { get; set; } = new(); }")]
    [InlineData("MFTOML007", "[TomlSerializable(typeof(Root))] internal partial class Ctx : TomlSerializerContext { } [TomlPolymorphic(TypeDiscriminatorPropertyName = \"\")] [TomlDerivedType(typeof(Derived), \"d\")] public class Root { public int X { get; set; } } public sealed class Derived : Root { }")]
    [InlineData("MFTOML011", "[TomlSerializable(typeof(Root))] internal partial class Ctx : TomlSerializerContext { } public sealed class Root { [TomlPropertyName(\"a\")] public int A { get; set; } [TomlPropertyName(\"a\")] public int B { get; set; } }")]
    [InlineData("MFTOML011", "[TomlSerializable(typeof(Root))] internal partial class Ctx : TomlSerializerContext { } public sealed class Root { public Root(int x) { X = x; } public int X { get; } [TomlObjectCreationHandling(Meziantou.Framework.Toml.TomlObjectCreationHandling.Populate)] public System.Collections.Generic.List<int> Items { get; } = []; }")]
    [InlineData("MFTOML018", "[TomlSerializable(typeof(Root))] internal partial class Ctx : TomlSerializerContext { } public sealed class Root { [TomlIgnore(Condition = (Meziantou.Framework.Toml.TomlIgnoreCondition)42)] public int X { get; set; } }")]
    [InlineData("MFTOML018", "[TomlSerializable(typeof(Root))] internal partial class Ctx : TomlSerializerContext { } public sealed class Root { [TomlStringStyle(Meziantou.Framework.Toml.TomlStringStyle.Basic, AllowHexEscapes = (TomlBooleanPreference)3)] public string X { get; set; } = \"\"; }")]
    [InlineData("MFTOML018", "[TomlSerializable(typeof(Root))] internal partial class Ctx : TomlSerializerContext { } [TomlUnmappedMemberHandling((Meziantou.Framework.Toml.TomlUnmappedMemberHandling)2)] public sealed class Root { public int X { get; set; } }")]
    [InlineData("MFTOML018", "[TomlSerializable(typeof(Root))] internal partial class Ctx : TomlSerializerContext { } [TomlPolymorphic(UnknownDerivedTypeHandling = (Meziantou.Framework.Toml.TomlUnknownDerivedTypeHandling)(-2))] [TomlDerivedType(typeof(Derived), \"d\")] public class Root { public int X { get; set; } } public sealed class Derived : Root { }")]
    public void Generator_UnsupportedContextOrType_ReportsOnlyADiagnostic(string id, string declarations)
    {
        var source = "#nullable enable\nusing Meziantou.Framework.Toml.Serialization;\n" + declarations;

        var diagnostics = RunGenerator(source);

        // The context is not generated, so the compiler also reports its missing members (CS0534), like for MFTOML001
        Assert.Single(diagnostics, d => d.Id == id);
        Assert.All(diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error && d.Id != id), d => Assert.Equal("CS0534", d.Id));
    }

    // Options other than the context's own share the metadata between threads, and the metadata of some types is only
    // resolved when a value is read
    [Fact]
    public void Generator_TypeInfoCacheOfOtherOptions_IsThreadSafe()
    {
        var source = """
            #nullable enable
            using Meziantou.Framework.Toml.Serialization;

            [TomlSerializable(typeof(Holder))]
            internal partial class Ctx : TomlSerializerContext { }

            public class Holder
            {
                [TomlSingleOrArray]
                public System.Collections.Generic.List<string>? Tags { get; set; }
            }
            """;

        var generatedSource = RunGeneratorTest(source).GeneratedSources.Single();

        Assert.Contains("global::System.Collections.Concurrent.ConcurrentDictionary<global::System.Type, global::Meziantou.Framework.Toml.TomlTypeInfo>? _typeInfoCache", generatedSource);
        Assert.DoesNotContain("global::System.Collections.Generic.Dictionary<global::System.Type, global::Meziantou.Framework.Toml.TomlTypeInfo>", generatedSource);
    }

    [Theory]
    [InlineData("public class M { public int A { get; set; } [TomlExtensionData] public System.Collections.Generic.Dictionary<string, object?>? Ext { get; private set; } }")]
    [InlineData("public class M { public int A { get; set; } [TomlExtensionData, TomlInclude] public System.Collections.Generic.Dictionary<string, object?>? Ext { get; private set; } }")]
    [InlineData("public class M { public int A { get; set; } [TomlExtensionData, TomlInclude] public System.Collections.Generic.Dictionary<string, object?>? Ext { get; private init; } }")]
    [InlineData("public class M { [TomlIgnore] public required int F { get; set; } public int A { get; set; } }")]
    [InlineData("public class M { public required int F; public int A { get; set; } }")]
    [InlineData("public class M { public required int F { private get; set; } public int A { get; set; } }")]
    [InlineData("public class M { [TomlIgnore] public required int F { get; set; } public int A { get; init; } }")]
    [InlineData("public class M { public M(System.Collections.Generic.List<int> l, int n) { L = l; N = n; } [TomlSingleOrArray] public System.Collections.Generic.List<int> L { get; } public int N { get; } }")]
    public void Generator_ValidModel_Compiles(string declarations)
    {
        var source = "#nullable enable\nusing Meziantou.Framework.Toml.Serialization;\n[TomlSerializable(typeof(M))] internal partial class Ctx : TomlSerializerContext { }\n" + declarations;

        var diagnostics = RunGenerator(source);

        Assert.DoesNotContain(diagnostics, d => d.Severity == DiagnosticSeverity.Error);
    }

    [Fact]
    public void Generator_ObsoleteAndExperimentalModel_CompilesWithoutWarnings()
    {
        var source = """
            #nullable enable
            using System;
            using System.Diagnostics.CodeAnalysis;
            using Meziantou.Framework.Toml.Serialization;

            [Experimental("MYEXP001")]
            public sealed class ExperimentalType { public int A { get; set; } }

            public sealed class M
            {
                [Obsolete("gone")] public int Old { get; set; }
                [Obsolete("gone", error: true)] public int OldError { get; set; }
            #pragma warning disable MYEXP001
                public ExperimentalType? E { get; set; }
            #pragma warning restore MYEXP001
            }

            public sealed class ObsoleteConstructor
            {
                [Obsolete("x", error: true)] public ObsoleteConstructor() { }
                public int B { get; set; }
            }

            [TomlSerializable(typeof(M))]
            [TomlSerializable(typeof(ObsoleteConstructor))]
            internal partial class Ctx : TomlSerializerContext { }
            """;

        var diagnostics = RunGenerator(source);

        Assert.DoesNotContain(diagnostics, d => d.Severity >= DiagnosticSeverity.Warning);
    }

    [Fact]
    public void Generator_ObsoleteAndExperimentalConverters_Compile()
    {
        var source = """
            #nullable enable
            using System;
            using System.Diagnostics.CodeAnalysis;
            using Meziantou.Framework.Toml;
            using Meziantou.Framework.Toml.Serialization;

            public sealed class ObsoleteConstructorConverter : TomlConverter<int>
            {
                [Obsolete("x", true)] public ObsoleteConstructorConverter() { }
                public override int Read(TomlReader reader) { var value = (int)reader.GetInt64(); reader.Read(); return value; }
                public override void Write(TomlWriter writer, int value) => writer.WriteIntegerValue(value);
            }

            [Experimental("EXPC1")]
            public sealed class ExperimentalConverter : TomlConverter<int>
            {
                public override int Read(TomlReader reader) { var value = (int)reader.GetInt64(); reader.Read(); return value; }
                public override void Write(TomlWriter writer, int value) => writer.WriteIntegerValue(value);
            }

            public sealed class ExperimentalConstructorConverter : TomlConverter<int>
            {
                [Experimental("EXPC2")] public ExperimentalConstructorConverter() { }
                public override int Read(TomlReader reader) { var value = (int)reader.GetInt64(); reader.Read(); return value; }
                public override void Write(TomlWriter writer, int value) => writer.WriteIntegerValue(value);
            }

            [Experimental("EXPC3")]
            public sealed class ExperimentalOptionsConverter : TomlConverter<long>
            {
                public override long Read(TomlReader reader) { var value = reader.GetInt64(); reader.Read(); return value; }
                public override void Write(TomlWriter writer, long value) => writer.WriteIntegerValue(value);
            }

            #pragma warning disable EXPC1, EXPC2, EXPC3
            public sealed class M
            {
                [TomlConverter(typeof(ObsoleteConstructorConverter))] public int A { get; set; }
                [TomlConverter(typeof(ExperimentalConverter))] public int B { get; set; }
                [TomlConverter(typeof(ExperimentalConstructorConverter))] public int C { get; set; }
                public long D { get; set; }
            }

            [TomlSourceGenerationOptions(Converters = [typeof(ExperimentalOptionsConverter)])]
            [TomlSerializable(typeof(M))]
            internal partial class Ctx : TomlSerializerContext { }
            #pragma warning restore EXPC1, EXPC2, EXPC3
            """;

        var diagnostics = RunGenerator(source);

        Assert.DoesNotContain(diagnostics, d => d.Severity >= DiagnosticSeverity.Warning);
    }

    [Fact]
    public void Generator_TypeWithOnlyMembersIgnoredOnRead_CompilesWithoutWarnings()
    {
        var source = """
            #nullable enable
            using Meziantou.Framework.Toml.Serialization;

            public sealed class WriteOnly { [TomlIgnore(Condition = Meziantou.Framework.Toml.TomlIgnoreCondition.WhenReading)] public int X { get; set; } }

            public sealed record WriteOnlyRecord(int A) { [TomlIgnore(Condition = Meziantou.Framework.Toml.TomlIgnoreCondition.WhenReading)] public int X { get; set; } }

            [TomlSerializable(typeof(WriteOnly))]
            [TomlSerializable(typeof(WriteOnlyRecord))]
            internal partial class Ctx : TomlSerializerContext { }
            """;

        var diagnostics = RunGenerator(source);

        Assert.DoesNotContain(diagnostics, d => d.Severity >= DiagnosticSeverity.Warning);
    }

    [Fact]
    public void Generator_ExperimentalTypeArgumentsConvertersAndMemberTypes_Compile()
    {
        var source = """
            #nullable enable
            using System.Diagnostics.CodeAnalysis;
            using Meziantou.Framework.Toml;
            using Meziantou.Framework.Toml.Serialization;

            #pragma warning disable EXPA, EXPB, EXPC
            [Experimental("EXPA")]
            public sealed class Marker { }

            public sealed class Generic<T> { public int A { get; set; } }

            [Experimental("EXPB")]
            public static class Outer
            {
                public sealed class Converter : TomlConverter<int>
                {
                    public override int Read(TomlReader reader) { var value = (int)reader.GetInt64(); reader.Read(); return value; }
                    public override void Write(TomlWriter writer, int value) => writer.WriteIntegerValue(value);
                }
            }

            [Experimental("EXPC")]
            public struct ExperimentalValue { public int X { get; set; } }

            public sealed class ExperimentalValueConverter : TomlConverter<ExperimentalValue>
            {
                public override ExperimentalValue Read(TomlReader reader) { var value = (int)reader.GetInt64(); reader.Read(); return new ExperimentalValue { X = value }; }
                public override void Write(TomlWriter writer, ExperimentalValue value) => writer.WriteIntegerValue(value.X);
            }

            public sealed class Model
            {
                [TomlConverter(typeof(Outer.Converter))] public int X { get; set; }
                [TomlConverter(typeof(ExperimentalValueConverter))] public ExperimentalValue Y { get; set; }
            }

            [TomlSerializable(typeof(Generic<Marker>))]
            [TomlSerializable(typeof(Model))]
            internal partial class Ctx : TomlSerializerContext { }
            #pragma warning restore EXPA, EXPB, EXPC
            """;

        var diagnostics = RunGenerator(source);

        Assert.DoesNotContain(diagnostics, d => d.Severity >= DiagnosticSeverity.Warning);
    }

    [Fact]
    public void Generator_MembersWithAnInaccessibleGetter_Compile()
    {
        var source = """
            #nullable enable
            using System;
            using System.Collections.Generic;
            using Meziantou.Framework.Toml.Serialization;

            public sealed class Child { public int X { get; set; } }

            public sealed class GetOnly
            {
                [TomlInclude] private Child C { get; } = new();
                [TomlInclude] private List<int> L { get; } = [1];
                [TomlInclude, TomlSingleOrArray] private List<int> S { get; } = [1];
                [Obsolete("x", true)] public Child O { get; } = new();
            }

            public sealed class PrivateGetter
            {
                [TomlInclude] public Child? C { private get; set; }
                [TomlInclude, TomlObjectCreationHandling(Meziantou.Framework.Toml.TomlObjectCreationHandling.Populate)] public List<int>? L { private get; set; } = [1];
            }

            public sealed class Generic<T>
            {
                public required T R { get; init; }
                [TomlInclude, TomlSingleOrArray] private List<int> S { get; } = [1];
            }

            public sealed class ObsoleteExtensionData
            {
                [Obsolete("x", true), TomlExtensionData] public Dictionary<string, object?>? Extra { get; set; }
            }

            public sealed class ObsoleteExtensionDataRecord(int A)
            {
                public int A { get; } = A;
                [Obsolete("x", true), TomlExtensionData] public Dictionary<string, object?>? Extra { get; set; }
            }

            [TomlSerializable(typeof(ObsoleteExtensionData))]
            [TomlSerializable(typeof(ObsoleteExtensionDataRecord))]
            [TomlSerializable(typeof(GetOnly))]
            [TomlSerializable(typeof(PrivateGetter))]
            [TomlSerializable(typeof(Generic<int>))]
            internal partial class Ctx : TomlSerializerContext { }
            """;

        var diagnostics = RunGenerator(source);

        Assert.DoesNotContain(diagnostics, d => d.Severity >= DiagnosticSeverity.Warning);
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

    [Theory]
    [InlineData("public", "protected")]
    [InlineData("internal", "private protected")]
    [InlineData("internal", "internal")]
    public void Generator_TypeAsAccessibleAsANestedContext_Compiles(string typeAccessibility, string contextAccessibility)
    {
        var source = $$"""
            #nullable enable
            using Meziantou.Framework.Toml.Serialization;

            {{typeAccessibility}} sealed class Person { public string Name { get; set; } = ""; }

            public partial class Outer
            {
                [TomlSerializable(typeof(Person))]
                {{contextAccessibility}} partial class Ctx : TomlSerializerContext { }
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

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Generator_MemberDiagnostic_IsReportedOnce(bool twoContexts)
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

        if (twoContexts)
        {
            source += "\n[TomlSerializable(typeof(Person))] internal partial class OtherCtx : TomlSerializerContext { }\n";
        }

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
        Add(typeof(TomlSerializerContext).Assembly.Location);

        return references;
    }
}
