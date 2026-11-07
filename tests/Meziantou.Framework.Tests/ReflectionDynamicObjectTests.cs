using System.Runtime.CompilerServices;

namespace Meziantou.Framework.Tests;

public class ReflectionDynamicObjectTests
{
    [Fact]
    public void Documentation_PrivateMembers()
    {
        #region ReflectionDynamicObject_PrivateMembers
        dynamic counter = new ReflectionDynamicObject(new Counter());

        // Read and write private fields and properties
        counter._count = 41;
        Assert.Equal(41, counter.Count);

        // Call private methods
        counter.Increment();
        Assert.Equal(42, counter._count);

        // Wrap the type to reach static members and to call a public constructor
        dynamic counterType = new ReflectionDynamicObject(typeof(Counter));
        Assert.Equal("counter", (string)counterType.Name);
        dynamic createdCounter = counterType.CreateInstance(10);
        Assert.Equal(10, createdCounter.Count);
        #endregion
    }

#pragma warning disable IDE0051 // Remove unused private members: they are used through reflection
#pragma warning disable IDE0032 // Use auto property
    #region ReflectionDynamicObject_Counter
    private sealed class Counter
    {
        private int _count;

        public Counter()
        {
        }

        public Counter(int count) => _count = count;

        private static string Name => "counter";

        private int Count => _count;

        private void Increment() => _count++;
    }
    #endregion
#pragma warning restore IDE0032
#pragma warning restore IDE0051

    [Fact]
    public void ReflectionDynamicObject()
    {
        var test = new Test();
        dynamic rdo = new ReflectionDynamicObject(test);

        Assert.Equal(42, rdo._privateField);
        Assert.Equal(10, rdo.PrivateProperty);
        Assert.Equal(1, rdo[1]);
        Assert.Equal(3, rdo[1, 2]);
        Assert.Equal("test", (string)rdo["test"]);
        rdo["test"] = "sample";
        Assert.Equal("testsample", (string)rdo["test"]);
        Assert.Equal(42, rdo.PrivateMethod());
        Assert.Equal(1, rdo.PrivateMethodWithOverload());
        Assert.Equal(2, rdo.PrivateMethodWithOverload(1));
        Assert.Equal(3L, rdo.PrivateMethodWithOverload(1L));
        Assert.Equal("test", (string)rdo.ProtectedVirtualMethod());

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
        Assert.Equal("test2", (string)rdo.ProtectedVirtualMethod());
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
        Assert.Throws<ArgumentException>(() => rdo.CreateInstance("tests"));
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

        private string? _indexer;

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

    [Fact]
    public void ReflectionDynamicObject_ConvertToBaseTypeAndInterface()
    {
        var instance = new Test2();
        dynamic rdo = new ReflectionDynamicObject(instance);

        Test baseValue = rdo;
        Assert.Same(instance, baseValue);

        using var stream = new MemoryStream();
        dynamic streamRdo = new ReflectionDynamicObject(stream);
        IDisposable interfaceValue = streamRdo;
        Assert.Same(stream, interfaceValue);
    }

    [Fact]
    public void ReflectionDynamicObject_ConvertIConvertible()
    {
        dynamic rdo = new ReflectionDynamicObject(42);
        Assert.Equal(42L, (long)rdo);
        Assert.Equal(42L, (long?)rdo);
    }

    [Fact]
    public void ReflectionDynamicObject_ConvertToUnrelatedType()
    {
        dynamic rdo = new ReflectionDynamicObject(new Test3());
        Assert.Throws<Microsoft.CSharp.RuntimeBinder.RuntimeBinderException>(() => (string)rdo);
    }

    [Fact]
    public void ReflectionDynamicObject_ConvertStaticWrapper()
    {
        dynamic rdo = new ReflectionDynamicObject(typeof(Test3));
        Assert.Null((string?)rdo);
        Assert.Throws<Microsoft.CSharp.RuntimeBinder.RuntimeBinderException>(() => (int)rdo);
    }

    [Fact]
    public void ReflectionDynamicObject_IndexerExceptionIsRethrown()
    {
        dynamic dictionary = new ReflectionDynamicObject(new Dictionary<string, int>(StringComparer.Ordinal) { ["a"] = 1 });
        Assert.Equal(1, dictionary["a"]);
        Assert.Throws<KeyNotFoundException>(() => { _ = dictionary["missing"]; });

        dynamic list = new ReflectionDynamicObject(new List<int> { 1, 2 });
        Assert.Equal(2, list[1]);
        Assert.Throws<ArgumentOutOfRangeException>(() => { _ = list[5]; });
        Assert.Throws<ArgumentOutOfRangeException>(() => { list[5] = 3; });
    }

    [Fact]
    public void ReflectionDynamicObject_IndexerWithWideningConversion()
    {
        dynamic rdo = new ReflectionDynamicObject(new LongIndexer());
        Assert.Equal(43L, rdo[42]);
    }

    [Fact]
    public void ReflectionDynamicObject_GetDynamicMemberNames_Instance()
    {
        var names = new ReflectionDynamicObject(new Test()).GetDynamicMemberNames().ToList();

        Assert.Contains("_privateField", names);
        Assert.Contains("PrivateProperty", names);
        Assert.DoesNotContain("StaticProperty", names);
        Assert.DoesNotContain("Item", names);
        Assert.DoesNotContain(names, name => name.Contains("k__BackingField", StringComparison.Ordinal));
    }

    [Fact]
    public void ReflectionDynamicObject_GetDynamicMemberNames_Static()
    {
        var names = new ReflectionDynamicObject(typeof(Test)).GetDynamicMemberNames().ToList();

        Assert.Contains("StaticProperty", names);
        Assert.DoesNotContain("_privateField", names);
        Assert.DoesNotContain("PrivateProperty", names);
        Assert.DoesNotContain(names, name => name.Contains("k__BackingField", StringComparison.Ordinal));
    }

    [Fact]
    public void ReflectionDynamicObject_DoesNotPreventAssemblyUnloading()
    {
        var weakReference = WrapTypeOfCollectibleAssembly();
        for (var i = 0; i < 10 && weakReference.IsAlive; i++)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
        }

        Assert.False(weakReference.IsAlive);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference WrapTypeOfCollectibleAssembly()
    {
        var assemblyBuilder = System.Reflection.Emit.AssemblyBuilder.DefineDynamicAssembly(new System.Reflection.AssemblyName("ReflectionDynamicObjectCollectible"), System.Reflection.Emit.AssemblyBuilderAccess.RunAndCollect);
        var moduleBuilder = assemblyBuilder.DefineDynamicModule("ReflectionDynamicObjectCollectible");
        var typeBuilder = moduleBuilder.DefineType("CollectibleType", System.Reflection.TypeAttributes.Public);
        typeBuilder.DefineField("Value", typeof(int), System.Reflection.FieldAttributes.Public);
        var type = typeBuilder.CreateType();

        dynamic rdo = new ReflectionDynamicObject(Activator.CreateInstance(type)!);
        rdo.Value = 1;
        Assert.Equal(1, (int)rdo.Value);

        return new WeakReference(type);
    }

    private sealed class LongIndexer
    {
        public long this[long value] => value + 1;
    }
}
