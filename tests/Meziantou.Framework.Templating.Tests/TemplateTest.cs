using System.Collections.Generic;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Meziantou.Framework.Templating.Tests
{
    [TestClass]
    public class TemplateTest
    {
        [TestMethod]
        public void Template_TextOnly()
        {
            // Arrange
            Template template = new Template();
            template.Load("Sample");
            template.OutputType = typeof(Output);

            // Act 
            string result = template.Run();

            // Assert
            Assert.AreEqual("Sample", result);
        }

        [TestMethod]
        public void Template_CodeOnly()
        {
            // Arrange
            Template template = new Template();
            template.Load("<% " + template.OutputParameterName + ".Write(\"Sample\"); %>");

            // Act 
            string result = template.Run();

            // Assert
            Assert.AreEqual("Sample", result);
        }

        [TestMethod]
        public void Template_CodeEval()
        {
            // Arrange
            Template template = new Template();
            template.Load("<%= \"Sample\" %>");

            // Act 
            string result = template.Run();

            // Assert
            Assert.AreEqual("Sample", result);
        }

        [TestMethod]
        public void Template_CodeEvalParameter01()
        {
            // Arrange
            Template template = new Template();
            template.Load("Hello <%=Name%>!");
            template.AddArgument("Name", typeof(string));

            // Act 
            string result = template.Run("Meziantou");

            // Assert
            Assert.AreEqual("Hello Meziantou!", result);
        }

        [TestMethod]
        public void Template_CodeEvalParameter02()
        {
            // Arrange
            Template template = new Template();
            template.Load("Hello <%=Name%>!");
            var arguments = new Dictionary<string, object>();
            arguments.Add("Name", "Meziantou");
            template.AddArguments(arguments);

            // Act 
            string result = template.Run(arguments);

            // Assert
            Assert.AreEqual("Hello Meziantou!", result);
        }

        [TestMethod]
        public void Template_Loop01()
        {
            // Arrange
            Template template = new Template();
            template.Load("Hello <% for(int i = 1; i <= 5; i++ ) { %><%= i %><% } %>!");

            // Act 
            string result = template.Run();

            // Assert
            Assert.AreEqual("Hello 12345!", result);
        }

        [TestMethod]
        public void Template_UntypedArgument()
        {
            // Arrange
            Template template = new Template();
            template.AddArgument("Name");
            template.Load("Hello <%= Name %>!");
            
            // Act 
            string result = template.Run("John");

            // Assert
            Assert.AreEqual("Hello John!", result);
        }
    }
}
