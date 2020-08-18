using Xunit;

namespace Meziantou.Framework.CodeDom.Tests
{
    public class ExtensionsTests
    {
        [Fact]
        public void GetMember()
        {
            var member = new TypeReferenceExpression(typeof(string)).CreateMemberReferenceExpression("Test", "Name");

            var csharp = new CSharpCodeGenerator().Write(member);

            Assert.Equal("string.Test.Name", csharp);
        }
    }
}
