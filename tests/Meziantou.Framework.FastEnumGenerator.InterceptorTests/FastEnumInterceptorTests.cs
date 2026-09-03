using System.Reflection;

[assembly: Meziantou.Framework.Annotations.FastEnumAttribute(typeof(Meziantou.Framework.FastEnumGenerator.InterceptorTests.InterceptedColor))]
[assembly: Meziantou.Framework.Annotations.FastEnumAttribute(typeof(Meziantou.Framework.FastEnumGenerator.InterceptorTests.InterceptedPermission))]

namespace Meziantou.Framework.FastEnumGenerator.InterceptorTests;

public sealed class FastEnumInterceptorTests
{
    // The BCL oracle must be reached reflectively: a direct call would itself be intercepted.
    private static object? InvokeEnum(string name, Type[] signature, object?[] arguments)
    {
        var method = typeof(Enum).GetMethod(name, BindingFlags.Public | BindingFlags.Static, binder: null, signature, modifiers: null)
            ?? throw new InvalidOperationException($"System.Enum.{name} was not found.");
        return method.Invoke(obj: null, parameters: arguments);
    }

    [Theory]
    [InlineData(InterceptedColor.Alpha)]
    [InlineData(InterceptedColor.Charlie)]
    [InlineData((InterceptedColor)99)]
    public void ToString_MatchesEnumToString(InterceptedColor value)
    {
        var expected = (string)typeof(Enum).GetMethod(nameof(Enum.ToString), BindingFlags.Public | BindingFlags.Instance, binder: null, Type.EmptyTypes, modifiers: null)!
            .Invoke(value, parameters: null)!;
        Assert.Equal(expected, value.ToString());
    }

    [Theory]
    [InlineData(InterceptedPermission.None)]
    [InlineData(InterceptedPermission.Read | InterceptedPermission.Write)]
    [InlineData((InterceptedPermission)9)]
    public void ToString_MatchesEnumToString_ForFlags(InterceptedPermission value)
    {
        var expected = (string)typeof(Enum).GetMethod(nameof(Enum.ToString), BindingFlags.Public | BindingFlags.Instance, binder: null, Type.EmptyTypes, modifiers: null)!
            .Invoke(value, parameters: null)!;
        Assert.Equal(expected, value.ToString());
    }

    [Theory]
    [InlineData(InterceptedColor.Bravo)]
    [InlineData((InterceptedColor)99)]
    public void GetName_MatchesEnumGetName(InterceptedColor value)
    {
        var expected = (string?)InvokeEnum(nameof(Enum.GetName), [typeof(Type), typeof(object)], [typeof(InterceptedColor), value]);
        Assert.Equal(expected, Enum.GetName(value));
    }

    [Theory]
    [InlineData(InterceptedColor.Bravo)]
    [InlineData((InterceptedColor)99)]
    public void IsDefined_MatchesEnumIsDefined(InterceptedColor value)
    {
        var expected = (bool)InvokeEnum(nameof(Enum.IsDefined), [typeof(Type), typeof(object)], [typeof(InterceptedColor), value])!;
        Assert.Equal(expected, Enum.IsDefined(value));
    }

    [Fact]
    public void GetNames_MatchesEnumGetNames()
    {
        var expected = (string[])InvokeEnum(nameof(Enum.GetNames), [typeof(Type)], [typeof(InterceptedColor)])!;
        Assert.Equal(expected, Enum.GetNames<InterceptedColor>());
    }

    [Fact]
    public void GetValues_MatchesEnumGetValues()
    {
        var expected = ((Array)InvokeEnum(nameof(Enum.GetValues), [typeof(Type)], [typeof(InterceptedColor)])!).Cast<InterceptedColor>().ToArray();
        Assert.Equal(expected, Enum.GetValues<InterceptedColor>());
    }

    [Fact]
    public void GetNames_ReturnsACallerOwnedArray()
    {
        // Enum.GetNames hands back a fresh array; mutating it must not affect later calls.
        var names = Enum.GetNames<InterceptedColor>();
        names[0] = "mutated";
        Assert.Equal("Alpha", Enum.GetNames<InterceptedColor>()[0]);
    }

    [Fact]
    public void GetValues_ReturnsACallerOwnedArray()
    {
        var values = Enum.GetValues<InterceptedColor>();
        values[0] = (InterceptedColor)42;
        Assert.Equal(InterceptedColor.Alpha, Enum.GetValues<InterceptedColor>()[0]);
    }

    [Theory]
    [InlineData(InterceptedPermission.Read | InterceptedPermission.Write, InterceptedPermission.Write, true)]
    [InlineData(InterceptedPermission.Read, InterceptedPermission.Write, false)]
    [InlineData(InterceptedPermission.Read, InterceptedPermission.None, true)]
    public void HasFlag_MatchesEnumHasFlag(InterceptedPermission value, InterceptedPermission flag, bool expected)
    {
        Assert.Equal(expected, value.HasFlag(flag));
    }

    [Fact]
    public void HasFlag_ThrowsForAFlagOfAnotherEnumType()
    {
        // Enum.HasFlag rejects a flag whose type differs from the receiver's.
#pragma warning disable CA2248 // Provide correct enum argument to Enum.HasFlag -- deliberately wrong here
        Assert.Throws<ArgumentException>(() => InterceptedPermission.Read.HasFlag(InterceptedColor.Alpha));
#pragma warning restore CA2248
    }

    [Fact]
    public void CallSites_AreActuallyIntercepted()
    {
        // Parity assertions alone cannot tell an intercepted call from the original, so read the IL and
        // check the call targets. Without this the whole suite would pass with interception disabled.
        var called = GetCalledMethods(typeof(FastEnumInterceptorTests).GetMethod(nameof(InterceptedCallSites), BindingFlags.NonPublic | BindingFlags.Static)!).ToArray();

        Assert.All(called, name => Assert.DoesNotContain("System.Enum", name));
        Assert.Contains(called, name => name.Contains("FastEnumInterceptors_", StringComparison.Ordinal) && name.EndsWith(".ToStringFast", StringComparison.Ordinal));
        Assert.Contains(called, name => name.Contains("FastEnumInterceptors_", StringComparison.Ordinal) && name.EndsWith(".IsDefinedFast", StringComparison.Ordinal));
        Assert.Contains(called, name => name.Contains("FastEnumInterceptors_", StringComparison.Ordinal) && name.EndsWith(".GetNameFast", StringComparison.Ordinal));
        Assert.Contains(called, name => name.Contains("FastEnumInterceptors_", StringComparison.Ordinal) && name.EndsWith(".GetNamesFast", StringComparison.Ordinal));
        Assert.Contains(called, name => name.Contains("FastEnumInterceptors_", StringComparison.Ordinal) && name.EndsWith(".GetValuesFast", StringComparison.Ordinal));
        Assert.Contains(called, name => name.Contains("FastEnumInterceptors_", StringComparison.Ordinal) && name.EndsWith(".HasFlagFast", StringComparison.Ordinal));
    }

    private static void InterceptedCallSites()
    {
        _ = InterceptedColor.Bravo.ToString();
        _ = Enum.IsDefined(InterceptedColor.Bravo);
        _ = Enum.GetName(InterceptedColor.Bravo);
        _ = Enum.GetNames<InterceptedColor>();
        _ = Enum.GetValues<InterceptedColor>();
        _ = InterceptedPermission.Read.HasFlag(InterceptedPermission.Write);
    }

    private static IEnumerable<string> GetCalledMethods(MethodBase method)
    {
        var il = method.GetMethodBody()?.GetILAsByteArray() ?? [];
        var module = method.Module;
        for (var i = 0; i < il.Length; i++)
        {
            var opCode = il[i];
            if (opCode is 0x28 or 0x6F) // call / callvirt
            {
                MethodBase? target = null;
                try
                {
                    target = module.ResolveMethod(BitConverter.ToInt32(il, i + 1));
                }
                catch (ArgumentException)
                {
                }

                if (target is not null)
                {
                    yield return target.DeclaringType?.FullName + "." + target.Name;
                }

                i += 4;
            }
            else if (opCode is 0x72 or 0x73 or 0x74 or 0x7B or 0x7D or 0x8C or 0x8D or 0xA5)
            {
                i += 4;
            }
        }
    }
}
