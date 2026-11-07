using Meziantou.Framework.Yaml.Events;
using Meziantou.Framework.Yaml.Model;
using YamlStream = Meziantou.Framework.Yaml.Model.YamlStream;

namespace Meziantou.Framework.Yaml.Tests;

public class YamlNodeTest
{
    [Fact]
    public void ReadmeExample_Load()
    {
        #region YamlStream_Load
        var stream = YamlStream.Load(new StringReader("""
            product:
              id: 1
              name: Sample product
            """));

        var document = stream[0];
        var root = (YamlMapping)document.Contents!;
        var product = (YamlMapping)root["product"]!;
        var name = ((YamlValue)product["name"]!).Value;
        Assert.Equal("Sample product", name);
        #endregion
    }

    [Fact]
    public void ReadmeExample_LoadWithOptions()
    {
        #region YamlStream_LoadWithOptions
        var options = new YamlSerializerOptions { MaxAliasExpansionNodeCount = 1_000 };

        using var reader = new StringReader("""
            base: &base { name: default }
            copy: *base
            """);

        var stream = YamlStream.Load(reader, options);
        #endregion

        Assert.HasCount(1, stream);
    }

    [Fact]
    public void Documentation_MultipleDocuments()
    {
        #region YamlStream_MultipleDocuments
        var stream = YamlStream.Load(new StringReader("""
            name: first
            ---
            name: second
            """));

        var names = new List<string>();
        foreach (var document in stream)
        {
            var root = (YamlMapping)document.Contents!;
            names.Add(((YamlValue)root["name"]!).Value);
        }

        Assert.Equal(["first", "second"], names);
        #endregion
    }

    [Fact]
    public void Documentation_Navigate()
    {
        #region YamlMapping_Navigate
        var stream = YamlStream.Load(new StringReader("""
            services:
              web:
                image: nginx
                ports: [80, 443]
              cache:
                image: redis
            """));

        var services = (YamlMapping)((YamlMapping)stream[0].Contents!)["services"]!;

        var images = new List<string>();
        foreach (var (name, service) in services)
        {
            var image = ((YamlMapping)service!)["image"] as YamlValue;
            images.Add($"{((YamlValue)name).Value}={image?.Value}");
        }

        Assert.Equal(["web=nginx", "cache=redis"], images);

        var web = (YamlMapping)services["web"]!;
        Assert.True(web.TryGetValue("ports", out var ports));
        Assert.Equal(YamlStyle.Flow, ((YamlSequence)ports).Style);
        Assert.Equal(["80", "443"], ((YamlSequence)ports).Select(port => ((YamlValue)port).Value));
        #endregion
    }

    [Fact]
    public void Documentation_Edit()
    {
        #region YamlMapping_Edit
        var stream = YamlStream.Load(new StringReader("""
            name: api
            replicas: 2
            ports:
              - 80
            """));

        var root = (YamlMapping)stream[0].Contents!;
        root["replicas"] = new YamlValue(3);
        ((YamlSequence)root["ports"]!).Add(new YamlValue(443));
        root.Remove("name");
        root["image"] = new YamlValue("nginx");

        using var writer = new StringWriter { NewLine = "\n" };
        stream.WriteTo(writer);
        Assert.Equal("""
            replicas: 3
            ports:
              - 80
              - 443
            image: nginx

            """, writer.ToString());
        #endregion
    }

    [Fact]
    public void Documentation_Create()
    {
        #region YamlMapping_Create
        var ports = new YamlSequence { new YamlValue(80), new YamlValue(443) };
        ports.Style = YamlStyle.Flow;

        var root = new YamlMapping
        {
            { new YamlValue("name"), new YamlValue("api") },
            { new YamlValue("ports"), ports },
            { new YamlValue("debug"), new YamlValue(false) },
        };

        var stream = new YamlStream { new YamlDocument { Contents = root } };

        using var writer = new StringWriter { NewLine = "\n" };
        stream.WriteTo(writer);
        Assert.Equal("""
            name: api
            ports: [80, 443]
            debug: false

            """, writer.ToString());
        #endregion
    }

    [Fact]
    public void ReadYamlReference()
    {
        using var file = Utils.GetResourceStream("YamlReferenceCard.yaml");

        using var fileStream = new StreamReader(file);
        var stream = YamlStream.Load(fileStream);

        var collectionIndicators = (YamlMapping)((YamlMapping)stream[0].Contents!)["Collection indicators"]!;

        var firstCollectionIndicator = collectionIndicators.Keys.First();

        Assert.Equal("? ", firstCollectionIndicator.ToObject<string>());

        var firstCollectionIndicatorValue = collectionIndicators[firstCollectionIndicator];

        collectionIndicators[0] = new KeyValuePair<YamlElement, YamlElement?>(
            new YamlValue(":-)"),
            firstCollectionIndicatorValue
        );

        var serialized = new StringBuilder();

        using var writer = new StringWriter(serialized);
        stream.WriteTo(writer);

        stream = YamlStream.Load(new StringReader(serialized.ToString()));

        collectionIndicators = (YamlMapping)((YamlMapping)stream[0].Contents!)["Collection indicators"]!;

        firstCollectionIndicator = collectionIndicators.Keys.First();

        Assert.Equal(":-)", firstCollectionIndicator.ToObject<string>());
    }

    [Fact]
    public void YamlValue()
    {
        using var file = Utils.GetResourceStream("test6.yaml");

        using var fileStream = new StreamReader(file);
        var stream = YamlStream.Load(fileStream);

        var value = (YamlValue)stream[0].Contents!;

        Assert.Equal(3.14f, value.ToObject<float>());

        stream[0].Contents = new YamlValue(double.PositiveInfinity);

        var serialized = new StringBuilder();

        using var writer = new StringWriter(serialized);
        stream.WriteTo(writer);

        stream = YamlStream.Load(new StringReader(serialized.ToString()));

        value = (YamlValue)stream[0].Contents!;

        Assert.Equal(float.PositiveInfinity, value.ToObject<float>());
    }

    [Fact]
    public void YamlValue_FloatingPointSpecialValues_UseYamlSpellings()
    {
        Assert.Equal(".inf", new YamlValue(double.PositiveInfinity).Scalar.Value);
        Assert.Equal("-.inf", new YamlValue(float.NegativeInfinity).Scalar.Value);
        Assert.Equal(".nan", new YamlValue(Half.NaN).Scalar.Value);
        Assert.Equal("-0.0", new YamlValue(-0.0).Scalar.Value);
        Assert.Equal("-0.0", new YamlValue(-0.0f).Scalar.Value);
        Assert.Equal("0.1", new YamlValue(0.1f).Scalar.Value);
    }

    [Fact]
    public void FromObject()
    {
        var stream = new YamlStream();
        var document = new YamlDocument();
        stream.Add(document);

        var sequence = (YamlSequence)YamlNode.FromObject(new[] { "item 4", "item 5", "item 6" }, expectedType: typeof(string[]));

        sequence.SequenceStart = new SequenceStart(sequence.SequenceStart.Anchor, sequence.SequenceStart.Tag, true, YamlStyle.Flow);

        stream[0].Contents = sequence;

        var serialized = new StringBuilder();

        using var writer = new StringWriter(serialized);
        stream.WriteTo(writer, true);

        Assert.Equal("[item 4, item 5, item 6]", serialized.ToString().Trim());
    }

    [Fact]
    public void DeepClone()
    {
        using var file = Utils.GetResourceStream("test11.yaml");

        using var fileStream = new StreamReader(file);
        var stream = YamlStream.Load(fileStream);

        var serialized = new StringBuilder();
        using var writer = new StringWriter(serialized);
        stream.WriteTo(writer, true);

        var clone = (YamlStream)stream.DeepClone();

        ((YamlMapping)((YamlMapping)clone[0].Contents!)[2].Value!)[new YamlValue("key 2")] = new YamlValue("value 3");

        var serialized2 = new StringBuilder();
        using var writer2 = new StringWriter(serialized2);
        stream.WriteTo(writer2, true);

        Assert.Equal(serialized.ToString(), serialized2.ToString());

        var serialized3 = new StringBuilder();
        using var writer3 = new StringWriter(serialized3);
        clone.WriteTo(writer3, true);

        Assert.NotEqual(serialized.ToString(), serialized3.ToString());
    }

    [Fact]
    public void MappingStringKey()
    {
        using var file = Utils.GetResourceStream("test11.yaml");

        using var fileStream = new StreamReader(file);
        var stream = YamlStream.Load(fileStream);

        Assert.Equal("value 2", ((YamlMapping)((YamlMapping)stream[0].Contents!)[2].Value!)["key 2"]!.ToObject<string>());

        ((YamlMapping)((YamlMapping)stream[0].Contents!)[2].Value!)["key 3"] = new YamlValue("value 3");

        Assert.Equal("key 3", ((YamlMapping)((YamlMapping)stream[0].Contents!)[2].Value!)[2].Key!.ToObject<string>());
        Assert.Equal("value 3", ((YamlMapping)((YamlMapping)stream[0].Contents!)[2].Value!)[2].Value!.ToObject<string>());
    }


    [Fact]
    public void AllowMissingKeyLookup()
    {
        using var file = Utils.GetResourceStream("test11.yaml");

        using var fileStream = new StreamReader(file);
        var stream = YamlStream.Load(fileStream);

        Assert.Null(((YamlMapping)stream[0].Contents!)["Bla"]);
    }


    [Fact]
    public void ToStringTest()
    {
        using var file = Utils.GetResourceStream("test8.yaml");

        using var fileStream = new StreamReader(file);
        var stream = YamlStream.Load(fileStream);

        Assert.Equal("[item 1, item 2, item 3]", stream.ToString());
        Assert.Equal("[item 1, item 2, item 3]", stream[0].Contents!.ToString());
        Assert.Equal("item 1", ((YamlSequence)stream[0].Contents!)[0].ToString());
    }

    [Fact]
    public void StyleTest()
    {
        using var file = Utils.GetResourceStream("test10.yaml");

        using var fileStream = new StreamReader(file);
        var stream = YamlStream.Load(fileStream);

        var seq = (YamlSequence)(YamlContainer)stream[0]!.Contents!;
        Assert.Equal(YamlStyle.Block, seq.Style);
        Assert.Equal(YamlStyle.Block, ((YamlContainer)seq[2]).Style);
        Assert.Equal(YamlStyle.Block, ((YamlContainer)seq[3]).Style);

        seq.Style = YamlStyle.Flow;
        ((YamlContainer)seq[2]).Style = YamlStyle.Flow;
        ((YamlContainer)seq[3]).Style = YamlStyle.Flow;

        var serialized = new StringBuilder();
        using var writer = new StringWriter(serialized);
        stream.WriteTo(writer, true);
        Assert.Single(serialized.ToString().Split(new[] { "\r\n" }, System.StringSplitOptions.RemoveEmptyEntries));
    }

    [Fact]
    public void UnsafeTagTest()
    {
        using var file = Utils.GetResourceStream("dictionaryExplicit.yaml");

        using var fileStream = new StreamReader(file);
        var stream = YamlStream.Load(fileStream);

        var options = new YamlSerializerOptions() { UnsafeAllowDeserializeFromTagTypeName = true };
        var dict = stream[0]!.Contents!.ToObject<object>(options)!;

        Assert.Equal(typeof(Dictionary<string, int>), dict.GetType());
        Assert.Equal("!System.Collections.Generic.Dictionary`2[System.String,System.Int32],mscorlib", stream[0]!.Contents!.Tag);

        stream[0]!.Contents!.Tag = "!System.Collections.Generic.Dictionary`2[System.String,System.Double],mscorlib";

        var dict2 = stream[0]!.Contents!.ToObject<object>(options)!;

        Assert.Equal(typeof(Dictionary<string, double>), dict2.GetType());
    }


    [Fact]
    public void ScalarStyleTest()
    {
        using var file = Utils.GetResourceStream("test6.yaml");

        using var fileStream = new StreamReader(file);
        var stream = YamlStream.Load(fileStream);

        var value = (YamlValue)stream[0]!.Contents!;
        Assert.Equal(ScalarStyle.DoubleQuoted, value.Style);

        value.Style = ScalarStyle.Plain;

        var serialized = new StringBuilder();
        using var writer = new StringWriter(serialized);
        stream.WriteTo(writer, true);
        Assert.StartsWith("!!float 3.14", serialized.ToString());
    }

    [Fact]
    public void IsCanonicalTest()
    {
        using var file = Utils.GetResourceStream("test6.yaml");

        using var fileStream = new StreamReader(file);
        var stream = YamlStream.Load(fileStream);

        var value = (YamlValue?)stream[0]!.Contents;
        Assert.NotNull(value);
        Assert.True(value.IsCanonical);

        value.IsPlainImplicit = true;
        value.Style = ScalarStyle.Plain;

        var serialized = new StringBuilder();
        using var writer = new StringWriter(serialized);
        stream.WriteTo(writer, true);
        Assert.StartsWith("3.14", serialized.ToString());
    }

    [Fact]
    public void Mapping_ReplacingKeyByIndex_RemovesPreviousKeyFromStringLookup()
    {
        var mapping = new YamlMapping();
        mapping["a"] = new YamlValue("1");

        mapping[0] = new KeyValuePair<YamlElement, YamlElement?>(new YamlValue("b"), new YamlValue("2"));

        Assert.False(mapping.ContainsKey("a"));
        Assert.Null(mapping["a"]);
        Assert.True(mapping.ContainsKey("b"));
        Assert.HasCount(1, mapping);
    }

    [Fact]
    public void Mapping_ValuesAndCopyTo_FollowKeyOrder()
    {
        var mapping = new YamlMapping();
        mapping["a"] = new YamlValue("1");
        mapping["b"] = new YamlValue("2");
        mapping.Remove("a");
        mapping["c"] = new YamlValue("3");

        Assert.Equal(new[] { "b", "c" }, mapping.Keys.Select(key => ((YamlValue)key).Value).ToArray());
        Assert.Equal(new[] { "2", "3" }, mapping.Values.Select(value => ((YamlValue)value!).Value).ToArray());

        var pairs = new KeyValuePair<YamlElement, YamlElement?>[2];
        ((ICollection<KeyValuePair<YamlElement, YamlElement?>>)mapping).CopyTo(pairs, 0);
        Assert.Equal(new[] { "b", "c" }, pairs.Select(pair => ((YamlValue)pair.Key).Value).ToArray());
    }

    [Fact]
    public void Mapping_Keys_IsReadOnly()
    {
        var mapping = new YamlMapping();
        mapping["a"] = new YamlValue("1");

        Assert.True(mapping.Keys.IsReadOnly);
        Assert.Throws<NotSupportedException>(() => mapping.Keys.Clear());
        Assert.HasCount(1, mapping);
    }

    [Fact]
    public void Mapping_ContainsPair_ComparesValue()
    {
        var mapping = new YamlMapping();
        var key = new YamlValue("a");
        var value = new YamlValue("1");
        mapping.Add(key, value);
        var collection = (ICollection<KeyValuePair<YamlElement, YamlElement?>>)mapping;

        Assert.True(collection.Contains(new KeyValuePair<YamlElement, YamlElement?>(key, value)));
        Assert.False(collection.Contains(new KeyValuePair<YamlElement, YamlElement?>(key, new YamlValue("1"))));
    }

    [Fact]
    public void ToObject_KeepChompedScalar_KeepsTrailingLineBreaks()
    {
        var stream = YamlStream.Load(new StringReader("--- |+\n  a\n\n"));

        Assert.Equal("a\n\n", stream[0].Contents!.ToObject<string>());
    }

    [Fact]
    public void Value_SettingTag_WritesTag()
    {
        var mapping = new YamlMapping();
        mapping["k"] = new YamlValue("x") { Tag = "tag:example.com,2000:foo" };

        Assert.Equal("k: !<tag:example.com,2000:foo> x", mapping.ToString());
    }
}
