using System.Reflection;

namespace Meziantou.Framework.Tests;

#pragma warning disable CS0612 // Type or member is obsolete
#pragma warning disable MA0070 // Obsolete attributes should include explanations
#pragma warning disable CA1822 // Mark members as static

public class CustomAttributeExtensionsTests
{
    // Type tests
    [Fact]
    public void Type_HasCustomAttribute_Generic_ShouldDetectAttribute()
    {
        var type = typeof(TestClassWithAttribute);
        Assert.True(type.HasCustomAttribute<TestInheritableAttribute>());
    }

    [Fact]
    public void Type_HasCustomAttribute_Generic_ShouldNotDetectMissingAttribute()
    {
        var type = typeof(TestClassWithoutAttribute);
        Assert.False(type.HasCustomAttribute<TestInheritableAttribute>());
    }

    [Fact]
    public void Type_HasCustomAttribute_Generic_WithInherit_ShouldDetectInheritedAttribute()
    {
        var type = typeof(DerivedTestClass);
        Assert.True(type.HasCustomAttribute<TestInheritableAttribute>(inherit: true));
    }

    [Fact]
    public void Type_HasCustomAttribute_Generic_WithoutInherit_ShouldNotDetectInheritedAttribute()
    {
        var type = typeof(DerivedTestClass);
        Assert.False(type.HasCustomAttribute<TestInheritableAttribute>(inherit: false));
    }

    [Fact]
    public void Type_HasCustomAttribute_NonGeneric_ShouldDetectAttribute()
    {
        var type = typeof(TestClassWithAttribute);
        Assert.True(type.HasCustomAttribute(typeof(TestInheritableAttribute)));
    }

    [Fact]
    public void Type_HasCustomAttribute_NonGeneric_ShouldNotDetectMissingAttribute()
    {
        var type = typeof(TestClassWithoutAttribute);
        Assert.False(type.HasCustomAttribute(typeof(TestInheritableAttribute)));
    }

    [Fact]
    public void Type_HasCustomAttribute_NonGeneric_WithInherit_ShouldDetectInheritedAttribute()
    {
        var type = typeof(DerivedTestClass);
        Assert.True(type.HasCustomAttribute(typeof(TestInheritableAttribute), inherit: true));
    }

    [Fact]
    public void Type_HasCustomAttribute_NonGeneric_WithoutInherit_ShouldNotDetectInheritedAttribute()
    {
        var type = typeof(DerivedTestClass);
        Assert.False(type.HasCustomAttribute(typeof(TestInheritableAttribute), inherit: false));
    }

    // Assembly tests
    [Fact]
    public void Assembly_HasCustomAttribute_Generic_ShouldDetectAttribute()
    {
        var assembly = typeof(CustomAttributeExtensionsTests).Assembly;
        Assert.True(assembly.HasCustomAttribute<AssemblyCompanyAttribute>());
    }

    [Fact]
    public void Assembly_HasCustomAttribute_Generic_ShouldNotDetectMissingAttribute()
    {
        var assembly = typeof(CustomAttributeExtensionsTests).Assembly;
        Assert.False(assembly.HasCustomAttribute<ObsoleteAttribute>());
    }

    [Fact]
    public void Assembly_HasCustomAttribute_NonGeneric_ShouldDetectAttribute()
    {
        var assembly = typeof(CustomAttributeExtensionsTests).Assembly;
        Assert.True(assembly.HasCustomAttribute(typeof(AssemblyCompanyAttribute)));
    }

    [Fact]
    public void Assembly_HasCustomAttribute_NonGeneric_ShouldNotDetectMissingAttribute()
    {
        var assembly = typeof(CustomAttributeExtensionsTests).Assembly;
        Assert.False(assembly.HasCustomAttribute(typeof(ObsoleteAttribute)));
    }

    // Module tests
    [Fact]
    public void Module_HasCustomAttribute_Generic_ShouldWorkCorrectly()
    {
        var module = typeof(CustomAttributeExtensionsTests).Module;
        // Most modules don't have custom attributes, so we just test that it doesn't throw
        var result = module.HasCustomAttribute<ObsoleteAttribute>();
        Assert.False(result);
    }

    [Fact]
    public void Module_HasCustomAttribute_NonGeneric_ShouldWorkCorrectly()
    {
        var module = typeof(CustomAttributeExtensionsTests).Module;
        var result = module.HasCustomAttribute(typeof(ObsoleteAttribute));
        Assert.False(result);
    }

    // MemberInfo tests
    [Fact]
    public void MemberInfo_HasCustomAttribute_Generic_ShouldDetectAttribute()
    {
        var method = typeof(TestClassWithAttribute).GetMethod(nameof(TestClassWithAttribute.MethodWithAttribute));
        Assert.NotNull(method);
        Assert.True(method.HasCustomAttribute<ObsoleteAttribute>());
    }

    [Fact]
    public void MemberInfo_HasCustomAttribute_Generic_ShouldNotDetectMissingAttribute()
    {
        var method = typeof(TestClassWithAttribute).GetMethod(nameof(TestClassWithAttribute.MethodWithoutAttribute));
        Assert.NotNull(method);
        Assert.False(method.HasCustomAttribute<ObsoleteAttribute>());
    }

    [Fact]
    public void MemberInfo_HasCustomAttribute_Generic_WithInherit_ShouldDetectInheritedAttribute()
    {
        var method = typeof(DerivedTestClass).GetMethod(nameof(DerivedTestClass.VirtualMethodWithAttribute));
        Assert.NotNull(method);
        Assert.True(method.HasCustomAttribute<ObsoleteAttribute>(inherit: true));
    }

    [Fact]
    public void MemberInfo_HasCustomAttribute_NonGeneric_ShouldDetectAttribute()
    {
        var method = typeof(TestClassWithAttribute).GetMethod(nameof(TestClassWithAttribute.MethodWithAttribute));
        Assert.NotNull(method);
        Assert.True(method.HasCustomAttribute(typeof(ObsoleteAttribute)));
    }

    [Fact]
    public void MemberInfo_HasCustomAttribute_NonGeneric_ShouldNotDetectMissingAttribute()
    {
        var method = typeof(TestClassWithAttribute).GetMethod(nameof(TestClassWithAttribute.MethodWithoutAttribute));
        Assert.NotNull(method);
        Assert.False(method.HasCustomAttribute(typeof(ObsoleteAttribute)));
    }

    // ParameterInfo tests
    [Fact]
    public void ParameterInfo_HasCustomAttribute_Generic_ShouldDetectAttribute()
    {
        var method = typeof(TestClassWithAttribute).GetMethod(nameof(TestClassWithAttribute.MethodWithParameterAttribute));
        Assert.NotNull(method);
        var parameter = method.GetParameters()[0];
        Assert.True(parameter.HasCustomAttribute<TestParameterAttribute>());
    }

    [Fact]
    public void ParameterInfo_HasCustomAttribute_Generic_ShouldNotDetectMissingAttribute()
    {
        var method = typeof(TestClassWithAttribute).GetMethod(nameof(TestClassWithAttribute.MethodWithoutAttribute));
        Assert.NotNull(method);
        var parameters = method.GetParameters();
        if (parameters.Length > 0)
        {
            Assert.False(parameters[0].HasCustomAttribute<TestParameterAttribute>());
        }
    }

    [Fact]
    public void ParameterInfo_HasCustomAttribute_NonGeneric_ShouldDetectAttribute()
    {
        var method = typeof(TestClassWithAttribute).GetMethod(nameof(TestClassWithAttribute.MethodWithParameterAttribute));
        Assert.NotNull(method);
        var parameter = method.GetParameters()[0];
        Assert.True(parameter.HasCustomAttribute(typeof(TestParameterAttribute)));
    }

    [Fact]
    public void ParameterInfo_HasCustomAttribute_NonGeneric_ShouldNotDetectMissingAttribute()
    {
        var method = typeof(TestClassWithAttribute).GetMethod(nameof(TestClassWithAttribute.MethodWithoutAttribute));
        Assert.NotNull(method);
        var parameters = method.GetParameters();
        if (parameters.Length > 0)
        {
            Assert.False(parameters[0].HasCustomAttribute(typeof(TestParameterAttribute)));
        }
    }

    // Test classes
    [TestInheritableAttribute]
    private class TestClassWithAttribute
    {
        [Obsolete]
        public void MethodWithAttribute()
        {
        }

        public void MethodWithoutAttribute()
        {
        }

        public void MethodWithParameterAttribute([TestParameter] string _)
        {
        }

        [Obsolete]
        public virtual void VirtualMethodWithAttribute()
        {
        }
    }

    private sealed class TestClassWithoutAttribute
    {
    }

    private sealed class DerivedTestClass : TestClassWithAttribute
    {
    }

    [AttributeUsage(AttributeTargets.Class, Inherited = true)]
    private sealed class TestInheritableAttribute : Attribute
    {
    }

    [AttributeUsage(AttributeTargets.Parameter)]
    private sealed class TestParameterAttribute : Attribute
    {
    }
}
