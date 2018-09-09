using System;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Meziantou.Framework.CodeDom.Tests
{
    [TestClass]
    public class VisitorTests
    {
        [TestMethod]
        public void DefaultVisitor_AcceptAnyCodeObject()
        {
            var types = typeof(CodeObject).Assembly.GetTypes()
                .Where(t => t.IsPublic && !t.IsAbstract && !t.ContainsGenericParameters)
                .Where(t => typeof(CodeObject).IsAssignableFrom(t))
                .Where(t => t != typeof(CommentCollection))
                .Where(t => t != typeof(XmlCommentCollection))
                .OrderBy(t => t.FullName)
                .ToList();

            foreach (var type in types)
            {
                try
                {
                    var instance = (CodeObject)Activator.CreateInstance(type);
                    var generator = new Visitor();
                    generator.Visit(instance);
                }
                catch (Exception ex)
                {
                    Assert.Fail("Cannot visit " + type.FullName + ": " + ex);
                }
            }
        }
    }
}
