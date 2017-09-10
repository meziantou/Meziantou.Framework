using Meziantou.Framework.Templating;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Meziantou.Framework.Templating.Tests
{
    [TestClass]
    public class EmailTemplateTest
    {
        [TestMethod]
        public void EmailTemplate_01()
        {
            // Arrange
            HtmlEmailTemplate template = new HtmlEmailTemplate();
            template.Load("Hello {{# \"Meziantou\" }}!");
            HtmlEmailMetadata metadata;

            // Act 
            string result = template.Run(out metadata);

            // Assert
            Assert.AreEqual("Hello Meziantou!", result);
            Assert.AreEqual(null, metadata.Title);
        }

        [TestMethod]
        public void EmailTemplate_Section_01()
        {
            // Arrange
            HtmlEmailTemplate template = new HtmlEmailTemplate();
            template.Load("Hello {{@begin section title}}{{# \"Meziantou\" }}{{@end section}}!");
            HtmlEmailMetadata metadata;

            // Act 
            string result = template.Run(out metadata);

            // Assert
            Assert.AreEqual("Hello Meziantou!", result);
            Assert.AreEqual("Meziantou", metadata.Title);
        }

        [TestMethod]
        public void EmailTemplate_HtmlEncode_01()
        {
            // Arrange
            HtmlEmailTemplate template = new HtmlEmailTemplate();
            template.Load("Hello {{#html \"<Meziantou>\" }}!");
            HtmlEmailMetadata metadata;

            // Act 
            string result = template.Run(out metadata);

            // Assert
            Assert.AreEqual("Hello &lt;Meziantou&gt;!", result);
        }

        [TestMethod]
        public void EmailTemplate_UrlEncode_01()
        {
            // Arrange
            HtmlEmailTemplate template = new HtmlEmailTemplate();
            template.Load("Hello <a href=\"http://www.localhost.com/{{#url \"Sample&Url\" }}\">Meziantou</a>!");
            HtmlEmailMetadata metadata;

            // Act 
            string result = template.Run(out metadata);

            // Assert
            Assert.AreEqual("Hello <a href=\"http://www.localhost.com/Sample%26Url\">Meziantou</a>!", result);
        }

        [TestMethod]
        public void EmailTemplate_HtmlAttributeEncode_01()
        {
            // Arrange
            HtmlEmailTemplate template = new HtmlEmailTemplate();
            template.Load("Hello <a href=\"{{#attr \"Sample&Sample\"}}\">Meziantou</a>!");
            HtmlEmailMetadata metadata;

            // Act 
            string result = template.Run(out metadata);

            // Assert
            Assert.AreEqual("Hello <a href=\"Sample&amp;Sample\">Meziantou</a>!", result);
        }

        [TestMethod]
        public void EmailTemplate_HtmlCode_01()
        {
            // Arrange
            HtmlEmailTemplate template = new HtmlEmailTemplate();
            template.Load("{{html for(int i = 0; i &lt; 3; i++) { }}{{#i}} {{ } }}");
            HtmlEmailMetadata metadata;

            // Act 
            string result = template.Run(out metadata);

            // Assert
            Assert.AreEqual("0 1 2 ", result);
        }

        [TestMethod]
        public void EmailTemplate_Cid_01()
        {
            // Arrange
            HtmlEmailTemplate template = new HtmlEmailTemplate();
            template.Load("<img src=\"{{cid test1.png}}\" /><img src=\"{{cid test2.png}}\" />");
            HtmlEmailMetadata metadata;

            // Act 
            string result = template.Run(out metadata);

            // Assert
            Assert.AreEqual("<img src=\"cid:test1.png\" /><img src=\"cid:test2.png\" />", result);
            Assert.AreEqual(2, metadata.ContentIdentifiers.Count);
            Assert.AreEqual("test1.png", metadata.ContentIdentifiers[0]);
            Assert.AreEqual("test2.png", metadata.ContentIdentifiers[1]);
        }
    }
}
