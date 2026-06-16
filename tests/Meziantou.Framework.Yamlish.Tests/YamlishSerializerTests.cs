namespace Meziantou.Framework.Yamlish.Tests;

public sealed class YamlishSerializerTests
{
    [Fact]
    public void Options_DefaultValues()
    {
        var options = new YamlishSerializerOptions();

        Assert.False(options.IgnoreReadOnlyFields);
        Assert.False(options.IgnoreReadOnlyProperties);
        Assert.False(options.IncludeFields);
        Assert.Equal(' ', options.IndentCharacter);
        Assert.Equal(2, options.IndentSize);
        Assert.Equal(Environment.NewLine, options.NewLine);
        Assert.Equal(YamlishObjectCreationHandling.Replace, options.PreferredObjectCreationHandling);
        Assert.True(options.AllowDuplicateProperties);
        Assert.True(options.RespectRequiredConstructorParameters);
        Assert.True(options.RespectNullableAnnotations);
    }

    [Fact]
    public void Serialize_UsesCSharpNamesByDefault()
    {
        var result = YamlishSerializer.Serialize(new DefaultNamesProduct { Id = "abc", IsAvailable = true, Price = 12.5m });

        Assert.Equal("""
            Id: abc
            IsAvailable: true
            Price: 12.5
            """, result, ignoreLineEndingDifferences: true);
    }

    [Fact]
    public void Serialize_UsesSnakeCasePolicyAndAttributes()
    {
        var options = new YamlishSerializerOptions { PropertyNamingPolicy = YamlishNamingPolicy.SnakeCaseLower };

        var result = YamlishSerializer.Serialize(new Product { Id = "abc", IsAvailable = true, Ignored = "secret" }, options);

        Assert.Contains("product_id: abc", result, StringComparison.Ordinal);
        Assert.Contains("is_available: true", result, StringComparison.Ordinal);
        Assert.DoesNotContain("Ignored", result, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(nameof(YamlishNamingPolicy.CamelCase))]
    [InlineData(nameof(YamlishNamingPolicy.SnakeCaseLower))]
    [InlineData(nameof(YamlishNamingPolicy.SnakeCaseUpper))]
    [InlineData(nameof(YamlishNamingPolicy.KebabCaseLower))]
    [InlineData(nameof(YamlishNamingPolicy.KebabCaseUpper))]
    public void NamingPolicy_MatchesSystemTextJson(string policyName)
    {
        var yamlishPolicy = GetYamlishNamingPolicy(policyName);
        var jsonPolicy = GetJsonNamingPolicy(policyName);

        foreach (var name in new[] { "IPAddress", "URLValue", "XmlDocument", "lowerCase", "SimpleXMLParser" })
        {
            Assert.Equal(jsonPolicy.ConvertName(name), yamlishPolicy.ConvertName(name));
        }
    }

    [Theory]
    [InlineData("value", "Value")]
    [InlineData("Value", "Value")]
    [InlineData("iPAddress", "IPAddress")]
    public void NamingPolicy_PascalCase(string name, string expected)
    {
        Assert.Equal(expected, YamlishNamingPolicy.PascalCase.ConvertName(name));
    }

    [Fact]
    public void Serialize_UsesKebabCasePolicy()
    {
        var options = new YamlishSerializerOptions { PropertyNamingPolicy = YamlishNamingPolicy.KebabCaseLower };

        var result = YamlishSerializer.Serialize(new DefaultNamesProduct { Id = "abc", IsAvailable = true }, options);

        Assert.Contains("is-available: true", result, StringComparison.Ordinal);
    }

    [Fact]
    public void Options_AddAttribute_OverridesDeclaredAttributes()
    {
        var options = new YamlishSerializerOptions();
        options.AddAttribute(typeof(Product), nameof(Product.Id), new YamlishPropertyNameAttribute("id_from_options"));
        options.AddAttribute(typeof(Product), nameof(Product.Ignored), new YamlishIgnoreAttribute { Condition = YamlishIgnoreCondition.Never });
        options.AddAttribute<Product>(product => product.Price, new YamlishIgnoreAttribute());

        var content = YamlishSerializer.Serialize(new Product { Id = "abc", Ignored = "value" }, options);
        var result = YamlishSerializer.Deserialize<Product>("id_from_options: def\nIgnored: deserialized", options);

        Assert.Contains("id_from_options: abc", content, StringComparison.Ordinal);
        Assert.Contains("Ignored: value", content, StringComparison.Ordinal);
        Assert.DoesNotContain("Price", content, StringComparison.Ordinal);
        Assert.Equal("def", result?.Id);
        Assert.Equal("deserialized", result?.Ignored);
    }

    [Fact]
    public void Options_AreReadOnlyAndMetadataIsCachedAfterFirstUse()
    {
        var predicateEvaluationCount = 0;
        var options = new YamlishSerializerOptions();
        options.AddPropertyAttribute(property =>
        {
            predicateEvaluationCount++;
            return false;
        }, new YamlishIgnoreAttribute());

        YamlishSerializer.Serialize(new DefaultNamesProduct(), options);
        var countAfterFirstUse = predicateEvaluationCount;
        YamlishSerializer.Serialize(new DefaultNamesProduct(), options);
        YamlishSerializer.Deserialize<DefaultNamesProduct>("Id: abc", options);

        Assert.True(options.IsReadOnly);
        Assert.True(countAfterFirstUse > 0);
        Assert.Equal(countAfterFirstUse, predicateEvaluationCount);
        Assert.Throws<InvalidOperationException>(() => options.IncludeFields = true);
        Assert.Throws<InvalidOperationException>(() => options.IndentCharacter = '\t');
        Assert.Throws<InvalidOperationException>(() => options.NewLine = "\n");
        Assert.Throws<InvalidOperationException>(() => options.AllowDuplicateProperties = false);
        Assert.Throws<InvalidOperationException>(() => options.RespectRequiredConstructorParameters = true);
        Assert.Throws<InvalidOperationException>(() => options.RespectNullableAnnotations = true);
        Assert.Throws<InvalidOperationException>(() => options.AddAttribute(typeof(DefaultNamesProduct), new YamlishIgnoreAttribute()));
        Assert.Throws<InvalidOperationException>(() => options.AddAttribute(typeof(DefaultNamesProduct), nameof(DefaultNamesProduct.Id), new YamlishIgnoreAttribute()));
        Assert.Throws<InvalidOperationException>(() => options.AddTypeAttribute(type => type == typeof(DefaultNamesProduct), new YamlishIgnoreAttribute()));
    }

    [Fact]
    public void Serialize_IgnoreReadOnlyProperties()
    {
        var value = new ReadOnlyMembers();

        Assert.Contains("ReadOnlyProperty: property", YamlishSerializer.Serialize(value), StringComparison.Ordinal);
        Assert.DoesNotContain("ReadOnlyProperty", YamlishSerializer.Serialize(value, new YamlishSerializerOptions { IgnoreReadOnlyProperties = true }), StringComparison.Ordinal);
    }

    [Fact]
    public void Serialize_IgnoreReadOnlyFields()
    {
        var value = new ReadOnlyMembers();

        Assert.Contains("ReadOnlyField: field", YamlishSerializer.Serialize(value, new YamlishSerializerOptions { IncludeFields = true }), StringComparison.Ordinal);
        Assert.DoesNotContain("ReadOnlyField", YamlishSerializer.Serialize(value, new YamlishSerializerOptions { IncludeFields = true, IgnoreReadOnlyFields = true }), StringComparison.Ordinal);
    }

    [Fact]
    public void SerializeAndDeserialize_IncludeFields()
    {
        var options = new YamlishSerializerOptions { IncludeFields = true };

        var content = YamlishSerializer.Serialize(new FieldValue { Value = "serialized" }, options);
        var result = YamlishSerializer.Deserialize<FieldValue>("Value: deserialized", options);

        Assert.Equal("Value: serialized", content);
        Assert.Equal("deserialized", result?.Value);
    }

    [Fact]
    public void Serialize_UsesIndentCharacterAndIndentSize()
    {
        var options = new YamlishSerializerOptions { IndentCharacter = '\t', IndentSize = 3, NewLine = "\n" };

        var content = YamlishSerializer.Serialize(new NestedValue { Value = new StringValue { Value = "first\nsecond" } }, options);
        var result = YamlishSerializer.Deserialize<NestedValue>(content, options);

        Assert.Equal("Value:\n\t\t\tValue: |-\n\t\t\t\t\t\tfirst\n\t\t\t\t\t\tsecond", content);
        Assert.Equal("first\nsecond", result?.Value?.Value);
    }

    [Fact]
    public void Serialize_UsesNewLine()
    {
        var options = new YamlishSerializerOptions { NewLine = "\r\n" };

        var content = YamlishSerializer.Serialize(new DefaultNamesProduct { Id = "abc", IsAvailable = true }, options);

        Assert.Equal("Id: abc\r\nIsAvailable: true\r\nPrice: 0", content);
    }

    [Fact]
    public void Deserialize_PreferredObjectCreationHandling_ReplaceByDefault()
    {
        var value = YamlishSerializer.Deserialize<ObjectCreationValue>("""
            Values: [new]
            SettableValues: [new]
            EnumerableValues: [new]
            Lookup:
              existing: 2
              new: 3
            Dimensions:
              Width: 10
            """);

        Assert.NotNull(value);
        Assert.Equal(["initial"], value.Values);
        Assert.Equal(["new"], value.SettableValues);
        Assert.Equal(["new"], value.EnumerableValues);
        Assert.Equal(1, value.Lookup["existing"]);
        Assert.False(value.Lookup.ContainsKey("new"));
        Assert.Equal(1, value.Dimensions.Width);
    }

    [Fact]
    public void Deserialize_PreferredObjectCreationHandling_Populate()
    {
        var options = new YamlishSerializerOptions { PreferredObjectCreationHandling = YamlishObjectCreationHandling.Populate };

        var value = YamlishSerializer.Deserialize<ObjectCreationValue>("""
            Values: [new]
            SettableValues: [new]
            EnumerableValues: [new]
            Lookup:
              existing: 2
              new: 3
            Dimensions:
              Width: 10
            """, options);

        Assert.NotNull(value);
        Assert.Equal(["initial", "new"], value.Values);
        Assert.Equal(["initial", "new"], value.SettableValues);
        Assert.Equal(["new"], value.EnumerableValues);
        Assert.Equal(2, value.Lookup["existing"]);
        Assert.Equal(3, value.Lookup["new"]);
        Assert.Equal(10, value.Dimensions.Width);
    }

    [Fact]
    public void Deserialize_AllowsDuplicatePropertiesByDefault()
    {
        var value = YamlishSerializer.Deserialize<StringValue>("Value: first\nValue: second");

        Assert.Equal("second", value?.Value);
    }

    [Fact]
    public void Deserialize_AllowDuplicatePropertiesFalse_Throws()
    {
        var options = new YamlishSerializerOptions { AllowDuplicateProperties = false };

        Assert.Throws<FormatException>(() => YamlishSerializer.Deserialize<StringValue>("Value: first\nValue: second", options));
    }

    [Fact]
    public void Serialize_DuplicateProperties_Throws()
    {
        Assert.Throws<ArgumentException>(() => YamlishSerializer.Serialize(new DuplicatePropertyNames()));
    }

    [Fact]
    public void Deserialize_ParameterizedConstructor()
    {
        var value = YamlishSerializer.Deserialize<ConstructorValue>("Id: 42\nName: value");

        Assert.NotNull(value);
        Assert.Equal(42, value.Id);
        Assert.Equal("value", value.Name);
    }

    [Fact]
    public void Deserialize_MissingRequiredConstructorParameter_UsesDefaultWhenRequiredParametersAreNotRespected()
    {
        var options = new YamlishSerializerOptions { RespectRequiredConstructorParameters = false };

        var value = YamlishSerializer.Deserialize<ConstructorValue>("Name: value", options);

        Assert.NotNull(value);
        Assert.Equal(0, value.Id);
        Assert.Equal("value", value.Name);
    }

    [Fact]
    public void Deserialize_MissingRequiredConstructorParameter_ThrowsWhenRequiredParametersAreRespected()
    {
        var options = new YamlishSerializerOptions { RespectRequiredConstructorParameters = true };

        var exception = Assert.Throws<FormatException>(() => YamlishSerializer.Deserialize<ConstructorValue>("Name: value", options));

        Assert.Contains("Id", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Deserialize_MissingOptionalConstructorParameter_UsesDeclaredDefault()
    {
        var options = new YamlishSerializerOptions { RespectRequiredConstructorParameters = true };

        var value = YamlishSerializer.Deserialize<OptionalConstructorValue>("Id: 42", options);

        Assert.NotNull(value);
        Assert.Equal(42, value.Id);
        Assert.Equal("default", value.Name);
    }

    [Fact]
    public void Deserialize_ConstructorParameter_UsesSerializedPropertyName()
    {
        var options = new YamlishSerializerOptions
        {
            PropertyNamingPolicy = YamlishNamingPolicy.SnakeCaseLower,
            RespectRequiredConstructorParameters = true,
        };

        var value = YamlishSerializer.Deserialize<ConstructorValue>("id: 42\nName: value", options);

        Assert.NotNull(value);
        Assert.Equal(42, value.Id);
        Assert.Equal("value", value.Name);
    }

    [Fact]
    public void Deserialize_MissingRequiredProperty_Throws()
    {
        var exception = Assert.Throws<FormatException>(() => YamlishSerializer.Deserialize<RequiredPropertyValue>("Other: value"));

        Assert.Contains("value", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Deserialize_RequiredProperty_IsSet()
    {
        var value = YamlishSerializer.Deserialize<RequiredPropertyValue>("value: content");

        Assert.Equal("content", value?.Value);
    }

    [Fact]
    public void Deserialize_MissingRequiredField_ThrowsWhenFieldsAreIncluded()
    {
        var options = new YamlishSerializerOptions { IncludeFields = true };

        Assert.Throws<FormatException>(() => YamlishSerializer.Deserialize<RequiredFieldValue>("Other: value", options));
    }

    [Fact]
    public void Deserialize_RequiredField_IsSetWhenFieldsAreIncluded()
    {
        var options = new YamlishSerializerOptions { IncludeFields = true };

        var value = YamlishSerializer.Deserialize<RequiredFieldValue>("Value: content", options);

        Assert.Equal("content", value?.Value);
    }

    [Fact]
    public void Serialize_RespectNullableAnnotations_ThrowsForNullNonNullableGetter()
    {
        var options = new YamlishSerializerOptions { RespectNullableAnnotations = true };

        Assert.Throws<InvalidOperationException>(() => YamlishSerializer.Serialize(new NullableAnnotationsValue { NonNullable = null! }, options));
    }

    [Fact]
    public void Serialize_RespectNullableAnnotations_AllowsNullNullableGetter()
    {
        var options = new YamlishSerializerOptions { RespectNullableAnnotations = true };

        YamlishSerializer.Serialize(new NullableAnnotationsValue { Nullable = null }, options);
    }

    [Fact]
    public void Deserialize_RespectNullableAnnotations_ThrowsForNullNonNullableSetter()
    {
        var options = CreateNullValueConverterOptions();
        options.RespectNullableAnnotations = true;

        Assert.Throws<FormatException>(() => YamlishSerializer.Deserialize<NullableAnnotationsValue>("NonNullable: null", options));
    }

    [Fact]
    public void Deserialize_RespectNullableAnnotations_AllowsNullNullableSetter()
    {
        var options = CreateNullValueConverterOptions();
        options.RespectNullableAnnotations = true;

        var value = YamlishSerializer.Deserialize<NullableAnnotationsValue>("Nullable: null", options);

        Assert.Null(value?.Nullable);
    }

    [Fact]
    public void Deserialize_RespectNullableAnnotations_ThrowsForNullNonNullableConstructorParameter()
    {
        var options = CreateNullValueConverterOptions();
        options.RespectNullableAnnotations = true;

        Assert.Throws<FormatException>(() => YamlishSerializer.Deserialize<NullableConstructorValue>("Value: null", options));
    }

    [Fact]
    public void Deserialize_RespectNullableAnnotations_ThrowsForMissingNonNullableConstructorParameter()
    {
        var options = new YamlishSerializerOptions { RespectNullableAnnotations = true };

        Assert.Throws<FormatException>(() => YamlishSerializer.Deserialize<ConstructorValue>("Id: 42", options));
    }

    [Fact]
    public void RespectNullableAnnotations_HonorsCodeAnalysisAttributes()
    {
        var options = CreateNullValueConverterOptions();
        options.RespectNullableAnnotations = true;

        YamlishSerializer.Serialize(new CodeAnalysisNullableAnnotationsValue(), options);
        var value = YamlishSerializer.Deserialize<CodeAnalysisNullableAnnotationsValue>("Setter: null", options);

        Assert.Null(value?.Setter);
    }

    [Fact]
    public void Deserialize_RespectNullableAnnotationsFalse_AllowsNullNonNullableSetter()
    {
        var options = CreateNullValueConverterOptions();
        options.RespectNullableAnnotations = false;

        var value = YamlishSerializer.Deserialize<NullableAnnotationsValue>("NonNullable: null", options);

        Assert.Null(value?.NonNullable);
    }

    [Fact]
    public void Serialize_DefaultIgnoreCondition_WhenWritingDefault()
    {
        var options = new YamlishSerializerOptions { DefaultIgnoreCondition = YamlishIgnoreCondition.WhenWritingDefault };

        var result = YamlishSerializer.Serialize(new IgnoreConditions(), options);

        Assert.Equal("Never: 0", result);
    }

    [Fact]
    public void Serialize_IgnoreAttributeConditions()
    {
        var value = new IgnoreConditions
        {
            Always = "value",
            WhenWritingDefault = 1,
            WhenWritingNull = "value",
        };

        var result = YamlishSerializer.Serialize(value);

        Assert.Equal("""
            Never: 0
            WhenWritingDefault: 1
            WhenWritingNull: value
            """, result, ignoreLineEndingDifferences: true);
    }

    [Fact]
    public void Serialize_IgnoreAttributeConditions_Fields()
    {
        var options = new YamlishSerializerOptions { IncludeFields = true };

        var result = YamlishSerializer.Serialize(new IgnoreConditionFields
        {
            Never = 0,
            Always = "value",
            WhenWritingDefault = 0,
            WhenWritingNull = null,
        }, options);

        Assert.Equal("Never: 0", result);
    }

    [Fact]
    public void Options_AddFieldAttribute_OverridesDeclaredAttribute()
    {
        var options = new YamlishSerializerOptions { IncludeFields = true };
        options.AddFieldAttribute(field => field.Name == nameof(IgnoreConditionFields.Always), new YamlishIgnoreAttribute { Condition = YamlishIgnoreCondition.Never });

        var result = YamlishSerializer.Serialize(new IgnoreConditionFields { Always = "value" }, options);

        Assert.Equal("""
            Never: 0
            Always: value
            """, result, ignoreLineEndingDifferences: true);
    }

    [Fact]
    public void Serialize_DefaultIgnoreCondition_Never_NullThrows()
    {
        var options = new YamlishSerializerOptions { DefaultIgnoreCondition = YamlishIgnoreCondition.Never };

        Assert.Throws<InvalidOperationException>(() => YamlishSerializer.Serialize(new StringValue(), options));
    }

    [Fact]
    public void Deserialize_AlwaysIgnoreConditionIsIgnored()
    {
        var result = YamlishSerializer.Deserialize<IgnoreConditions>("""
            Never: 1
            Always: value
            WhenWritingDefault: 2
            WhenWritingNull: value
            """);

        Assert.NotNull(result);
        Assert.Equal(1, result.Never);
        Assert.Null(result.Always);
        Assert.Equal(2, result.WhenWritingDefault);
        Assert.Equal("value", result.WhenWritingNull);
    }

    [Fact]
    public void Deserialize_ConvertsScalarsAndNestedValues()
    {
        var result = YamlishSerializer.Deserialize<Product>("""
            product_id: abc
            IsAvailable: true
            Price: 12.5
            Tags: [new, sale]
            Dimensions:
              Width: 10
              Height: 20
            """);

        Assert.NotNull(result);
        Assert.Equal("abc", result.Id);
        Assert.True(result.IsAvailable);
        Assert.Equal(12.5m, result.Price);
        Assert.Equal(["new", "sale"], result.Tags);
        Assert.Equal(10, result.Dimensions?.Width);
        Assert.Equal(20, result.Dimensions?.Height);
    }

    [Fact]
    public void Deserialize_JaggedArray_BlockSequence()
    {
        var result = YamlishSerializer.Deserialize<string[][]>("""
            -
              - item1.1
              - item1.2
            -
              - item2.1
              - item2.2
            """);

        Assert.Equal(
            [
                ["item1.1", "item1.2"],
                ["item2.1", "item2.2"],
            ], result);
    }

    [Fact]
    public void Serialize_UsesSequenceStyleAttributes()
    {
        var content = YamlishSerializer.Serialize(new SequenceStyleValue());

        Assert.Equal("""
            Block:
              - item1
              - item2
            Flow: [item1, item2]
            """, content, ignoreLineEndingDifferences: true);
    }

    [Fact]
    public void Serialize_UsesScalarStyleAttributes()
    {
        var content = YamlishSerializer.Serialize(new ScalarStyleValue());

        Assert.Equal("""
            Plain: item1,item2
            DoubleQuoted: "item1"
            SingleQuoted: 'it''s literal'
            Literal: |-
              first
              second
            Folded: >
              first
              second
            """, content, ignoreLineEndingDifferences: true);
    }

    [Fact]
    public void Serialize_SequenceStyleAttribute_CanBeConfiguredWithOptions()
    {
        var options = new YamlishSerializerOptions();
        options.AddAttribute(typeof(OptionsSequenceStyleValue), nameof(OptionsSequenceStyleValue.Values), new YamlishSequenceStyleAttribute(YamlishSequenceStyle.Block));

        var content = YamlishSerializer.Serialize(new OptionsSequenceStyleValue(), options);

        Assert.Equal("""
            Values:
              - item1
              - item2
            """, content, ignoreLineEndingDifferences: true);
    }

    [Fact]
    public void Serialize_ScalarStyleAttribute_CanBeConfiguredWithOptions()
    {
        var options = new YamlishSerializerOptions();
        options.AddAttribute(typeof(OptionsScalarStyleValue), nameof(OptionsScalarStyleValue.Value), new YamlishScalarStyleAttribute(YamlishScalarStyle.SingleQuoted));

        var content = YamlishSerializer.Serialize(new OptionsScalarStyleValue(), options);

        Assert.Equal("Value: 'it''s literal'", content);
    }

    [Fact]
    public void Serialize_JaggedArray_CanUseBlockSequenceStyle()
    {
        var value = new JaggedArrayValue
        {
            Items =
            [
                ["item1.1", "item1.2"],
                ["item2.1", "item2.2"],
            ],
        };

        var content = YamlishSerializer.Serialize(value);

        Assert.Equal("""
            Items:
              -
                - item1.1
                - item1.2
              -
                - item2.1
                - item2.2
            """, content, ignoreLineEndingDifferences: true);
    }

    [Theory]
    [InlineData("""
        Name: Test
        Url: https://www.meziantou.net
        Taxonomies:
        - Name: Categories
          Terms:
          - Name: ".NET"
            ChildrenNames: ["ASP.NET Core"]
          - Name: "ASP.NET Core"
            ChildrenNames: ["Blazor"]
        """)]
    [InlineData("""
        Name: Test
        Url: https://www.meziantou.net
        Taxonomies:
        - Name: Categories
          Terms:
            - Name: ".NET"
              ChildrenNames: ["ASP.NET Core"]
            - Name: "ASP.NET Core"
              ChildrenNames: ["Blazor"]
        """)]
    public void Deserialize_ListOfObjects_WithNestedListOfObjects(string content)
    {
        var result = YamlishSerializer.Deserialize<WebsiteMetadata>(content);

        Assert.NotNull(result);
        Assert.Equal("Test", result.Name);
        Assert.Equal("https://www.meziantou.net", result.Url);
        var taxonomy = Assert.Single(result.Taxonomies);
        Assert.Equal("Categories", taxonomy.Name);
        Assert.Collection(
            taxonomy.Terms,
            term =>
            {
                Assert.Equal(".NET", term.Name);
                Assert.Equal(["ASP.NET Core"], term.ChildrenNames);
            },
            term =>
            {
                Assert.Equal("ASP.NET Core", term.Name);
                Assert.Equal(["Blazor"], term.ChildrenNames);
            });
    }

    [Fact]
    public void SerializeAndDeserialize_Dictionary()
    {
        var value = new Dictionary<string, int>(StringComparer.Ordinal)
        {
            ["one"] = 1,
            ["two"] = 2,
        };

        var content = YamlishSerializer.Serialize(value);
        var result = YamlishSerializer.Deserialize<Dictionary<string, int>>(content);

        Assert.Equal(value, result);
    }

    [Theory]
    [InlineData("")]
    [InlineData("plain value")]
    [InlineData(" leading and trailing ")]
    [InlineData("quote: \"; slash: \\; tab: \t")]
    [InlineData("first\nsecond")]
    [InlineData("first\n")]
    public void SerializeAndDeserialize_String_RoundTripsWithoutDocumentTrailingNewLine(string value)
    {
        var content = YamlishSerializer.Serialize(new StringValue { Value = value });
        var result = YamlishSerializer.Deserialize<StringValue>(content);

        Assert.False(content.EndsWith('\n', StringComparison.Ordinal));
        Assert.Equal(value, result?.Value);
    }

    [Theory]
    [InlineData("Value: plain value", "plain value")]
    [InlineData("Value: \" leading and trailing \"", " leading and trailing ")]
    [InlineData("Value: 'it''s literal'", "it's literal")]
    [InlineData("Value: |-\n  first\n  second\n", "first\nsecond")]
    [InlineData("Value: |\n  first\n  second\n", "first\nsecond\n")]
    [InlineData("Value: |+\n  first\n  second\n\n", "first\nsecond\n\n")]
    [InlineData("Value: >-\n  first\n  second\n", "first second")]
    [InlineData("Value: >\n  first\n  second\n", "first second\n")]
    [InlineData("Value: >+\n  first\n  second\n\n", "first second\n\n")]
    public void Deserialize_StringValues(string content, string expected)
    {
        var result = YamlishSerializer.Deserialize<StringValue>(content);

        Assert.Equal(expected, result?.Value);
    }

    [Fact]
    public void Serialize_PolymorphicType_AddsDefaultTypeDiscriminator()
    {
        PolymorphicBase value = new PolymorphicDerived { BaseValue = 1, DerivedValue = "derived" };

        var content = YamlishSerializer.Serialize<PolymorphicBase>(value);

        Assert.Equal("""
            $type: derived
            DerivedValue: derived
            BaseValue: 1
            """, content, ignoreLineEndingDifferences: true);
    }

    [Fact]
    public void Deserialize_PolymorphicType_CreatesDerivedType()
    {
        var result = YamlishSerializer.Deserialize<PolymorphicBase>("""
            $type: derived
            DerivedValue: derived
            BaseValue: 1
            """);

        var derived = Assert.IsType<PolymorphicDerived>(result);
        Assert.Equal(1, derived.BaseValue);
        Assert.Equal("derived", derived.DerivedValue);
    }

    [Fact]
    public void Serialize_PolymorphicType_UsesDerivedTypeNameByDefault()
    {
        DefaultPolymorphicBase value = new DefaultPolymorphicDerived { Value = "derived" };

        var content = YamlishSerializer.Serialize<DefaultPolymorphicBase>(value);

        Assert.Equal("""
            $type: DefaultPolymorphicDerived
            Value: derived
            """, content, ignoreLineEndingDifferences: true);
    }

    [Fact]
    public void Serialize_PolymorphicType_CanAddDiscriminatorForBaseType()
    {
        PolymorphicBaseAndDerived value = new() { BaseValue = 1 };

        var content = YamlishSerializer.Serialize<PolymorphicBaseAndDerived>(value);

        Assert.Equal("""
            $type: base
            BaseValue: 1
            """, content, ignoreLineEndingDifferences: true );
    }

    [Fact]
    public void SerializeAndDeserialize_PolymorphicType_UsesCustomTypeDiscriminatorPropertyName()
    {
        CustomPolymorphicBase value = new CustomPolymorphicDerived { Value = "derived" };

        var content = YamlishSerializer.Serialize<CustomPolymorphicBase>(value);
        var result = YamlishSerializer.Deserialize<CustomPolymorphicBase>(content);

        Assert.Equal("""
            $kind: custom
            Value: derived
            """, content, ignoreLineEndingDifferences: true);
        Assert.IsType<CustomPolymorphicDerived>(result);
    }

    [Fact]
    public void SerializeAndDeserialize_PolymorphicType_UsesOptionsAttributes()
    {
        var options = new YamlishSerializerOptions();
        options.AddAttribute(typeof(OptionsPolymorphicBase), new YamlishDerivedTypeAttribute(typeof(OptionsPolymorphicDerived), "options"));
        OptionsPolymorphicBase value = new OptionsPolymorphicDerived { Value = "derived" };

        var content = YamlishSerializer.Serialize<OptionsPolymorphicBase>(value, options);
        var result = YamlishSerializer.Deserialize<OptionsPolymorphicBase>(content, options);

        Assert.Equal("""
            $type: options
            Value: derived
            """, content, ignoreLineEndingDifferences: true);
        Assert.IsType<OptionsPolymorphicDerived>(result);
    }

    [Fact]
    public void Serialize_PolymorphicType_UnknownDerivedTypeThrows()
    {
        PolymorphicBase value = new UnknownPolymorphicDerived { BaseValue = 1 };

        Assert.Throws<NotSupportedException>(() => YamlishSerializer.Serialize<PolymorphicBase>(value));
    }

    [Fact]
    public void Deserialize_PolymorphicType_UnknownTypeDiscriminatorThrows()
    {
        var exception = Assert.Throws<FormatException>(() => YamlishSerializer.Deserialize<PolymorphicBase>("""
            $type: unknown
            BaseValue: 1
            """));

        Assert.Contains("unknown", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Deserialize_PolymorphicType_UsesPropertyNameComparerForTypeDiscriminatorPropertyName()
    {
        var result = YamlishSerializer.Deserialize<PolymorphicBase>("""
            $TYPE: derived
            DerivedValue: derived
            BaseValue: 1
            """);

        Assert.IsType<PolymorphicDerived>(result);
    }

    [Fact]
    public void SerializeAndDeserialize_PolymorphicType_InObjectProperty()
    {
        var value = new PolymorphicContainer { Value = new PolymorphicDerived { BaseValue = 1, DerivedValue = "derived" } };

        var content = YamlishSerializer.Serialize(value);
        var result = YamlishSerializer.Deserialize<PolymorphicContainer>(content);

        Assert.Equal("""
            Value:
              $type: derived
              DerivedValue: derived
              BaseValue: 1
            """, content, ignoreLineEndingDifferences: true);
        Assert.IsType<PolymorphicDerived>(result?.Value);
    }

    [Fact]
    public void SerializeAndDeserialize_PolymorphicType_InCollection()
    {
        var value = new List<PolymorphicBase>
        {
            new PolymorphicDerived { BaseValue = 1, DerivedValue = "derived" },
        };

        var content = YamlishSerializer.Serialize(value);
        var result = YamlishSerializer.Deserialize<List<PolymorphicBase>>(content);

        Assert.Equal("""
            -
              $type: derived
              DerivedValue: derived
              BaseValue: 1
            """, content, ignoreLineEndingDifferences: true);
        Assert.NotNull(result);
        var derived = Assert.IsType<PolymorphicDerived>(Assert.Single(result));
        Assert.Equal("derived", derived.DerivedValue);
    }

    [Fact]
    public void SerializeAndDeserialize_PolymorphicType_InDictionary()
    {
        var value = new Dictionary<string, PolymorphicBase>(StringComparer.Ordinal)
        {
            ["value"] = new PolymorphicDerived { BaseValue = 1, DerivedValue = "derived" },
        };

        var content = YamlishSerializer.Serialize(value);
        var result = YamlishSerializer.Deserialize<Dictionary<string, PolymorphicBase>>(content);

        Assert.Equal("""
            value:
              $type: derived
              DerivedValue: derived
              BaseValue: 1
            """, content, ignoreLineEndingDifferences: true);
        var derived = Assert.IsType<PolymorphicDerived>(result?["value"]);
        Assert.Equal("derived", derived.DerivedValue);
    }

    [Fact]
    public void Serialize_PolymorphicType_DuplicateTypeDiscriminatorThrows()
    {
        var options = new YamlishSerializerOptions();
        options.AddAttribute(typeof(OptionsPolymorphicBase), new YamlishDerivedTypeAttribute(typeof(OptionsPolymorphicDerived), "duplicate"));
        options.AddAttribute(typeof(OptionsPolymorphicBase), new YamlishDerivedTypeAttribute(typeof(SecondOptionsPolymorphicDerived), "duplicate"));

        Assert.Throws<InvalidOperationException>(() => YamlishSerializer.Serialize<OptionsPolymorphicBase>(new OptionsPolymorphicDerived(), options));
    }

    [Fact]
    public void Serialize_PolymorphicType_NonAssignableDerivedTypeThrows()
    {
        var options = new YamlishSerializerOptions();
        options.AddAttribute(typeof(OptionsPolymorphicBase), new YamlishDerivedTypeAttribute(typeof(StringValue), "invalid"));

        Assert.Throws<InvalidOperationException>(() => YamlishSerializer.Serialize<OptionsPolymorphicBase>(new OptionsPolymorphicBase(), options));
    }

    [Fact]
    public void Serialize_PolymorphicType_DiscriminatorPropertyCollisionThrows()
    {
        PolymorphicCollisionBase value = new PolymorphicCollisionDerived { Type = "value" };

        Assert.Throws<ArgumentException>(() => YamlishSerializer.Serialize<PolymorphicCollisionBase>(value));
    }

    [Fact]
    public void Deserialize_PolymorphicType_RespectsDerivedRequiredConstructorParameters()
    {
        Assert.Throws<FormatException>(() => YamlishSerializer.Deserialize<PolymorphicConstructorBase>("""
            $type: constructor
            """));
    }

    [Fact]
    public void Deserialize_PolymorphicType_RespectsDerivedNullableAnnotations()
    {
        var options = CreateNullValueConverterOptions();

        Assert.Throws<FormatException>(() => YamlishSerializer.Deserialize<PolymorphicNullableBase>("""
            $type: nullable
            Value: null
            """, options));
    }

    private sealed class Product
    {
        [YamlishPropertyName("product_id")]
        public string? Id { get; set; }

        public bool IsAvailable { get; set; }

        public decimal Price { get; set; }

        public List<string>? Tags { get; set; }

        public Dimensions? Dimensions { get; set; }

        [YamlishIgnore]
        public string? Ignored { get; set; }
    }

    private sealed class DefaultNamesProduct
    {
        public string? Id { get; set; }

        public bool IsAvailable { get; set; }

        public decimal Price { get; set; }
    }

    private sealed class Dimensions
    {
        public int Width { get; set; }

        public int Height { get; set; }
    }

    private sealed class StringValue
    {
        public string? Value { get; set; }
    }

    private sealed class SequenceStyleValue
    {
        [YamlishSequenceStyle(YamlishSequenceStyle.Block)]
        public string[] Block { get; set; } = ["item1", "item2"];

        [YamlishSequenceStyle(YamlishSequenceStyle.Flow)]
        public string[] Flow { get; set; } = ["item1", "item2"];
    }

    private sealed class ScalarStyleValue
    {
        [YamlishScalarStyle(YamlishScalarStyle.Plain)]
        public string Plain { get; set; } = "item1,item2";

        [YamlishScalarStyle(YamlishScalarStyle.DoubleQuoted)]
        public string DoubleQuoted { get; set; } = "item1";

        [YamlishScalarStyle(YamlishScalarStyle.SingleQuoted)]
        public string SingleQuoted { get; set; } = "it's literal";

        [YamlishScalarStyle(YamlishScalarStyle.Literal, Chomping = YamlishScalarChomping.Strip)]
        public string Literal { get; set; } = "first\nsecond";

        [YamlishScalarStyle(YamlishScalarStyle.Folded)]
        public string Folded { get; set; } = "first\nsecond\n";
    }

    private sealed class OptionsSequenceStyleValue
    {
        public string[] Values { get; set; } = ["item1", "item2"];
    }

    private sealed class OptionsScalarStyleValue
    {
        public string Value { get; set; } = "it's literal";
    }

    private sealed class JaggedArrayValue
    {
        [YamlishSequenceStyle(YamlishSequenceStyle.Block)]
        public string[][]? Items { get; set; }
    }

    private sealed class WebsiteMetadata
    {
        public string? Name { get; set; }

        public string? Url { get; set; }

        public List<Taxonomy> Taxonomies { get; set; } = [];
    }

    private sealed class Taxonomy
    {
        public string? Name { get; set; }

        public List<Term> Terms { get; set; } = [];
    }

    private sealed class Term
    {
        public string? Name { get; set; }

        public List<string> ChildrenNames { get; set; } = [];
    }

    [YamlishDerivedType(typeof(PolymorphicDerived), "derived")]
    private class PolymorphicBase
    {
        public int BaseValue { get; set; }
    }

    private sealed class PolymorphicDerived : PolymorphicBase
    {
        public string? DerivedValue { get; set; }
    }

    private sealed class UnknownPolymorphicDerived : PolymorphicBase;

    [YamlishDerivedType(typeof(DefaultPolymorphicDerived))]
    private class DefaultPolymorphicBase;

    private sealed class DefaultPolymorphicDerived : DefaultPolymorphicBase
    {
        public string? Value { get; set; }
    }

    [YamlishDerivedType(typeof(PolymorphicBaseAndDerived), "base")]
    private sealed class PolymorphicBaseAndDerived
    {
        public int BaseValue { get; set; }
    }

    [YamlishPolymorphic(TypeDiscriminatorPropertyName = "$kind")]
    [YamlishDerivedType(typeof(CustomPolymorphicDerived), "custom")]
    private class CustomPolymorphicBase;

    private sealed class CustomPolymorphicDerived : CustomPolymorphicBase
    {
        public string? Value { get; set; }
    }

    private class OptionsPolymorphicBase;

    private sealed class OptionsPolymorphicDerived : OptionsPolymorphicBase
    {
        public string? Value { get; set; }
    }

    private sealed class SecondOptionsPolymorphicDerived : OptionsPolymorphicBase;

    private sealed class PolymorphicContainer
    {
        public PolymorphicBase? Value { get; set; }
    }

    [YamlishDerivedType(typeof(PolymorphicCollisionDerived), "collision")]
    private class PolymorphicCollisionBase;

    private sealed class PolymorphicCollisionDerived : PolymorphicCollisionBase
    {
        [YamlishPropertyName("$type")]
        public string? Type { get; set; }
    }

    [YamlishDerivedType(typeof(PolymorphicConstructorDerived), "constructor")]
    private class PolymorphicConstructorBase;

    private sealed class PolymorphicConstructorDerived(string name) : PolymorphicConstructorBase
    {
        public string Name { get; } = name;
    }

    [YamlishDerivedType(typeof(PolymorphicNullableDerived), "nullable")]
    private class PolymorphicNullableBase;

    private sealed class PolymorphicNullableDerived : PolymorphicNullableBase
    {
        public NullValue Value { get; set; } = new();
    }

    private static YamlishNamingPolicy GetYamlishNamingPolicy(string policyName)
    {
        return policyName switch
        {
            nameof(YamlishNamingPolicy.CamelCase) => YamlishNamingPolicy.CamelCase,
            nameof(YamlishNamingPolicy.SnakeCaseLower) => YamlishNamingPolicy.SnakeCaseLower,
            nameof(YamlishNamingPolicy.SnakeCaseUpper) => YamlishNamingPolicy.SnakeCaseUpper,
            nameof(YamlishNamingPolicy.KebabCaseLower) => YamlishNamingPolicy.KebabCaseLower,
            nameof(YamlishNamingPolicy.KebabCaseUpper) => YamlishNamingPolicy.KebabCaseUpper,
            _ => throw new ArgumentOutOfRangeException(nameof(policyName)),
        };
    }

    private static System.Text.Json.JsonNamingPolicy GetJsonNamingPolicy(string policyName)
    {
        return policyName switch
        {
            nameof(YamlishNamingPolicy.CamelCase) => System.Text.Json.JsonNamingPolicy.CamelCase,
            nameof(YamlishNamingPolicy.SnakeCaseLower) => System.Text.Json.JsonNamingPolicy.SnakeCaseLower,
            nameof(YamlishNamingPolicy.SnakeCaseUpper) => System.Text.Json.JsonNamingPolicy.SnakeCaseUpper,
            nameof(YamlishNamingPolicy.KebabCaseLower) => System.Text.Json.JsonNamingPolicy.KebabCaseLower,
            nameof(YamlishNamingPolicy.KebabCaseUpper) => System.Text.Json.JsonNamingPolicy.KebabCaseUpper,
            _ => throw new ArgumentOutOfRangeException(nameof(policyName)),
        };
    }

    private sealed class NestedValue
    {
        public StringValue? Value { get; set; }
    }

    private sealed class ObjectCreationValue
    {
        public List<string> Values { get; } = ["initial"];

        public List<string> SettableValues { get; set; } = ["initial"];

        public IEnumerable<string> EnumerableValues { get; set; } = ["initial"];

        public Dictionary<string, int> Lookup { get; } = new(StringComparer.Ordinal)
        {
            ["existing"] = 1,
        };

        public Dimensions Dimensions { get; } = new() { Width = 1 };
    }

    private sealed class DuplicatePropertyNames
    {
        [YamlishPropertyName("Value")]
        public string First { get; set; } = "first";

        [YamlishPropertyName("Value")]
        public string Second { get; set; } = "second";
    }

    private sealed class ConstructorValue(int id, string name)
    {
        public int Id { get; } = id;

        [YamlishPropertyName("Name")]
        public string Name { get; } = name;
    }

    private sealed class OptionalConstructorValue(int id, string name = "default")
    {
        public int Id { get; } = id;

        public string Name { get; } = name;
    }

    private sealed class RequiredPropertyValue
    {
        [YamlishPropertyName("value")]
        public required string Value { get; set; }
    }

#pragma warning disable CA1051
    private sealed class RequiredFieldValue
    {
        public required string Value = null!;
    }
#pragma warning restore CA1051

    private sealed class NullableAnnotationsValue
    {
        public NullValue NonNullable { get; set; } = new();

        public NullValue? Nullable { get; set; }
    }

    private sealed class NullableConstructorValue(NullValue value)
    {
        public NullValue Value { get; } = value;
    }

    private sealed class CodeAnalysisNullableAnnotationsValue
    {
        [System.Diagnostics.CodeAnalysis.MaybeNull]
        public NullValue Getter { get; } = null!;

        [System.Diagnostics.CodeAnalysis.AllowNull]
        public NullValue Setter { get; set; } = new();
    }

    private sealed class NullValue;

    private sealed class NullValueConverter : YamlishConverter<NullValue>
    {
        public override NullValue? Read(YamlishNode node, YamlishSerializerOptions options) => null;

        public override YamlishNode Write(NullValue value, YamlishSerializerOptions options) => new YamlishScalar("value");
    }

    private static YamlishSerializerOptions CreateNullValueConverterOptions()
    {
        var options = new YamlishSerializerOptions();
        options.Converters.Insert(0, new NullValueConverter());
        return options;
    }

    private sealed class IgnoreConditions
    {
        [YamlishIgnore(Condition = YamlishIgnoreCondition.Never)]
        public int Never { get; set; }

        [YamlishIgnore]
        public string? Always { get; set; }

        [YamlishIgnore(Condition = YamlishIgnoreCondition.WhenWritingDefault)]
        public int WhenWritingDefault { get; set; }

        [YamlishIgnore(Condition = YamlishIgnoreCondition.WhenWritingNull)]
        public string? WhenWritingNull { get; set; }
    }

#pragma warning disable CA1051
    private sealed class FieldValue
    {
        public string? Value;
    }

    private sealed class ReadOnlyMembers
    {
        public readonly string ReadOnlyField = "field";

        public string ReadOnlyProperty { get; } = "property";
    }

    private sealed class IgnoreConditionFields
    {
        [YamlishIgnore(Condition = YamlishIgnoreCondition.Never)]
        public int Never;

        [YamlishIgnore]
        public string? Always;

        [YamlishIgnore(Condition = YamlishIgnoreCondition.WhenWritingDefault)]
        public int WhenWritingDefault;

        [YamlishIgnore(Condition = YamlishIgnoreCondition.WhenWritingNull)]
        public string? WhenWritingNull;
    }
#pragma warning restore CA1051
}
