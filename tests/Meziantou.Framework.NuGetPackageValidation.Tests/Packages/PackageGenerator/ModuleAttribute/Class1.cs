using System;

[module: Samplelib.MyMarker]

namespace Samplelib;

[AttributeUsage(AttributeTargets.All)]
internal sealed class MyMarkerAttribute : Attribute
{
}

public static class Class1
{
    /// <summary>Test method</summary>
    public static void A() { }
}
