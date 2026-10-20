namespace Meziantou.Framework.CodeDom;

/// <summary>Represents a class declaration.</summary>
/// <example>
/// <code xmlns:ref="urn:meziantou:file-referencer" ref:source="../../../tests/Meziantou.Framework.CodeDom.Tests/CSharpCodeGeneratorTests.cs" ref:region="ClassDeclaration_Example" ref:dedent="true">
/// var cls = new ClassDeclaration("MyClass");
/// cls.Modifiers = Modifiers.Public;
/// cls.BaseType = typeof(object);
/// cls.Members.Add(new MethodDeclaration("MyMethod") { Statements = [] });
///
/// Assert.Equal("""
///     public class MyClass : object
///     {
///         void MyMethod()
///         {
///         }
///     }
///
///     """, cls.ToCsharpString());
/// </code>
/// </example>
public class ClassDeclaration : ClassOrStructDeclaration, IInheritanceParameters
{
    public ClassDeclaration()
        : this(name: null)
    {
    }

    /// <summary>Initializes a new instance of the <see cref="ClassDeclaration"/> class with the specified name.</summary>
    /// <param name="name">The class name.</param>
    public ClassDeclaration(string? name)
    {
        Name = name;
    }

    /// <summary>Gets or sets the base class type.</summary>
    public TypeReference? BaseType { get; set; }
}
