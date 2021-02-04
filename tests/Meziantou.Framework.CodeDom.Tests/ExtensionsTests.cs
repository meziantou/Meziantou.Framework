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

            Assert.Equal("string.Test.Name", csharp);
        }

        [Fact]
        public void CreateInvokeMethodExpression_VariableDeclaration()
        {
            var v = new VariableDeclarationStatement("a", typeof(string));
            var member = v.InvokeMethod(LiteralExpression.Null());

            var csharp = new CSharpCodeGenerator().Write(member);

            Assert.Equal("a(null)", csharp);
        }

        [Fact]
        public void CreateInvokeMethodExpression_VariableDeclaration_WithMemberName()
        {
            var v = new VariableDeclarationStatement("a", typeof(string));
            var member = v.InvokeMethod("Test", "Name");

            var csharp = new CSharpCodeGenerator().Write(member);

            Assert.Equal("a(\"Test\", \"Name\")", csharp);
        }
    }
}
