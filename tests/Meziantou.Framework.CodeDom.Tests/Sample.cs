using System;
using Xunit;

namespace Meziantou.Framework.CodeDom.Tests
{
    public sealed class Sample
    {
        [Fact]
        public void StaticMember()
        {
            var expression = new MemberReferenceExpression(typeof(HashCode), nameof(HashCode.Combine));

            Assert.NotNull(expression.TargetObject);
            Assert.NotNull(expression.Name);
        }
    }
}
