using FluentAssertions;
using Xunit;

namespace Meziantou.Framework.CodeDom.Tests
{
    public class ExtensionsTests
    {
        [Fact]
        public void GetMember()
        {
            var member = new TypeReferenceExpression(typeof(string)).Member("Test", "Name");

            var csharp = new CSharpCodeGenerator().Write(member);

            csharp.Should().Be("string.Test.Name");
        }

        [Fact]
        public void CreateInvokeMethodExpression_VariableDeclaration()
        {
            var v = new VariableDeclarationStatement("a", typeof(string));
            var member = v.InvokeMethod(Expression.Null());

            var csharp = new CSharpCodeGenerator().Write(member);

            csharp.Should().Be("a(null)");
        }

        [Fact]
        public void CreateInvokeMethodExpression_VariableDeclaration_WithMemberName()
        {
            var v = new VariableDeclarationStatement("a", typeof(string));
            var member = v.InvokeMethod("Test", "Name");

            var csharp = new CSharpCodeGenerator().Write(member);

            csharp.Should().Be("a(\"Test\", \"Name\")");
        }
    }
}
