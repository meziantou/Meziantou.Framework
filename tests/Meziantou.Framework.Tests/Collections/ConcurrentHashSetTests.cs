using System.Diagnostics.CodeAnalysis;
using Meziantou.Framework.Collections;
using Xunit;

namespace Meziantou.Framework.Tests.Collections
{
    public class ConcurrentHashSetTests
    {
        [Fact]
        [SuppressMessage("Assertions", "xUnit2017:Do not use Contains() to check if a value exists in a collection", Justification = "Explicitly test these methods")]
        [SuppressMessage("Assertions", "xUnit2013:Do not use equality check to check for collection size.", Justification = "Explicitly test these methods")]
        public void TestConcurrentHashSet()
        {
            ConcurrentHashSet<int> set = new();
            Assert.True(set.Add(1));
            Assert.True(set.Add(2));
            Assert.True(set.Add(3));
            Assert.False(set.Add(3));

            Assert.True(set.Contains(1));
            Assert.False(set.Contains(4));

            Assert.Equal(3, set.Count);
            Assert.Equal(new[] { 1, 2, 3 }, set.Sort());

            set.Clear();
            Assert.Equal(0, set.Count);

            set.AddRange(4, 5, 6);
            Assert.Equal(new[] { 4, 5, 6 }, set.Sort());
        }
    }
}
