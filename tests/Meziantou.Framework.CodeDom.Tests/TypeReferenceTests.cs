using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Xunit;

namespace Meziantou.Framework.CodeDom.Tests;

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
        typeReference.ClrFullTypeName.Should().Be(expectedClrFullTypeName);

        if (type.IsArray)
        {
            typeReference.ArrayRank.Should().Be(type.GetArrayRank());
        }
        else
        {
            typeReference.ArrayRank.Should().Be(0);
        }
    }

    [Fact]
    public void TypeReference_FromGenericType()
    {
        // Act
        var typeReference = new TypeReference(typeof(Nullable<>)).MakeGeneric(typeof(int));

        // Assert
        typeReference.ClrFullTypeName.Should().Be("System.Nullable<System.Int32>");
        typeReference.Parameters.Select(p => p.ClrFullTypeName).ToList().Should().BeEquivalentTo(new[] { typeof(int).FullName });
    }

    private sealed class TestNested
    {
        public sealed class TestNested2
        {
        }
    }
}
