namespace Meziantou.Framework.CodeDom;

/// <summary>Represents a record declaration.</summary>
/// <example>
/// <code xmlns:ref="urn:meziantou:file-referencer" ref:source="../../../tests/Meziantou.Framework.CodeDom.Tests/CSharpCodeGeneratorTests.cs" ref:region="RecordDeclaration_Example" ref:dedent="true">
/// var record = new RecordDeclaration("Person");
///
/// // An accessor without statements is auto-implemented
/// record.Members.Add(new PropertyDeclaration("Name", typeof(string)) { Getter = new PropertyAccessorDeclaration { Statements = null } });
/// record.Members.Add(new PropertyDeclaration("Age", typeof(int)) { Getter = new PropertyAccessorDeclaration { Statements = null } });
///
/// Assert.Equal("""
///     record Person
///     {
///         string Name
///         {
///             get;
///         }
///
///         int Age
///         {
///             get;
///         }
///     }
///
///     """, record.ToCsharpString());
/// </code>
/// </example>
public class RecordDeclaration : ClassOrStructDeclaration, IInheritanceParameters
{
    public RecordDeclaration()
        : this(name: null)
    {
    }

    /// <summary>Initializes a new instance of the <see cref="RecordDeclaration"/> class with the specified name.</summary>
    /// <param name="name">The record name.</param>
    public RecordDeclaration(string? name)
    {
        Name = name;
    }

    public TypeReference? BaseType { get; set; }
}
