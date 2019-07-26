using System;
using System.Linq;
using Xunit;

namespace Meziantou.Framework.CodeDom.Tests
{
    public class TypeReferenceTests
    {
        [Fact]
        public void TypeReference_FromGenericType()
        {
            // Act
            var typeReference = new TypeReference(typeof(Nullable<>)).MakeGeneric(typeof(int));

            // Assert
            Assert.Equal(new[] { typeof(int).FullName }, typeReference.Parameters.Select(p => p.ClrFullTypeName).ToList());
        }
    }
}
