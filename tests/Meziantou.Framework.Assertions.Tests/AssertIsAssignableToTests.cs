using System.Runtime.InteropServices;
using AssertionsAssert = Meziantou.Framework.Assertions.Assert;

namespace Meziantou.Framework.Assertions.Tests;

public sealed class AssertIsAssignableToTests
{
    [Fact]
    public void Generic_Success()
    {
        object actual = "Hello";

        var result = AssertionsAssert.IsAssignableTo<object>(actual);

        AssertionsAssert.Same(actual, result);
    }

    [Fact]
    public void Type_Success()
    {
        object actual = "Hello";

        var result = AssertionsAssert.IsAssignableTo(typeof(object), actual);

        AssertionsAssert.Same(actual, result);
    }

    [Fact]
    public void FailsWhenTypeIsNotAssignable()
    {
        object actual = "Hello";

        AssertionTestHelpers.Validate(() => AssertionsAssert.IsAssignableTo<int>(actual), """
            Assert.IsAssignableTo() assertion failed.
            Expression: actual
            Expected type: System.Int32
            Actual type:   System.String
            Actual value: "Hello"
            """);
    }

    [Fact]
    public void FailsWhenNull()
    {
        object? actual = null;

        AssertionTestHelpers.Validate(() => AssertionsAssert.IsAssignableTo<string>(actual), """
            Assert.IsAssignableTo() assertion failed.
            Expression: actual
            Expected type: System.String
            Actual type:   <null>
            Actual value: <null>
            """);
    }

    [Fact]
    public void IsNotAssignableTo_Success()
    {
        object actual = "Hello";

        AssertionsAssert.IsNotAssignableTo<int>(actual);
    }

    [Fact]
    public void IsNotAssignableTo_Fails()
    {
        object actual = "Hello";

        AssertionTestHelpers.Validate(() => AssertionsAssert.IsNotAssignableTo<object>(actual), """
            Assert.IsNotAssignableTo() assertion failed.
            Expression: actual
            Not expected assignable type: System.Object
            Actual type:                  System.String
            Actual value: "Hello"
            """);
    }

    [Fact]
    public void ActualIsNotNullAfterTheAssertion()
    {
        object? generic = Environment.TickCount64 >= 0 ? "Hello" : null;
        object? type = Environment.TickCount64 >= 0 ? "Hello" : null;

        AssertionsAssert.IsAssignableTo<string>(generic);
        AssertionsAssert.IsAssignableTo(typeof(string), type);

        AssertionsAssert.Equal(generic.GetHashCode(), type.GetHashCode());
    }

    [Fact]
    public void IsNotAssignableToType_Success()
    {
        object actual = "Hello";

        AssertionsAssert.IsNotAssignableTo(typeof(int), actual);
        AssertionsAssert.IsNotAssignableTo(typeof(string), null);
    }

    [Fact]
    public void IsNotAssignableToType_Fails()
    {
        object actual = "Hello";

        AssertionTestHelpers.Validate(() => AssertionsAssert.IsNotAssignableTo(typeof(object), actual), """
            Assert.IsNotAssignableTo() assertion failed.
            Expression: actual
            Not expected assignable type: System.Object
            Actual type:                  System.String
            Actual value: "Hello"
            """);
    }

    [Fact]
    public void DynamicInterfaceCastable_IsAssignableToTheDynamicInterface()
    {
        object actual = new DynamicCastableObject();

        AssertionsAssert.Same(actual, AssertionsAssert.IsAssignableTo<IDynamicInterface>(actual));
        AssertionsAssert.Same(actual, AssertionsAssert.IsAssignableTo(typeof(IDynamicInterface), actual));
        AssertionsAssert.Throws<AssertionException>(() => AssertionsAssert.IsNotAssignableTo<IDynamicInterface>(actual));
        AssertionsAssert.Throws<AssertionException>(() => AssertionsAssert.IsNotAssignableTo(typeof(IDynamicInterface), actual));
    }

    [Fact]
    public void DynamicInterfaceCastable_IsNotAssignableToAnotherInterface()
    {
        object actual = new DynamicCastableObject();

        AssertionsAssert.IsNotAssignableTo<IDisposable>(actual);
        AssertionsAssert.IsNotAssignableTo(typeof(IDisposable), actual);
        AssertionsAssert.Throws<AssertionException>(() => AssertionsAssert.IsAssignableTo<IDisposable>(actual));
        AssertionsAssert.Throws<AssertionException>(() => AssertionsAssert.IsAssignableTo(typeof(IDisposable), actual));
    }

    public interface IDynamicInterface
    {
        int GetValue();
    }

    [DynamicInterfaceCastableImplementation]
    private interface IDynamicInterfaceImplementation : IDynamicInterface
    {
        int IDynamicInterface.GetValue() => 42;
    }

    private sealed class DynamicCastableObject : IDynamicInterfaceCastable
    {
        public RuntimeTypeHandle GetInterfaceImplementation(RuntimeTypeHandle interfaceType)
        {
            return interfaceType.Equals(typeof(IDynamicInterface).TypeHandle) ? typeof(IDynamicInterfaceImplementation).TypeHandle : default;
        }

        public bool IsInterfaceImplemented(RuntimeTypeHandle interfaceType, bool throwIfNotImplemented)
        {
            if (interfaceType.Equals(typeof(IDynamicInterface).TypeHandle))
                return true;

            if (throwIfNotImplemented)
                throw new InvalidCastException();

            return false;
        }
    }
}
