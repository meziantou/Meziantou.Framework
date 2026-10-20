namespace Meziantou.Framework.CodeDom;

/// <summary>Represents a struct declaration.</summary>
/// <example>
/// <code xmlns:ref="urn:meziantou:file-referencer" ref:source="../../../tests/Meziantou.Framework.CodeDom.Tests/CSharpCodeGeneratorTests.cs" ref:region="StructDeclaration_Example" ref:dedent="true">
/// var str = new StructDeclaration("Point");
/// str.Members.Add(new FieldDeclaration("X", typeof(int)));
/// str.Members.Add(new FieldDeclaration("Y", typeof(int)));
///
/// Assert.Equal("""
///     struct Point
///     {
///         int X;
///
///         int Y;
///     }
///
///     """, str.ToCsharpString());
/// </code>
/// </example>
public class StructDeclaration : ClassOrStructDeclaration
{
    public StructDeclaration()
        : this(name: null)
    {
    }

    /// <summary>Initializes a new instance of the <see cref="StructDeclaration"/> class with the specified name.</summary>
    /// <param name="name">The struct name.</param>
    public StructDeclaration(string? name)
    {
        Name = name;
    }
}
