using System.Collections.Immutable;
using Xunit;

namespace Meziantou.Framework.Tests.Collections;
public sealed class EquatableImmutableArrayTests
{
    [Fact]
    public void EquatableImmutableArray_Empty()
    {
        ImmutableArray<int> immutableArray1 = [0, 1, 2];
        ImmutableArray<int> immutableArray2 = [.. immutableArray1];

        Assert.False(immutableArray1 == immutableArray2);
        Assert.True(immutableArray1.AsEquatableArray() == immutableArray2.AsEquatableArray());
    }
}
