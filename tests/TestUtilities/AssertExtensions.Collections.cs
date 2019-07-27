using System.Collections.Generic;
using Xunit;

namespace TestUtilities
{
    public static partial class AssertExtensions
    {
        public static void AllItemsAreUnique<T>(IEnumerable<T> items)
        {
            var duplicated = new List<T>();
            var hashSet = new HashSet<T>();
            foreach (var item in items)
            {
                if (!hashSet.Add(item))
                {
                    duplicated.Add(item);
                }
            }

            if (duplicated.Count > 0)
            {
                Assert.True(false, "The collection contains duplicated items: " + string.Join(", ", duplicated));
            }
        }
    }
}
