using Meziantou.Framework.Utilities;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Meziantou.Framework.Tests.Utilities
{
    [TestClass]
    public class ReflectionDynamicObjectTests
    {
        [TestMethod]
        public void ReflectionDynamicObject()
        {
            var test = new Test();
            dynamic rdo = new ReflectionDynamicObject(test);

            Assert.AreEqual(42, rdo._privateField);
            Assert.AreEqual(10, rdo.PrivateProperty);
            Assert.AreEqual(1, rdo[1]);
            Assert.AreEqual(3, rdo[1, 2]);
            Assert.AreEqual("test", rdo["test"]);
            rdo["test"] = "sample";
            Assert.AreEqual("testsample", rdo["test"]);
            Assert.AreEqual(42, rdo.PrivateMethod());
            Assert.AreEqual(1, rdo.PrivateMethodWithOverload());
            Assert.AreEqual(2, rdo.PrivateMethodWithOverload(1));
            Assert.AreEqual(3L, rdo.PrivateMethodWithOverload(1L));
            Assert.AreEqual("test", rdo.ProtectedVirtualMethod());

            rdo._privateField = 43;
            Assert.AreEqual(43, rdo._privateField);
            Assert.AreEqual(43, rdo.PrivateField);
        }

        [TestMethod]
        public void ReflectionDynamicObject_Inheritance()
        {
            var test = new Test2();
            dynamic rdo = new ReflectionDynamicObject(test);

            Assert.AreEqual(42, rdo._privateField);
            Assert.AreEqual(11, rdo.PrivateProperty);
            Assert.AreEqual("test2", rdo.ProtectedVirtualMethod());
        }

        [TestMethod]
        public void ReflectionDynamicObject_StaticMethod()
        {
            dynamic rdo = new ReflectionDynamicObject(typeof(Test));
            Assert.AreEqual(1, rdo.Static(1));
        }

        private class Test
        {
            private int _privateField = 42;
            public int PrivateField { get => _privateField; set => _privateField = value; }

            private int PrivateProperty { get; set; } = 10;

            private int PrivateMethod()
            {
                return _privateField;
            }

            private int PrivateMethodWithOverload()
            {
                return 1;
            }

            private int PrivateMethodWithOverload(int a)
            {
                return 1 + a;
            }

            private long PrivateMethodWithOverload(long a)
            {
                return 2 + a;
            }

            protected virtual string ProtectedVirtualMethod()
            {
                return "test";
            }

            private int this[int i] => i;
            private int this[int i, int j] => i + j;

            private string _indexer;
            private string this[string str]
            {
                get => str + _indexer;
                set => _indexer = value;
            }

            private static int Static(int a) => a;
        }

        private class Test2 : Test
        {
            private int PrivateProperty { get; set; } = 11;

            protected override string ProtectedVirtualMethod()
            {
                return "test2";
            }
        }
    }
}
