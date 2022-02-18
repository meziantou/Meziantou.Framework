using System.Globalization;
using FluentAssertions;
using Xunit;

namespace Meziantou.Framework.Tests;

public class ReflectionDynamicObjectTests
{
    [Fact]
    public void ReflectionDynamicObject()
    {
        var test = new Test();
        dynamic rdo = new ReflectionDynamicObject(test);

        Assert.Equal(42, rdo._privateField);
        Assert.Equal(10, rdo.PrivateProperty);
        Assert.Equal(1, rdo[1]);
        Assert.Equal(3, rdo[1, 2]);
        Assert.Equal("test", rdo["test"]);
        rdo["test"] = "sample";
        Assert.Equal("testsample", rdo["test"]);
        Assert.Equal(42, rdo.PrivateMethod());
        Assert.Equal(1, rdo.PrivateMethodWithOverload());
        Assert.Equal(2, rdo.PrivateMethodWithOverload(1));
        Assert.Equal(3L, rdo.PrivateMethodWithOverload(1L));
        Assert.Equal("test", rdo.ProtectedVirtualMethod());

        rdo._privateField = 43;
        Assert.Equal(43, rdo._privateField);
        Assert.Equal(43, rdo.PrivateField);
    }

    [Fact]
    public void ReflectionDynamicObject_Inheritance()
    {
        var test = new Test2();
        dynamic rdo = new ReflectionDynamicObject(test);

        Assert.Equal(42, rdo._privateField);
        Assert.Equal(11, rdo.PrivateProperty);
        Assert.Equal("test2", rdo.ProtectedVirtualMethod());
    }

    [Fact]
    public void ReflectionDynamicObject_StaticMethod()
    {
        dynamic rdo = new ReflectionDynamicObject(typeof(Test));
        Assert.Equal(1, rdo.Static(1));
    }

    [Fact]
    public void ReflectionDynamicObject_StaticProperty()
    {
        dynamic rdo = new ReflectionDynamicObject(typeof(Test));
        Assert.Equal(12, rdo.StaticProperty);
        rdo.StaticProperty = 42;
        Assert.Equal(42, rdo.StaticProperty);
    }

    [Fact]
    public void ReflectionDynamicObject_DefaultConstructor()
    {
        dynamic rdo = new ReflectionDynamicObject(typeof(Test3)).CreateInstance();
        Assert.Equal(0, rdo.Value);
    }

    [Fact]
    public void ReflectionDynamicObject_IntConstructor()
    {
        dynamic rdo = new ReflectionDynamicObject(typeof(Test3)).CreateInstance(10);
        Assert.Equal(10, rdo.Value);
    }

    [Fact]
    public void ReflectionDynamicObject_StringConstructor()
    {
        dynamic rdo = new ReflectionDynamicObject(typeof(Test3)).CreateInstance("20");
        Assert.Equal(20, rdo.Value);
    }

    [Fact]
    public void ReflectionDynamicObject_StringConstructor_ThrowException()
    {
        var rdo = new ReflectionDynamicObject(typeof(Test3));
        new Func<object>(() => rdo.CreateInstance("tests")).Should().ThrowExactly<ArgumentException>();
    }

#pragma warning disable IDE0051 // Remove unused private members
#pragma warning disable IDE0032 // Use auto property
#pragma warning disable CA1822 // Make method static
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
        private static int StaticProperty { get; set; } = 12;
    }

    private sealed class Test2 : Test
    {
        private int PrivateProperty { get; set; } = 11;

        protected override string ProtectedVirtualMethod()
        {
            return "test2";
        }
    }

    private sealed class Test3
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
            Value = int.Parse(value, CultureInfo.InvariantCulture);
        }
    }
}
