using System.Text.Json;
using Xunit;

namespace Meziantou.Framework.Tests
{
    public sealed class FullPathTests
    {
        [Fact]
        public void IsEmpty()
        {
            Assert.True(default(FullPath).IsEmpty);
            Assert.True(FullPath.Empty.IsEmpty);
            Assert.False(FullPath.FromPath("test").IsEmpty);
        }

        [Fact]
        public void CombinePath()
        {
            var actual = FullPath.FromPath("test") / "a" / ".." / "a" / "." / "b";
            var expected = FullPath.FromPath("test", "a", "b");
            Assert.Equal(expected, actual);
        }

        [Theory]
        [InlineData("a", "a")]
        [InlineData("a b", "a b")]
        [InlineData("a/", "a")]
        [InlineData("a/../b", "b")]
        [InlineData(".", ".")]
        public void MakeRelativeTo(string childPath, string expected)
        {
            var rootPath = FullPath.FromPath("test");
            var path1 = FullPath.FromPath("test", childPath);

            Assert.Equal(expected, path1.MakePathRelativeTo(rootPath));
        }

        [Theory]
        [InlineData("test", "test/a")]
        [InlineData("test", "test/a.txt")]
        [InlineData("test", "test/b/a.txt")]
        public void IsChildOf_True(string root, string path)
        {
            var rootPath = FullPath.FromPath(root);
            var childPath = FullPath.FromPath(root, path);

            Assert.True(childPath.IsChildOf(rootPath));
        }

        [Theory]
        [InlineData("test/", "test")]
        [InlineData("test/", "test/")]
        [InlineData("test", "test")]
        [InlineData("test", "test/")]
        [InlineData("test", "abc")]
        [InlineData("test", "../test")]
        [InlineData("test", "test1/b/a.txt")]
        public void IsChildOf_False(string root, string path)
        {
            var rootPath = FullPath.FromPath(root);
            var childPath = FullPath.FromPath(path);

            Assert.False(childPath.IsChildOf(rootPath));
        }

        [Theory]
        [InlineData("test", "abc")]
        [InlineData("test", "../test")]
        [InlineData("test", "test1/b/a.txt")]
        public void Equals_False(string root, string path)
        {
            var rootPath = FullPath.FromPath(root);
            var childPath = FullPath.FromPath(path);

            Assert.NotEqual(childPath, rootPath);
        }

        [Theory]
        [InlineData("test/", "test")]
        [InlineData("test/", "test/")]
        [InlineData("test", "test")]
        [InlineData("test", "test/")]
        [InlineData("test", "./test")]
        public void Equals_True(string root, string path)
        {
            var rootPath = FullPath.FromPath(root);
            var childPath = FullPath.FromPath(path);

            Assert.Equal(childPath, rootPath);
        }

        [Fact]
        public void JsonSerialize_RoundTripEmpty()
        {
            var value = FullPath.Empty;
            Assert.Equal(value, JsonSerializer.Deserialize<FullPath>(JsonSerializer.Serialize(value)));
        }

        [Fact]
        public void JsonSerialize_RoundTripNonEmpty()
        {
            var value = FullPath.FromPath(@"c:\test");
            Assert.Equal(value, JsonSerializer.Deserialize<FullPath>(JsonSerializer.Serialize(value)));
        }

        [Fact]
        public void JsonSerialize_Empty()
        {
            Assert.Equal("\"\"", JsonSerializer.Serialize(FullPath.Empty));
        }

        [Fact]
        public void JsonSerialize_NonEmpty()
        {
            Assert.Equal(@"""c:\\test""", JsonSerializer.Serialize(FullPath.FromPath(@"c:\test")));
        }

        [Fact]
        public void JsonDeserialize_Null()
        {
            Assert.Equal(FullPath.Empty, JsonSerializer.Deserialize<FullPath>(@"null"));
        }

        [Fact]
        public void JsonDeserialize_Empty()
        {
            Assert.Equal(FullPath.Empty, JsonSerializer.Deserialize<FullPath>(@""""""));
        }

        [Fact]
        public void JsonDeserialize_NonEmpty()
        {
            Assert.Equal(FullPath.FromPath(@"c:\test"), JsonSerializer.Deserialize<FullPath>(@"""c:\\test"""));
        }
    }
}
