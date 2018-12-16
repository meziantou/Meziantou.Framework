using System;
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
            var template = new Template();
            template.Load("Sample");
            template.OutputType = typeof(Output);

            // Act 
            var result = template.Run();

            // Assert
            Assert.AreEqual("Sample", result);
        }

        [TestMethod]
        public void Template_CodeOnly()
        {
            // Arrange
            var template = new Template();
            template.Load("<% " + template.OutputParameterName + ".Write(\"Sample\"); %>");

            // Act 
            var result = template.Run();

            // Assert
            Assert.AreEqual("Sample", result);
        }

        [TestMethod]
        public void Template_CodeEval()
        {
            // Arrange
            var template = new Template();
            template.Load("<%= \"Sample\" %>");

            // Act 
            var result = template.Run();

            // Assert
            Assert.AreEqual("Sample", result);
        }

        [TestMethod]
        public void Template_CodeEvalParameter01()
        {
            // Arrange
            var template = new Template();
            template.Load("Hello <%=Name%>!");
            template.AddArgument("Name", typeof(string));

            // Act 
            var result = template.Run("Meziantou");

            // Assert
            Assert.AreEqual("Hello Meziantou!", result);
        }

        [TestMethod]
        public void Template_CodeEvalParameter02()
        {
            // Arrange
            var template = new Template();
            template.Load("Hello <%=Name%>!");
            var arguments = new Dictionary<string, object>(StringComparer.Ordinal)
            {
                { "Name", "Meziantou" }
            };
            template.AddArguments(arguments);

            // Act 
            var result = template.Run(arguments);

            // Assert
            Assert.AreEqual("Hello Meziantou!", result);
        }

        [TestMethod]
        public void Template_Loop01()
        {
            // Arrange
            var template = new Template();
            template.Load("Hello <% for(int i = 1; i <= 5; i++ ) { %><%= i %><% } %>!");

            // Act 
            var result = template.Run();

            // Assert
            Assert.AreEqual("Hello 12345!", result);
        }

        [TestMethod]
        public void Template_UntypedArgument()
        {
            // Arrange
            var template = new Template();
            template.AddArgument("Name");
            template.Load("Hello <%= Name %>!");

            // Act 
            var result = template.Run("John");

            // Assert
            Assert.AreEqual("Hello John!", result);
        }

        [TestMethod]
        public void Template_Debug()
        {
            // Arrange
            var template = new Template();
            template.Debug = true;
            template.Load(@"Hello <%= 
#if DEBUG
""debug""
#elif RELEASE
""release""
#else
#error Error
#endif
%>!");

            // Act 
            var result = template.Run();

            // Assert
            Assert.AreEqual("Hello debug!", result);
        }

        [TestMethod]
        public void Template_Release()
        {
            // Arrange
            var template = new Template();
            template.Debug = false;
            template.Load(@"Hello <%= 
#if DEBUG
""debug""
#elif RELEASE
""release""
#else
#error Error
#endif
%>!");

            // Act 
            var result = template.Run();

            // Assert
            Assert.AreEqual("Hello release!", result);
        }
    }
}
