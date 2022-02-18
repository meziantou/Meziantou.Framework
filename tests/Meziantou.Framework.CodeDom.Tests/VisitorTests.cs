using FluentAssertions;
using Xunit;

namespace Meziantou.Framework.CodeDom.Tests
{
    public class VisitorTests
    {
        [Fact]
        public void DefaultVisitor_AcceptAnyCodeObject()
        {
            var types = typeof(CodeObject).Assembly.GetTypes()
                .Where(t => t.IsPublic && !t.IsAbstract && !t.ContainsGenericParameters && typeof(CodeObject).IsAssignableFrom(t) && t != typeof(CommentCollection) && t != typeof(XmlCommentCollection))
                .OrderBy(t => t.FullName)
                .ToList();

            foreach (var type in types)
            {
                Action visit = () => VisitType(type);
                visit.Should().NotThrow(type.FullName + " should be visitable");
            }
        }

        private static void VisitType(Type type)
        {
            var instance = (CodeObject)Activator.CreateInstance(type);
            var generator = new Visitor();
            generator.Visit(instance);
        }
    }
}
