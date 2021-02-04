#pragma warning disable MA0048 // File name must match type name
using System;

namespace StronglyTypedIdSample
{
    public static class Class1
    {
        private static void Main()
        {
            TestInt32Method();
        }

        private static void TestInt32Method()
        {
            var a = TestInt32.FromInt32(1);
            var b = TestInt32.FromInt32(1);
            var c = TestInt32.FromInt32(2);
            var d = TestInt32.Parse("3");
            var e = TestInt32.Parse((ReadOnlySpan<char>)"3");

            TestGuid.New();

            Console.WriteLine(d == e);
            Console.WriteLine(a == b);
            Console.WriteLine(a != c);
            Console.WriteLine(b != c);
            Console.WriteLine(b);
        }
    }

    [StronglyTypedId(typeof(int))]
    internal partial struct TestInt32 { }

    [StronglyTypedId(typeof(Guid))]
    internal partial struct TestGuid { }
}
