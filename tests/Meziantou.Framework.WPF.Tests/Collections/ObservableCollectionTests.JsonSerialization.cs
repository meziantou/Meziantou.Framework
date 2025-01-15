using Meziantou.Framework.WPF.Collections;
using Xunit;

namespace Meziantou.Framework.Windows.Tests;

public sealed partial class ObservableCollectionTests
{
    [Fact]
    public void JsonSerializable()
    {
        var collection = new ConcurrentObservableCollection<int>()
        {
            1,
            2,
            3,
        };

        var json = System.Text.Json.JsonSerializer.Serialize(collection);
        Assert.Equal("[1,2,3]", json);

        var deserialized = System.Text.Json.JsonSerializer.Deserialize<ConcurrentObservableCollection<int>>(json);
        Assert.Equivalent(collection, deserialized);
    }
}
