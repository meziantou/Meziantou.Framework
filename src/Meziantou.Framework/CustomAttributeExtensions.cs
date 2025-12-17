using System.Reflection;

namespace Meziantou.Framework;

/// <summary>Provides extension methods for checking custom attributes on reflection types.</summary>
public static class CustomAttributeExtensions
{
    // Type extensions
    /// <summary>Determines whether a custom attribute of the specified type is applied to the type.</summary>
    /// <typeparam name="T">The type of attribute to search for.</typeparam>
    /// <param name="type">The type to check.</param>
    /// <returns><see langword="true"/> if the attribute is applied to the type; otherwise, <see langword="false"/>.</returns>
    public static bool HasCustomAttribute<T>(this Type type)
        where T : Attribute
    {
        ArgumentNullException.ThrowIfNull(type);
        return type.IsDefined(typeof(T), inherit: false);
    }

    /// <summary>Determines whether a custom attribute of the specified type is applied to the type.</summary>
    /// <typeparam name="T">The type of attribute to search for.</typeparam>
    /// <param name="type">The type to check.</param>
    /// <param name="inherit"><see langword="true"/> to search the type's inheritance chain to find the attributes; otherwise, <see langword="false"/>.</param>
    /// <returns><see langword="true"/> if the attribute is applied to the type; otherwise, <see langword="false"/>.</returns>
    public static bool HasCustomAttribute<T>(this Type type, bool inherit)
        where T : Attribute
    {
        ArgumentNullException.ThrowIfNull(type);
        return type.IsDefined(typeof(T), inherit);
    }

    /// <summary>Determines whether a custom attribute of the specified type is applied to the type.</summary>
    /// <param name="type">The type to check.</param>
    /// <param name="attributeType">The type of attribute to search for.</param>
    /// <returns><see langword="true"/> if the attribute is applied to the type; otherwise, <see langword="false"/>.</returns>
    public static bool HasCustomAttribute(this Type type, Type attributeType)
    {
        ArgumentNullException.ThrowIfNull(type);
        ArgumentNullException.ThrowIfNull(attributeType);
        return type.IsDefined(attributeType, inherit: false);
    }

    /// <summary>Determines whether a custom attribute of the specified type is applied to the type.</summary>
    /// <param name="type">The type to check.</param>
    /// <param name="attributeType">The type of attribute to search for.</param>
    /// <param name="inherit"><see langword="true"/> to search the type's inheritance chain to find the attributes; otherwise, <see langword="false"/>.</param>
    /// <returns><see langword="true"/> if the attribute is applied to the type; otherwise, <see langword="false"/>.</returns>
    public static bool HasCustomAttribute(this Type type, Type attributeType, bool inherit)
    {
        ArgumentNullException.ThrowIfNull(type);
        ArgumentNullException.ThrowIfNull(attributeType);
        return type.IsDefined(attributeType, inherit);
    }

    // Assembly extensions
    /// <summary>Determines whether a custom attribute of the specified type is applied to the assembly.</summary>
    /// <typeparam name="T">The type of attribute to search for.</typeparam>
    /// <param name="assembly">The assembly to check.</param>
    /// <returns><see langword="true"/> if the attribute is applied to the assembly; otherwise, <see langword="false"/>.</returns>
    public static bool HasCustomAttribute<T>(this Assembly assembly)
        where T : Attribute
    {
        ArgumentNullException.ThrowIfNull(assembly);
        return assembly.IsDefined(typeof(T), inherit: false);
    }

    /// <summary>Determines whether a custom attribute of the specified type is applied to the assembly.</summary>
    /// <typeparam name="T">The type of attribute to search for.</typeparam>
    /// <param name="assembly">The assembly to check.</param>
    /// <param name="inherit">This parameter is ignored for assemblies.</param>
    /// <returns><see langword="true"/> if the attribute is applied to the assembly; otherwise, <see langword="false"/>.</returns>
    public static bool HasCustomAttribute<T>(this Assembly assembly, bool inherit)
        where T : Attribute
    {
        ArgumentNullException.ThrowIfNull(assembly);
        return assembly.IsDefined(typeof(T), inherit);
    }

    /// <summary>Determines whether a custom attribute of the specified type is applied to the assembly.</summary>
    /// <param name="assembly">The assembly to check.</param>
    /// <param name="attributeType">The type of attribute to search for.</param>
    /// <returns><see langword="true"/> if the attribute is applied to the assembly; otherwise, <see langword="false"/>.</returns>
    public static bool HasCustomAttribute(this Assembly assembly, Type attributeType)
    {
        ArgumentNullException.ThrowIfNull(assembly);
        ArgumentNullException.ThrowIfNull(attributeType);
        return assembly.IsDefined(attributeType, inherit: false);
    }

    /// <summary>Determines whether a custom attribute of the specified type is applied to the assembly.</summary>
    /// <param name="assembly">The assembly to check.</param>
    /// <param name="attributeType">The type of attribute to search for.</param>
    /// <param name="inherit">This parameter is ignored for assemblies.</param>
    /// <returns><see langword="true"/> if the attribute is applied to the assembly; otherwise, <see langword="false"/>.</returns>
    public static bool HasCustomAttribute(this Assembly assembly, Type attributeType, bool inherit)
    {
        ArgumentNullException.ThrowIfNull(assembly);
        ArgumentNullException.ThrowIfNull(attributeType);
        return assembly.IsDefined(attributeType, inherit);
    }

    // Module extensions
    /// <summary>Determines whether a custom attribute of the specified type is applied to the module.</summary>
    /// <typeparam name="T">The type of attribute to search for.</typeparam>
    /// <param name="module">The module to check.</param>
    /// <returns><see langword="true"/> if the attribute is applied to the module; otherwise, <see langword="false"/>.</returns>
    public static bool HasCustomAttribute<T>(this Module module)
        where T : Attribute
    {
        ArgumentNullException.ThrowIfNull(module);
        return module.IsDefined(typeof(T), inherit: false);
    }

    /// <summary>Determines whether a custom attribute of the specified type is applied to the module.</summary>
    /// <typeparam name="T">The type of attribute to search for.</typeparam>
    /// <param name="module">The module to check.</param>
    /// <param name="inherit">This parameter is ignored for modules.</param>
    /// <returns><see langword="true"/> if the attribute is applied to the module; otherwise, <see langword="false"/>.</returns>
    public static bool HasCustomAttribute<T>(this Module module, bool inherit)
        where T : Attribute
    {
        ArgumentNullException.ThrowIfNull(module);
        return module.IsDefined(typeof(T), inherit);
    }

    /// <summary>Determines whether a custom attribute of the specified type is applied to the module.</summary>
    /// <param name="module">The module to check.</param>
    /// <param name="attributeType">The type of attribute to search for.</param>
    /// <returns><see langword="true"/> if the attribute is applied to the module; otherwise, <see langword="false"/>.</returns>
    public static bool HasCustomAttribute(this Module module, Type attributeType)
    {
        ArgumentNullException.ThrowIfNull(module);
        ArgumentNullException.ThrowIfNull(attributeType);
        return module.IsDefined(attributeType, inherit: false);
    }

    /// <summary>Determines whether a custom attribute of the specified type is applied to the module.</summary>
    /// <param name="module">The module to check.</param>
    /// <param name="attributeType">The type of attribute to search for.</param>
    /// <param name="inherit">This parameter is ignored for modules.</param>
    /// <returns><see langword="true"/> if the attribute is applied to the module; otherwise, <see langword="false"/>.</returns>
    public static bool HasCustomAttribute(this Module module, Type attributeType, bool inherit)
    {
        ArgumentNullException.ThrowIfNull(module);
        ArgumentNullException.ThrowIfNull(attributeType);
        return module.IsDefined(attributeType, inherit);
    }

    // MemberInfo extensions
    /// <summary>Determines whether a custom attribute of the specified type is applied to the member.</summary>
    /// <typeparam name="T">The type of attribute to search for.</typeparam>
    /// <param name="member">The member to check.</param>
    /// <returns><see langword="true"/> if the attribute is applied to the member; otherwise, <see langword="false"/>.</returns>
    public static bool HasCustomAttribute<T>(this MemberInfo member)
        where T : Attribute
    {
        ArgumentNullException.ThrowIfNull(member);
        return member.IsDefined(typeof(T), inherit: false);
    }

    /// <summary>Determines whether a custom attribute of the specified type is applied to the member.</summary>
    /// <typeparam name="T">The type of attribute to search for.</typeparam>
    /// <param name="member">The member to check.</param>
    /// <param name="inherit"><see langword="true"/> to search the member's inheritance chain to find the attributes; otherwise, <see langword="false"/>.</param>
    /// <returns><see langword="true"/> if the attribute is applied to the member; otherwise, <see langword="false"/>.</returns>
    public static bool HasCustomAttribute<T>(this MemberInfo member, bool inherit)
        where T : Attribute
    {
        ArgumentNullException.ThrowIfNull(member);
        return member.IsDefined(typeof(T), inherit);
    }

    /// <summary>Determines whether a custom attribute of the specified type is applied to the member.</summary>
    /// <param name="member">The member to check.</param>
    /// <param name="attributeType">The type of attribute to search for.</param>
    /// <returns><see langword="true"/> if the attribute is applied to the member; otherwise, <see langword="false"/>.</returns>
    public static bool HasCustomAttribute(this MemberInfo member, Type attributeType)
    {
        ArgumentNullException.ThrowIfNull(member);
        ArgumentNullException.ThrowIfNull(attributeType);
        return member.IsDefined(attributeType, inherit: false);
    }

    /// <summary>Determines whether a custom attribute of the specified type is applied to the member.</summary>
    /// <param name="member">The member to check.</param>
    /// <param name="attributeType">The type of attribute to search for.</param>
    /// <param name="inherit"><see langword="true"/> to search the member's inheritance chain to find the attributes; otherwise, <see langword="false"/>.</param>
    /// <returns><see langword="true"/> if the attribute is applied to the member; otherwise, <see langword="false"/>.</returns>
    public static bool HasCustomAttribute(this MemberInfo member, Type attributeType, bool inherit)
    {
        ArgumentNullException.ThrowIfNull(member);
        ArgumentNullException.ThrowIfNull(attributeType);
        return member.IsDefined(attributeType, inherit);
    }

    // ParameterInfo extensions
    /// <summary>Determines whether a custom attribute of the specified type is applied to the parameter.</summary>
    /// <typeparam name="T">The type of attribute to search for.</typeparam>
    /// <param name="parameter">The parameter to check.</param>
    /// <returns><see langword="true"/> if the attribute is applied to the parameter; otherwise, <see langword="false"/>.</returns>
    public static bool HasCustomAttribute<T>(this ParameterInfo parameter)
        where T : Attribute
    {
        ArgumentNullException.ThrowIfNull(parameter);
        return parameter.IsDefined(typeof(T), inherit: false);
    }

    /// <summary>Determines whether a custom attribute of the specified type is applied to the parameter.</summary>
    /// <typeparam name="T">The type of attribute to search for.</typeparam>
    /// <param name="parameter">The parameter to check.</param>
    /// <param name="inherit"><see langword="true"/> to search the parameter's inheritance chain to find the attributes; otherwise, <see langword="false"/>.</param>
    /// <returns><see langword="true"/> if the attribute is applied to the parameter; otherwise, <see langword="false"/>.</returns>
    public static bool HasCustomAttribute<T>(this ParameterInfo parameter, bool inherit)
        where T : Attribute
    {
        ArgumentNullException.ThrowIfNull(parameter);
        return parameter.IsDefined(typeof(T), inherit);
    }

    /// <summary>Determines whether a custom attribute of the specified type is applied to the parameter.</summary>
    /// <param name="parameter">The parameter to check.</param>
    /// <param name="attributeType">The type of attribute to search for.</param>
    /// <returns><see langword="true"/> if the attribute is applied to the parameter; otherwise, <see langword="false"/>.</returns>
    public static bool HasCustomAttribute(this ParameterInfo parameter, Type attributeType)
    {
        ArgumentNullException.ThrowIfNull(parameter);
        ArgumentNullException.ThrowIfNull(attributeType);
        return parameter.IsDefined(attributeType, inherit: false);
    }

    /// <summary>Determines whether a custom attribute of the specified type is applied to the parameter.</summary>
    /// <param name="parameter">The parameter to check.</param>
    /// <param name="attributeType">The type of attribute to search for.</param>
    /// <param name="inherit"><see langword="true"/> to search the parameter's inheritance chain to find the attributes; otherwise, <see langword="false"/>.</param>
    /// <returns><see langword="true"/> if the attribute is applied to the parameter; otherwise, <see langword="false"/>.</returns>
    public static bool HasCustomAttribute(this ParameterInfo parameter, Type attributeType, bool inherit)
    {
        ArgumentNullException.ThrowIfNull(parameter);
        ArgumentNullException.ThrowIfNull(attributeType);
        return parameter.IsDefined(attributeType, inherit);
    }
}
