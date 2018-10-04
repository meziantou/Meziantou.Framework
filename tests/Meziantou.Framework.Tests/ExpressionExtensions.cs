using System;
using System.Linq.Expressions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Meziantou.Framework.Tests
{
    [TestClass]
    public class ExpressionExtensions
    {
        [TestMethod]
        public void AndAlso()
        {
            Expression<Func<int, bool>> func1 = n => n > 0;
            Expression<Func<int, bool>> func2 = n => n < 10;

            var func = func1.AndAlso(func2).Compile();
            Assert.IsTrue(func(1));
            Assert.IsFalse(func(100));
            Assert.IsFalse(func(0));
        }

        [TestMethod]
        public void OrElse()
        {
            Expression<Func<int, bool>> func1 = n => n < 0;
            Expression<Func<int, bool>> func2 = n => n > 10;

            var func = func1.OrElse(func2).Compile();
            Assert.IsFalse(func(1));
            Assert.IsTrue(func(100));
            Assert.IsTrue(func(-1));
        }
    }
}
