using Xunit;

namespace Meziantou.Framework.Tests
{
    public class SlugTests
    {
        [Theory]
        [InlineData("test", "test")]
        [InlineData("TeSt", "TeSt")]
        [InlineData("testé", "teste")]
        [InlineData("TeSt test", "TeSt-test")]
        [InlineData("TeSt test ", "TeSt-test")]
        public void Slug_WithDefaultOptions(string text, string expected)
        {
            var options = new SlugOptions();
            var slug = Slug.Create(text, options);

            Assert.Equal(expected, slug);
        }

        [Theory]
        [InlineData("test", "test")]
        [InlineData("TeSt", "test")]
        public void Slug_Lowercase(string text, string expected)
        {
            var options = new SlugOptions
            {
                ToLower = true,
            };
            var slug = Slug.Create(text, options);

            Assert.Equal(expected, slug);
        }
    }
}
