using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using Meziantou.Framework.Collections;
using Xunit;

namespace Meziantou.Framework.Tests.Collections
{
    public class UnsafeListDictionaryTests
    {
        [Fact]
        [SuppressMessage("Assertions", "xUnit2013:Do not use equality check to check for collection size.", Justification = "Explicitly test these methods")]
        public void TestDictionnary()
        {
            UnsafeListDictionary<int, string> dict = new();
            dict.Add(1, "a");
            dict.Add(2, "b");
            dict.Add(2, "c");

            Assert.Equal(3, dict.Count); // Allows duplicate values
            Assert.True(dict.ContainsKey(1));
            Assert.True(dict.ContainsKey(2));
            Assert.False(dict.ContainsKey(4));

            dict[1] = "d";
            Assert.Equal(3, dict.Count); // Replace existing item

            Assert.Equal(new[] { 1, 2, 2 }, dict.Keys);
            Assert.Equal(new[] { "d", "b", "c" }, dict.Values);

            dict.Clear();
            Assert.Equal(0, dict.Count);

            dict.AddRange(new KeyValuePair<int, string>[] { new(4, "a"), new(5, "e") });
            Assert.Equal(new[] { 4, 5 }, dict.Keys);
        }

        [Fact]
        public void JsonSerializable()
        {
            UnsafeListDictionary<int, string> dict = new();
            dict.Add(1, "a");
            dict.Add(2, "b");
            dict.Add(3, "c");

            var json = JsonSerializer.Serialize(dict);
            Assert.StartsWith("{", json, StringComparison.Ordinal);
            var deserialized = JsonSerializer.Deserialize<UnsafeListDictionary<int, string>>(json);

            Assert.Equal(dict, deserialized);
        }
    }
}
