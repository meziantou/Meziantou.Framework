namespace Meziantou.Framework.CodeDom;

/// <summary>Represents a constructor declaration.</summary>
/// <example>
/// <code xmlns:ref="urn:meziantou:file-referencer" ref:source="../../../tests/Meziantou.Framework.CodeDom.Tests/CSharpCodeGeneratorTests.cs" ref:region="ConstructorDeclaration_Example" ref:dedent="true">
/// var cls = new ClassDeclaration("Person");
/// var ctor = cls.AddMember(new ConstructorDeclaration());
/// ctor.Modifiers = Modifiers.Public;
/// ctor.Arguments.Add(new MethodArgumentDeclaration(typeof(string), "name"));
/// ctor.Statements = new AssignStatement(new MemberReferenceExpression(new ThisExpression(), "_name"), new ArgumentReferenceExpression("name"));
///
/// Assert.Equal("""
///     class Person
///     {
///         public Person(string name)
///         {
///             this._name = name;
///         }
///     }
///
///     """, cls.ToCsharpString());
/// </code>
/// </example>
public class ConstructorDeclaration : MemberDeclaration, IModifiers
{
    public ConstructorDeclaration()
    {
        Arguments = new MethodArgumentCollection(this);
        Statements = new StatementCollection(this);
    }

    public MethodArgumentCollection Arguments { get; }

    public StatementCollection? Statements
    {
        get;
        set => SetParent(ref field, value);
    }

    public ConstructorInitializer? Initializer
    {
        get;
        set => SetParent(ref field, value);
    }

    public Modifiers Modifiers { get; set; }

    public TypeDeclaration? ParentType => Parent.SelfOrAnscestorOfType<TypeDeclaration>();
}
