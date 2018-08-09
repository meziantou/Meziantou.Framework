using System;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Meziantou.Framework.CodeDom.Tests
{
    [TestClass]
    public class TypeReferenceTests
    {
        [TestMethod]
        public void TypeReference_FromGenericType()
        {
            // Act
            var typeReference = new TypeReference(typeof(Nullable<>)).MakeGeneric(typeof(int));

            // Assert
            CollectionAssert.AreEqual(new[] { typeof(int).FullName }, typeReference.Parameters.Select(p => p.ClrFullTypeName).ToList());
        }
    }
}
