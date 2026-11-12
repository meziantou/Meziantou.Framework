namespace Meziantou.Framework.JavaScriptEngine;

/// <summary>
/// Generates the JavaScript binding of a class: a source generator adds a static <c>JsClass</c> property to the class,
/// which <see cref="JsRealm.GetClass(JsClass)"/> turns into a JavaScript class. The class and its containing types must be
/// <see langword="partial"/>.
/// </summary>
/// <remarks>
/// <list type="bullet">
/// <item><description>The public constructor is the constructor of the JavaScript class (see <see cref="JsConstructorAttribute"/> when there are several); an abstract class, or a class without public constructor, cannot be created by the scripts.</description></item>
/// <item><description>The public instance methods, properties and fields are the methods and accessors of the prototype; the static ones are properties of the constructor.</description></item>
/// <item><description>A static class is a plain object whose properties are its static members, as <c>Math</c>.</description></item>
/// <item><description>The names are converted to camelCase: use <see cref="JsNameAttribute"/> to choose another name, and <see cref="JsIgnoreAttribute"/> to hide a member.</description></item>
/// </list>
/// </remarks>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class JsExportAttribute : Attribute
{
    /// <summary>Gets or sets the name of the JavaScript class. The default is the name of the .NET class.</summary>
    public string? Name { get; set; }
}

/// <summary>Sets the JavaScript name of a member of a class marked with <see cref="JsExportAttribute"/>, instead of its camelCase name.</summary>
/// <param name="name">The JavaScript name of the member.</param>
[AttributeUsage(AttributeTargets.Method | AttributeTargets.Property | AttributeTargets.Field, AllowMultiple = false, Inherited = false)]
public sealed class JsNameAttribute(string name) : Attribute
{
    /// <summary>Gets the JavaScript name of the member.</summary>
    public string Name { get; } = name;
}

/// <summary>Hides a public member of a class marked with <see cref="JsExportAttribute"/> from the scripts.</summary>
[AttributeUsage(AttributeTargets.Method | AttributeTargets.Property | AttributeTargets.Field | AttributeTargets.Constructor, AllowMultiple = false, Inherited = false)]
public sealed class JsIgnoreAttribute : Attribute
{
}

/// <summary>Selects the constructor of the JavaScript class when a class marked with <see cref="JsExportAttribute"/> has several public constructors.</summary>
[AttributeUsage(AttributeTargets.Constructor, AllowMultiple = false, Inherited = false)]
public sealed class JsConstructorAttribute : Attribute
{
}
