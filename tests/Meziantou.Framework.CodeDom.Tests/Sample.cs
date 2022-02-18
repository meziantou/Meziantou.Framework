using FluentAssertions;
using Xunit;

namespace Meziantou.Framework.CodeDom.Tests
{
    public sealed class Sample
    {
        [Fact]
        public void StaticMember()
        {
            var expression = new MemberReferenceExpression(typeof(HashCode), nameof(HashCode.Combine));

            expression.TargetObject.Should().NotBeNull();
            expression.Name.Should().NotBeNull();
        }
    }
}
