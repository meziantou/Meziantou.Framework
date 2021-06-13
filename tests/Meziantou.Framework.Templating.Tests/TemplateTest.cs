﻿using System;
using System.Collections.Generic;
using FluentAssertions;
using Xunit;

namespace Meziantou.Framework.Templating.Tests
{
    public class TemplateTest
    {
        [Fact]
        public void Template_TextOnly()
        {
            // Arrange
            var template = new Template();
            template.Load("Sample");
            template.OutputType = typeof(Output);

            // Act 
            var result = template.Run();

            // Assert
            result.Should().Be("Sample");
        }

        [Fact]
        public void Template_CodeOnly()
        {
            // Arrange
            var template = new Template();
            template.Load("<% " + template.OutputParameterName + ".Write(\"Sample\"); %>");

            // Act 
            var result = template.Run();

            // Assert
            result.Should().Be("Sample");
        }

        [Fact]
        public void Template_CodeEval()
        {
            // Arrange
            var template = new Template();
            template.Load("<%= \"Sample\" %>");

            // Act 
            var result = template.Run();

            // Assert
            result.Should().Be("Sample");
        }

        [Fact]
        public void Template_CodeEvalParameter01()
        {
            // Arrange
            var template = new Template();
            template.Load("Hello <%=Name%>!");
            template.AddArgument("Name", typeof(string));

            // Act 
            var result = template.Run("Meziantou");

            // Assert
            result.Should().Be("Hello Meziantou!");
        }

        [Fact]
        public void Template_CodeEvalParameter02()
        {
            // Arrange
            var template = new Template();
            template.Load("Hello <%=Name%>!");
            var arguments = new Dictionary<string, object>(StringComparer.Ordinal)
            {
                { "Name", "Meziantou" },
            };
            template.AddArguments(arguments);

            // Act 
            var result = template.Run(arguments);

            // Assert
            result.Should().Be("Hello Meziantou!");
        }

        [Fact]
        public void Template_Loop01()
        {
            // Arrange
            var template = new Template();
            template.Load("Hello <% for(int i = 1; i <= 5; i++ ) { %><%= i %><% } %>!");

            // Act 
            var result = template.Run();

            // Assert
            result.Should().Be("Hello 12345!");
        }

        [Fact]
        public void Template_UntypedArgument()
        {
            // Arrange
            var template = new Template();
            template.AddArgument("Name");
            template.Load("Hello <%= Name %>!");

            // Act 
            var result = template.Run("John");

            // Assert
            result.Should().Be("Hello John!");
        }

        [Fact]
        public void Template_Debug()
        {
            // Arrange
            var template = new Template
            {
                Debug = true,
            };
            template.Load("Hello <%= \n" +
                          "#if DEBUG\n" +
                          "\"debug\"\n" +
                          "#elif RELEASE\n" +
                          "\"release\"\n" +
                          "#else\n" +
                          "#error Error\n" +
                          "#endif\n" +
                          "%>!");

            // Act 
            var result = template.Run();

            // Assert
            result.Should().Be("Hello debug!");
        }

        [Fact]
        public void Template_Release()
        {
            // Arrange
            var template = new Template
            {
                Debug = false,
            };
            template.Load("Hello <%= \n" +
                          "#if DEBUG\n" +
                          "\"debug\"\n" +
                          "#elif RELEASE\n" +
                          "\"release\"\n" +
                          "#else\n" +
                          "#error Error\n" +
                          "#endif\n" +
                          "%>!");

            // Act 
            var result = template.Run();

            // Assert
            result.Should().Be("Hello release!");
        }
    }
}
