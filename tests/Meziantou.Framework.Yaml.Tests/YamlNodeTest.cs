using Meziantou.Framework.Yaml.Events;
using Meziantou.Framework.Yaml.Model;
using YamlStream = Meziantou.Framework.Yaml.Model.YamlStream;

namespace Meziantou.Framework.Yaml.Tests;

public class YamlNodeTest
{
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
}
