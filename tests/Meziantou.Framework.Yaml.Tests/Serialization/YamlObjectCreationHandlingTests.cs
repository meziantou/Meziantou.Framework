using System.Runtime.InteropServices;
using Meziantou.Framework.Yaml.Serialization;

namespace Meziantou.Framework.Yaml.Tests.Serialization;
public sealed class YamlObjectCreationHandlingTests
{
    private sealed class ChildModel
    {
        public int Existing { get; set; }

        public int Added { get; set; }
    }

    [StructLayout(LayoutKind.Auto)]
    private struct StructChildModel
    {
        public int Existing { get; set; }

        public int Added { get; set; }
    }

    private sealed class ReplaceByDefaultModel
    {
        public ChildModel Child { get; } = new() { Existing = 1 };

        public List<int> Numbers { get; } = [1, 2];
    }

    private sealed class PopulateViaOptionsModel
    {
        public ChildModel Child { get; } = new() { Existing = 1 };

        public List<int> Numbers { get; } = [1, 2];
    }

    [YamlObjectCreationHandling(YamlObjectCreationHandling.Populate)]
    private sealed class PopulateViaTypeAttributeModel
    {
        public ChildModel Child { get; } = new() { Existing = 1 };

        [YamlObjectCreationHandling(YamlObjectCreationHandling.Replace)]
        public List<int> Numbers { get; } = [1, 2];
    }

    private sealed class PopulateStructModel
    {
        [YamlObjectCreationHandling(YamlObjectCreationHandling.Populate)]
        public StructChildModel Child { get; set; } = new() { Existing = 1 };
    }

    private sealed class ReadOnlyPopulateStructModel
    {
        [YamlObjectCreationHandling(YamlObjectCreationHandling.Populate)]
        public StructChildModel Child { get; } = new() { Existing = 1 };
    }

    [Fact]
    public void ReadOnlyMembers_ReplaceByDefault()
    {
        var yaml = """
            Child:
              Added: 2
            Numbers:
              - 3
              - 4
            """;

        var model = YamlSerializer.Deserialize<ReplaceByDefaultModel>(yaml);

        Assert.NotNull(model);
        Assert.Equal(1, model.Child.Existing);
        Assert.Equal(0, model.Child.Added);
        Assert.Equal(new[] { 1, 2 }, model.Numbers);
    }

    [Fact]
    public void PreferredObjectCreationHandlingPopulate_PopulatesReadOnlyMembers()
    {
        var yaml = """
            Child:
              Added: 2
            Numbers:
              - 3
              - 4
            """;

        var model = YamlSerializer.Deserialize<PopulateViaOptionsModel>(
            yaml,
            new YamlSerializerOptions
            {
                PreferredObjectCreationHandling = YamlObjectCreationHandling.Populate,
            });

        Assert.NotNull(model);
        Assert.Equal(1, model.Child.Existing);
        Assert.Equal(2, model.Child.Added);
        Assert.Equal(new[] { 1, 2, 3, 4 }, model.Numbers);
    }

    [Fact]
    public void YamlObjectCreationHandlingAttribute_OnTypeCanPopulate_AndPropertyCanOverrideToReplace()
    {
        var yaml = """
            Child:
              Added: 2
            Numbers:
              - 3
              - 4
            """;

        var model = YamlSerializer.Deserialize<PopulateViaTypeAttributeModel>(yaml);

        Assert.NotNull(model);
        Assert.Equal(1, model.Child.Existing);
        Assert.Equal(2, model.Child.Added);
        Assert.Equal(new[] { 1, 2 }, model.Numbers);
    }

    [Fact]
    public void Populate_OnStructPropertyWithSetter_ModifiesCopyAndAssignsBack()
    {
        var yaml = """
            Child:
              Added: 2
            """;

        var model = YamlSerializer.Deserialize<PopulateStructModel>(yaml);

        Assert.NotNull(model);
        Assert.Equal(1, model.Child.Existing);
        Assert.Equal(2, model.Child.Added);
    }

    [Fact]
    public void Populate_OnReadOnlyStructProperty_Throws()
    {
        var yaml = """
            Child:
              Added: 2
            """;

        var exception = Assert.Throws<InvalidOperationException>(() => YamlSerializer.Deserialize<ReadOnlyPopulateStructModel>(yaml));

        Assert.Contains("value type", exception.Message);
        Assert.Contains("doesn't have a setter", exception.Message);
    }
}
