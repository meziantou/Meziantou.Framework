using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace Meziantou.Framework.CodeDom.Tests
{
    public class TypeReferenceTests
    {
        [Theory]
        [InlineData(typeof(int), "System.Int32")]
        [InlineData(typeof(int[]), "System.Int32[]")]
        [InlineData(typeof(int[,]), "System.Int32[,]")]
        [InlineData(typeof(IList<int[,]>), "System.Collections.Generic.IList<System.Int32[,]>")]
        [InlineData(typeof(int?), "System.Nullable<System.Int32>")]
        [InlineData(typeof(Uri), "System.Uri")]
        [InlineData(typeof(TestNested), "Meziantou.Framework.CodeDom.Tests.TypeReferenceTests+TestNested")]
        [InlineData(typeof(TestNested.TestNested2), "Meziantou.Framework.CodeDom.Tests.TypeReferenceTests+TestNested+TestNested2")]
        public void TypeReference_FromType(Type type, string expectedClrFullTypeName)
        {
            var typeReference = new TypeReference(type);

            // Assert
            Assert.Equal(expectedClrFullTypeName, typeReference.ClrFullTypeName);

            if (type.IsArray)
            {
                Assert.Equal(type.GetArrayRank(), typeReference.ArrayRank);
            }
            else
            {
                Assert.Equal(0, typeReference.ArrayRank);
            }
        }

        [Fact]
        public void TypeReference_FromGenericType()
        {
            // Act
            var typeReference = new TypeReference(typeof(Nullable<>)).MakeGeneric(typeof(int));

            // Assert
            Assert.Equal("System.Nullable<System.Int32>", typeReference.ClrFullTypeName);
            Assert.Equal(new[] { typeof(int).FullName }, typeReference.Parameters.Select(p => p.ClrFullTypeName).ToList());
        }

        private sealed class TestNested
        {
            public sealed class TestNested2
            {
            }
        }
    }
}
