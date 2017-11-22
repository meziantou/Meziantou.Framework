using Meziantou.Framework.Utilities;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Meziantou.Framework.Tests.Utilities
{
    [TestClass]
    public class SlugTests
    {
        [DataTestMethod]
        [DataRow("test", "test")]
        [DataRow("TeSt", "TeSt")]
        [DataRow("testé", "teste")]
        [DataRow("TeSt test", "TeSt-test")]
        [DataRow("TeSt test ", "TeSt-test")]
        public void Slug_WithDefaultOptions(string text, string expected)
        {
            var options = new SlugOptions();
            var slug = Slug.Create(text, options);

            Assert.AreEqual(expected, slug);
        }

        [DataTestMethod]
        [DataRow("test", "test")]
        [DataRow("TeSt", "test")]
        public void Slug_Lowercase(string text, string expected)
        {
            var options = new SlugOptions
            {
                ToLower = true
            };
            var slug = Slug.Create(text, options);

            Assert.AreEqual(expected, slug);
        }
    }
}
