using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Meziantou.Framework.Tests
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

        [TestMethod]
        public void ReflectionDynamicObject_StaticProperty()
        {
            dynamic rdo = new ReflectionDynamicObject(typeof(Test));
            Assert.AreEqual(12, rdo.StaticProperty);
            rdo.StaticProperty = 42;
            Assert.AreEqual(42, rdo.StaticProperty);
        }

        [TestMethod]
        public void ReflectionDynamicObject_DefaultConstructor()
        {
            dynamic rdo = new ReflectionDynamicObject(typeof(Test3)).CreateInstance();
            Assert.AreEqual(0, rdo.Value);
        }

        [TestMethod]
        public void ReflectionDynamicObject_IntConstructor()
        {
            dynamic rdo = new ReflectionDynamicObject(typeof(Test3)).CreateInstance(10);
            Assert.AreEqual(10, rdo.Value);
        }

        [TestMethod]
        public void ReflectionDynamicObject_StringConstructor()
        {
            dynamic rdo = new ReflectionDynamicObject(typeof(Test3)).CreateInstance("20");
            Assert.AreEqual(20, rdo.Value);
        }

        [TestMethod]
        public void ReflectionDynamicObject_StringConstructor_ThrowException()
        {
            var rdo = new ReflectionDynamicObject(typeof(Test3));
            Assert.ThrowsException<ArgumentException>(() => rdo.CreateInstance("tests"));
        }

        private class Test
        {
#pragma warning disable IDE0032 // Use auto property
            private int _privateField = 42;
#pragma warning restore IDE0032 // Use auto property

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
            private static int StaticProperty { get; set; } = 12;
        }

        private class Test2 : Test
        {
            private int PrivateProperty { get; set; } = 11;

            protected override string ProtectedVirtualMethod()
            {
                return "test2";
            }
        }

        private class Test3
        {
            public int Value { get; set; }

            public Test3()
            {
            }

            public Test3(int value)
            {
                Value = value;
            }

            public Test3(string value)
            {
                Value = int.Parse(value);
            }
        }
    }
}
