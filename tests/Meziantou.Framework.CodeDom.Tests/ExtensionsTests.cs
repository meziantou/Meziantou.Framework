using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Meziantou.Framework.CodeDom.Tests
{
    [TestClass]
    public class ExtensionsTests
    {
        [TestMethod]
        public void GetMember()
        {
            var member = new TypeReference(typeof(string)).CreateMemberReferenceExpression("Test", "Name");

            var csharp = new CSharpCodeGenerator().Write(member);

            Assert.AreEqual("string.Test.Name", csharp);
        }
    }
}
